using Markdig;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlogStudio;

using static Layout;

public partial class Program
{
    static ScriptOptions? _scriptOptions;
    static ScriptOptions ScriptOptions
    {
        get
        {
            if (_scriptOptions == null)
            {
                _scriptOptions = ScriptOptions.Default
                .AddReferences("System.Core")
                .AddImports("System.Linq");
            }
            return _scriptOptions;
        }
    }

    static async Task<Post> ReadPost(string fullPath, bool ignoreWarnings = false)
    {
        var content = await File.ReadAllTextAsync(fullPath);

        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        Post post;
        var match = metadataRegex.Match(content);
        if(match.Captures.Count > 0)
        {
            var metaContent = match.Groups[1].Value?.Trim();
            post = string.IsNullOrWhiteSpace(metaContent) ? new() : JsonSerializer.Deserialize<Post>(metaContent, SerializerOptions)!;

            var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
            post.InputPath = fullPath;
            post.Title = title; // Ignore title from meta, even if exists, for simplicity.
            post.CreatedAt = createdAt;
            try
            {
                post.OutputPath = outPath ?? Path.Combine(OutDir, post.StaticPath!);
            }
            catch (Exception)
            {
                throw new Exception("Please specify `staticPath` metadata for posts without date");
            }
            post.MarkdownContent = match.Groups[2].Value!.Trim();
        } else
        {
            post = new Post();
            var (outPath, createdAt, title, staticPath) = ParsePostPath(fullPath);
            post.InputPath = fullPath;
            post.Title = title;
            post.CreatedAt = createdAt;
            post.OutputPath = outPath!;
            post.MarkdownContent = content;
        }

        if (post.Layout == null)
        {
            post.Layout = "default";
            if (fallback)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Metadata `layout` is not specified for {post.OutputPath}.\n Its layout will fall back to default.");
                Console.ResetColor();
            }
        }

        if (!ignoreWarnings && post.Layout != null && !Layouts.Any(x => x.Name == post.Layout))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: The metadata value `layout` is specified for {post.OutputPath}, but no layout named {post.Layout} found.");
            Console.ResetColor();
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


    static async Task HandlePostChange(Post post)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(post.OutputPath)!);
        // Update cache
        if (Posts.TryGetValue(post, out var oldPost))
        {
            oldPost.MarkdownContent = post.MarkdownContent;
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

        var postHtml = Markdown.ToHtml(post.MarkdownContent);

        var globals = new Globals(postHtml, Posts, post, Layouts, layout!, null!);

        // Embed Fragments
        var html = await Helpers.ReplaceAsync(fragmentRegex, layout!.Content, async match =>
        {
            var fragmentName = match.Groups[1].Value!;
            if (Fragments.TryGetValue(new Fragment(fragmentName, null!), out var fragment))
            {
                if (match.Captures.Count == 2)
                {
                    return fragment.Content;
                }

                var kvps = match.Groups[2].Value!;
                Dictionary<string, object> fragmentProps = new();
                for (int i = 2; i < match.Groups.Count; i++)
                {
                    foreach (Capture capture in fragmentKvpRegex.Match(kvps).Groups[1].Captures)
                    {
                        var kv = capture.Value.Split("=");
                        var key = kv[0].Trim();
                        var valueStr = kv[1].Trim();
                        fragmentProps[key] = (object)(valueStr[0] == '"' ? valueStr.Substring(1, valueStr.Length - 2) :
                        int.TryParse(valueStr, out var intValue) ? intValue :
                        float.TryParse(valueStr, out var floatValue) ? floatValue : "");
                    }
                }

                // Replace Variables
                return await Helpers.ReplaceAsync(expressionRegex, fragment!.Content, async (match) =>
                {
                    try
                    {
                        var expStr = match.Groups[1].Value!;
                        expStr = Regex.Replace(expStr, @"props.(\w+)", match =>
                        {
                            return @$"props[""{match.Groups[1].Value!}""]";
                        });
                        var expValue = await CSharpScript.EvaluateAsync(expStr, ScriptOptions, globals: globals with
                        {
                            props = fragmentProps
                        });

                        return expValue switch
                        {
                            string str => str,
                            IEnumerable<string> strs => string.Join(Environment.NewLine, strs),
                            _ => expValue?.ToString() ?? ""
                        };
                    }
                    catch (Exception)
                    {
                        return "";
                    }
                });
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Fragment `` in post `{post.InputPath}` not found.");
                Console.ResetColor();
                return "";
            }
        });

        // Replace Variables
        html = await Helpers.ReplaceAsync(expressionRegex, html, async (match) =>
        {
            var expStr = match.Groups[1].Value!;
            try
            {
                var expValue = await CSharpScript.EvaluateAsync(expStr,
                    options: ScriptOptions,
                    globals: globals
                );
        
                return expValue switch
                {
                    string str => str,
                    IEnumerable<string> strs => string.Join(Environment.NewLine, strs),
                    _ => expValue?.ToString() ?? ""
                };
            }
            catch (Exception)
            {
                return "";
            }
        });

        await File.WriteAllTextAsync(post.OutputPath, html);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"A post `{post.Title}` has (re)loaded.");
        Console.ResetColor();
    }
}

public class Post : IEquatable<Post>
{
    public string Layout { get; set; } = default!; // From metadata. Treat as error if null
    public string Title { get; set; } = default!; // Set manually from path
    public string MarkdownContent { get; set; } = default!; // Set manually from content
    public DateOnly CreatedAt { get; set; } = default!; // Set manually from path
    public DateOnly? UpdatedAt { get; set; } // Optional
    public string OutputPath { get; set; } = default!; // Set manually from path
    public string? StaticPath { get; set; }
    public string InputPath { get; set; } = default!; // Set manually

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