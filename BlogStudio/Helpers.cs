using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlogStudio;
internal static class Helpers
{
    public static bool CheckFolderEmpty(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        var folder = new DirectoryInfo(path);
        if (folder.Exists)
        {
            return folder.GetFileSystemInfos().Length == 0;
        }

        throw new DirectoryNotFoundException();
    }
    public static async Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn)
    {
        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in regex.Matches(input))
        {
            sb.Append(input, lastIndex, match.Index - lastIndex)
              .Append(await replacementFn(match).ConfigureAwait(false));

            lastIndex = match.Index + match.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }

    public static Regex PostRegex = new(@"\s?---\s?[\r\n]+([^(---)]+)\s?---\s?[\r\n]+([\r\n\s\S]+)", RegexOptions.Compiled);
    public static Regex FragmentRegex = new(@"{%\s?([\w-]+)\s?(.+)?%}", RegexOptions.Compiled);
    public static Regex FragmentPropsRegex = new(@"(\s?\w+\s?=\s?""\w+""\s?)*", RegexOptions.Compiled);
    public static Regex ExpressionRegex = new(@"{{\s?([^{]+)\s?}}", RegexOptions.Compiled);

    public static IDeserializer YamlDeserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)  // see height_in_inches in sample yml 
    .Build();

    public static (string? Meta, string Content) SeparateMetaAndContent(string content)
    {
        // メタデータがある場合は、YAMLとして読む。なければ、ファイル名の情報から生成する
        var match = Helpers.PostRegex.Match(content);
        if (match.Captures.Count > 0)
        {
            var meta = match.Groups[1].Value?.Trim()!;
            var mainContent = match.Groups[2].Value!.Trim()!;
            return (meta, mainContent);
        }
        return (null, content);
    }

    static ScriptOptions? _scriptOptions;
    public static ScriptOptions ScriptOptions
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

    public static Task<string> EmbedFragments(IEnumerable<Fragment> fragments, string content, Globals globals)
    {
        return ReplaceAsync(FragmentRegex, content, async match =>
        {
            var fragmentName = match.Groups[1].Value!;
            if (fragments.Any(x => x.Name == fragmentName))
            {
                if (match.Captures.Count == 2)
                {
                    return content;
                }

                var kvps = match.Groups[2].Value!;
                Dictionary<string, object> fragmentProps = new();
                for (int i = 2; i < match.Groups.Count; i++)
                {
                    foreach (Capture capture in FragmentPropsRegex.Match(kvps).Groups[1].Captures)
                    {
                        var kv = capture.Value.Split("=");
                        var key = kv[0].Trim();
                        var valueStr = kv[1].Trim();
                        fragmentProps[key] = (valueStr[0] == '"' ? valueStr[1..^1] :
                            int.TryParse(valueStr, out var intValue) ? intValue :
                            float.TryParse(valueStr, out var floatValue) ? floatValue : "");
                    }
                }

                return await EmbedVariables(content, globals with { props = fragmentProps });
            }
            else
            {

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Fragment `{fragmentName}` not found.");
                Console.ResetColor();
                return "";
            }
        });
    }

    public static Task<string> EmbedVariables(string content, Globals globals)
    {
        return ReplaceAsync(ExpressionRegex, content, async (match) =>
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
    }
}
