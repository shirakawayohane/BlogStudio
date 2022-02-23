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

    static readonly IDeserializer serializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)  // see height_in_inches in sample yml 
    .Build();
    static async Task<Post> ReadPost(string fullPath, bool ignoreWarnings = false)
    {
        var content = await File.ReadAllTextAsync(fullPath);

        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        Post post;

        // メタデータがある場合は、YAMLとして読む。なければ、ファイル名の情報から生成する
        var match = Helpers.PostRegex.Match(content);
        if(match.Captures.Count > 0)
        {
            var metaContent = match.Groups[1].Value?.Trim();
            post = string.IsNullOrWhiteSpace(metaContent) ? new() : serializer.Deserialize<Post>(metaContent)!;

            var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
            post.InputPath = fullPath;
            post.Title = title; // Ignore title from meta, even if exists, for simplicity.
            post.CreatedAt = createdAt;
            post.Content = match.Groups[2].Value!.Trim();
            try
            {
                post.OutputPath = outPath ?? Path.Combine(OutDir, post.Path!);
            }
            catch (Exception)
            {
                throw new Exception($"Please specify {nameof(post.Path)} metadata for posts without date");
            }
        }
        else
        {
            post = new Post();
            var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
            post.InputPath = fullPath;
            post.Title = title;
            post.CreatedAt = createdAt;
            post.OutputPath = outPath!;
            post.Content = content;
        }

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

    static (string? OutputPath, DateOnly CreatedAt, string Title, bool staticPath) ParsePostPath(string fullPath)
    {
        var fileNameWithoutEx = Path.GetFileNameWithoutExtension(fullPath);
        string[] hyphenSplitted = fileNameWithoutEx.Split("-");
        if (hyphenSplitted.Length >= 4)
        {
            var date = new DateOnly(int.Parse(hyphenSplitted[0]), int.Parse(hyphenSplitted[1]), int.Parse(hyphenSplitted[2]));
            string outDir = Path.Combine(OutDir, date.Year.ToString("D4"), date.Month.ToString("D2"), date.Day.ToString("D2"));
            var fileName = string.Join("", hyphenSplitted.Skip(3));
            return (Path.Combine(outDir, fileName + ".html"), date, Path.GetFileNameWithoutExtension(fileName), false);
        }
        else
        {
            var date = DateOnly.FromDateTime(File.GetCreationTime(fullPath));
            // posts\ or posts/ = 6 length
            return (null, date, fileNameWithoutEx, true);
        }
    }


    static readonly MarkdownPipeline pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
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