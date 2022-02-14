using Markdig;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlogStudio;

public partial class Program
{
    const string OutDir = "publish";
    const int ThrottleMs = 100;
    static readonly HashSet<Post> Posts = new();
    static readonly HashSet<Fragment> Fragments = new();
    static readonly HashSet<Layout> Layouts = new();
    static JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    static bool fallback = false;


    static Regex expressionRegex = new Regex(@"{{\s?([^{]+)\s?}}", RegexOptions.Compiled);
    static Regex metadataRegex = new Regex(@"---[\r\n]+(.+[\r\n\s\S]+)---(.+[\r\n\s\S]+)", RegexOptions.Compiled);
    static Regex fragmentRegex = new Regex(@"{%\s?([\w-]+)\s?(.+)?%}", RegexOptions.Compiled);
    static Regex fragmentKvpRegex = new Regex(@"(\s?\w+\s?=\s?""\w+""\s?)*", RegexOptions.Compiled);

    public static async Task Main(string[] args)
    {
        // Json Serialization custom options.
        SerializerOptions.Converters.Add(new DateOnlyConverter());
        SerializerOptions.Converters.Add(new TimeOnlyConverter());

        Directory.CreateDirectory(OutDir);

        fallback = !File.Exists("layouts/default.html");

        await Task.WhenAll(
            Directory.EnumerateFiles("posts").Select(async postPath =>
            {
                var post = await ReadPost(postPath, true);
                if (post != null) Posts.Add(post);
                // generate after layouts loaded.
            })
        );

        await Task.WhenAll(
            Directory.EnumerateFiles("fragments").Select(async fragmentPath =>
            {
                var fragment = await ReadFragment(fragmentPath);
                if (fragment != null) Fragments.Add(fragment);
                // generate after layouts loaded
            })
        );

        await Task.WhenAll(
            Directory.EnumerateFiles("fragments").Select(async fragmentPath =>
            {
                var fragment = await ReadFragment(fragmentPath);
                Fragments.Add(fragment);
            })
        );

        await Task.WhenAll(
            Directory.EnumerateFiles("layouts").Select(async layoutPath =>
            {
                var layout = await ReadLayout(layoutPath);
                Layouts.Add(layout);
                await HandleLayoutChange(layout);
            })
        );

        // If there is no default.html in layouts, generate automatically.
        if (fallback)
        {
            var defaultLayout = new Layout("default", "{{content}}", new());
            Layouts.Add(defaultLayout);
            await HandleLayoutChange(defaultLayout);
        }

        // Copy asset files.
        foreach (var assetPath in Directory.EnumerateFiles("wwwroot"))
        {
            // 8 = wwwroot + \ or /
            File.Copy(assetPath, Path.Combine(OutDir, assetPath.Substring(8)), true);
        }

        if (!args.Contains("--watch")) return;

        using var postsWatcher = new FileSystemWatcher("posts");
        postsWatcher.NotifyFilter = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite;

        postsWatcher.ChangedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(async e =>
        {
            if (e.ChangeType is not WatcherChangeTypes.Changed) return;
            var post = await ReadPost(e.FullPath);
            if (post != null) await HandlePostChange(post);
        });

        postsWatcher.RenamedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(async e =>
        {
            var (oldOutputPath, _, _, _) = ParsePostPath(e.OldFullPath);

            Posts.RemoveWhere(post => post.OutputPath == oldOutputPath);
            if (File.Exists(oldOutputPath)) File.Delete(oldOutputPath);

            await HandlePostChange(await ReadPost(e.FullPath));
        });

        // do not watch Created Events because newly created file is not valid as a post in almost cases.

        postsWatcher.DeletedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(e =>
        {
            var (outPutPath, _, _, _) = ParsePostPath(e.FullPath);
            Posts.RemoveWhere(post => post.OutputPath == outPutPath);
            if (File.Exists(outPutPath)) File.Delete(outPutPath);
        });

        postsWatcher.Filter = "*.md";
        postsWatcher.EnableRaisingEvents = true;

        var layoutsWatcher = new FileSystemWatcher("layouts");
        layoutsWatcher.NotifyFilter = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite;

        layoutsWatcher.Filter = "*.html";
        layoutsWatcher.EnableRaisingEvents = true;

        layoutsWatcher.ChangedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(async e =>
        {
            var layout = await ReadLayout(e.FullPath);
            await HandleLayoutChange(layout);
        });

        layoutsWatcher.DeletedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(async e =>
        {
            var layoutName = Path.GetFileNameWithoutExtension(e.FullPath);
            Layouts.RemoveWhere(x => x.Name == layoutName);
            await Task.WhenAll(Posts.Where(post => post.Layout == layoutName).Select(async post =>
            {
                await HandlePostChange(post);
            }));
        });

        var assetWatcher = new FileSystemWatcher("wwwroot");
        assetWatcher.ChangedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(asset =>
        {
            File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(8)), true);
        });
        assetWatcher.DeletedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(asset =>
        {
            File.Delete(Path.Combine(OutDir, asset.FullPath.Substring(8)));
        });
        assetWatcher.CreatedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(asset =>
        {
            File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(8)));
        });
        assetWatcher.RenamedAsObservable().Throttle(TimeSpan.FromMilliseconds(ThrottleMs)).Subscribe(asset =>
        {
            File.Move(Path.Combine(OutDir, asset.OldFullPath.Substring(8)), Path.Combine(OutDir, asset.FullPath.Substring(8)), true);
        });
        assetWatcher.NotifyFilter = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite;
        assetWatcher.EnableRaisingEvents = true;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Watching files for Hot Reloading. Press Ctrl+C twice to stop.");
        Console.ResetColor();
        AutoResetEvent terminatedEvent = new AutoResetEvent(false);
        Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
        {
            e.Cancel = true;
            terminatedEvent.Set();
        };
        terminatedEvent.WaitOne();
        Console.WriteLine("Finished.");
    }

}