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
    const string OutDir = "wwwroot";
    const string EmptyLayout = "__empty";
    const string AssetPath = "assets";
    const string PostPath = "posts";
    const string LayoutPath = "layouts";
    const string FragmentPath = "fragments";
    const string ConfigPath = "config.json";
    const int ThrottleMs = 100;
    static readonly HashSet<Post> Posts = new();
    static readonly HashSet<Fragment> Fragments = new();
    static readonly HashSet<Layout> Layouts = new();
    static JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    static bool FallbackToDefaultLayout = false;

    static Regex ExpressionRegex = new Regex(@"{{\s?([^{]+)\s?}}", RegexOptions.Compiled);
    static Regex PostRegex = new Regex(@"\s?---\s?[\r\n]+([^(---)]+)\s?---\s?[\r\n]+([\r\n\s\S]+)", RegexOptions.Compiled);
    static Regex FragmentRegex = new Regex(@"{%\s?([\w-]+)\s?(.+)?%}", RegexOptions.Compiled);
    static Regex FragmentPropsRegex = new Regex(@"(\s?\w+\s?=\s?""\w+""\s?)*", RegexOptions.Compiled);

    static object ConsoleLockObj = new object();
    static Config? Config;

    public static async Task Main(string[] args)
    {
        // Json Serialization custom options.
        SerializerOptions.Converters.Add(new DateOnlyConverter());
        SerializerOptions.Converters.Add(new TimeOnlyConverter());

        if (File.Exists(ConfigPath))
        {
            Config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath));
        } else
        {
            Config = Config.Default;
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config));
        }

        await GenerateAsync();

        if (!args.Contains("--watch")) return;
        using var watch = Watch();

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

public record Config(string DefaultLayout)
{
    public static readonly Config Default = new Config("default");
}