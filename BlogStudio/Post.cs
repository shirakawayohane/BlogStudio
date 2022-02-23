using Markdig;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlogStudio;

using static Layout;

public partial class Program
{

    static async Task<Post?> ReadPostAsync(string fullPath, bool ignoreWarnings = false)
    {
        var fileContent = await File.ReadAllTextAsync(fullPath);

        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        // メタデータがある場合は、YAMLとして読む。なければ、ファイル名の情報から生成する
        //if(match.Captures.Count > 0)
        //{
        //    var metaContent = match.Groups[1].Value?.Trim();
        //    post = string.IsNullOrWhiteSpace(metaContent) ? new() : Helpers.YamlDeserializer.Deserialize<Post>(metaContent)!;

        //    var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
        //    post.InputPath = fullPath;
        //    post.Title = title; // Ignore title from meta, even if exists, for simplicity.
        //    post.CreatedAt = createdAt;
        //    post.Content = match.Groups[2].Value!.Trim();
        //    try
        //    {
        //        post.OutputPath = outPath ?? Path.Combine(OutDir, post.Path!);
        //    }
        //    catch (Exception)
        //    {
        //        throw new Exception($"Please specify {nameof(post.Path)} metadata for posts without date");
        //    }
        //}
        //else
        //{
        //    post = new Post();
        //    var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
        //    post.InputPath = fullPath;
        //    post.Title = title;
        //    post.CreatedAt = createdAt;
        //    post.OutputPath = outPath!;
        //    post.Content = content;
        //}
        var (metaStr, mainContent) = Helpers.SeparateMetaAndContent(fileContent);
        var post = metaStr != null ? Helpers.YamlDeserializer.Deserialize<Post>(metaStr) : new();
        var (outPath, createdAt, title) = ParsePostPath(fullPath);
        post.OutputPath = outPath;
        post.CreatedAt = createdAt;
        post.
        if(post.Layout == null) post.Layout = Config?.DefaultLayout ?? EmptyLayout;

        if (!ignoreWarnings && post.Layout != null && !Layouts.Any(x => x.Name == post.Layout))
        {
            lock (ConsoleLockObj)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: The metadata value `layout` is specified for {post.OutputPath}, but no layout named {post.Layout} found.");
                Console.ResetColor();
            }
        }

        return post;
    }

    public static string GetPostOutputDirectoryFromInputPath(string path)
    {
        var date = ParseDateOnly(path);
        return Path.Combine(OutDir, date.Year.ToString("D4"), date.Month.ToString("D2"), date.Day.ToString("D2"));
    }

    public static string GetPostTitleFromPath(string path) => path[11..];

    public static DateOnly ParseDateOnly(string path)
    {
        if (path[4] != '-' || path[8] != '-') throw new ArgumentException("Failed to parse DateOnly. argument must be following format: YYYY-MM-dd");
        return new DateOnly(int.Parse(path[..4]), int.Parse(path[5..7]), int.Parse(path[9..11]));
    }


    static MarkdownPipeline pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    static async Task HandlePostChange(Post post)
    {
        // Possbily null when using StaticPath
        var outputDir = Path.GetDirectoryName(post.OutputPath!);
        if(outputDir != null) Directory.CreateDirectory(outputDir);

        // Update cache
        if (Posts.TryGetValue(post, out var oldPost))
        {
            oldPost.Content = post.Content;
            oldPost.Title = post.Title;
            if (oldPost.OutputPath != post.OutputPath)
            {
                if (File.Exists(oldPost.OutputPath)) File.Delete(oldPost.OutputPath);
                var dirName = Path.GetDirectoryName(oldPost.OutputPath)!;
                if (Helpers.CheckFolderEmpty(dirName))
                {
                    Directory.Delete(dirName);
                }
            }
            oldPost.OutputPath = post.OutputPath;
        }
        else
        {
            Posts.Add(post);
        }

        _ = Layouts.TryGetValue(new Layout(post.Layout, "", null!), out var layout);

        var postHtml = post.InputPath.EndsWith(".md") || post.InputPath.EndsWith(".markdown") 
            ? Markdown.ToHtml(post.Content, pipeline) 
            : post.Content;

        var globals = new Globals(postHtml, Posts, post, Layouts, layout!, null!);

        var html = await Helpers.EmbedVariables(layout!.Content, globals);

        html = await Helpers.EmbedFragments(Fragments, html, globals);

        await File.WriteAllTextAsync(post.OutputPath, html);

        lock (ConsoleLockObj)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"A post `{post.Title}` has (re)loaded.");
            Console.ResetColor();
        }
    }
}

public class Post : IEquatable<Post>
{
    public string Layout { get; set; } = default!; // From metadata. Treat as error if null
    public string Title { get; set; } = default!; // Set manually from path
    public string Content { get; set; } = default!; // Set manually from content
    public DateOnly CreatedAt { get; set; } = default!; // Set manually from path
    public DateOnly? UpdatedAt { get; set; } // Optional
    public string OutputPath { get; set; } = default!; // Set manually from path
    public string InputPath { get; set; } = default!; // Set manually
    public string? Path { get; }

    public override bool Equals(object? obj) => obj is Post post && Equals(post);
    public bool Equals(Post? other) => other is Post post && post.Title.Equals(other.Title) && post.CreatedAt.Equals(other.CreatedAt);
    public override int GetHashCode() => Title.GetHashCode() ^ CreatedAt.GetHashCode();

}


#pragma warning disable IDE1006 // Naming Styles
public record Globals(
    string content,
    IEnumerable<Post> posts,
    Post post,
    IEnumerable<Layout> layouts,
    Layout layout,
    Dictionary<string, object> props
    );
#pragma warning restore IDE1006 // Naming Styles