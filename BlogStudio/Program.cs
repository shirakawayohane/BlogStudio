using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    static readonly JsonSerializerOptions SerializerOptions = new (JsonSerializerDefaults.Web);

    static readonly object ConsoleLockObj = new();
    static Config Config = default!;

    public static async Task Main(string[] args)
    {
        // Json Serialization custom options.
        SerializerOptions.Converters.Add(new DateOnlyConverter());
        SerializerOptions.Converters.Add(new TimeOnlyConverter());

        if (File.Exists(ConfigPath))
        {
            Config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))!;
            if (Config == null) throw new Exception("Failed to read config file.");
        }
        else
        {
            Config = Config.Default;
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config));
        }

        await GenerateAsync();

        if (!args.Contains("--watch")) return;
        using var watch = Watch();

        Process? serveProcess = null;
        if (Config!.ServeCommand != null)
        {
            try
            {
                serveProcess = Process.Start(new ProcessStartInfo()
                {
                    FileName = 
                      RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" 
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "zsh" 
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bash" : throw new PlatformNotSupportedException(),
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = Environment.CurrentDirectory
                });
                serveProcess!.BeginOutputReadLine();
                serveProcess!.OutputDataReceived += static (sender, args) =>
                {
                    Console.WriteLine(args.Data);
                };
                serveProcess!.StandardInput.WriteLine($"{Config.ServeCommand} {OutDir}");
            }
            catch (Exception)
            {
                Console.WriteLine($"Failed to launch process of `{Config.ServeCommand}` command.");
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Watching files for Hot Reloading. Press Ctrl+C twice to stop.");
        Console.ResetColor();
        AutoResetEvent terminatedEvent = new(false);
        Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
        {
            e.Cancel = true;
            terminatedEvent.Set();
        };
        terminatedEvent.WaitOne();
        serveProcess?.Kill();
        Console.WriteLine("Finished.");
    }
}

public record Config(string DefaultLayout, string? ServeCommand)
{
    public static readonly Config Default = new("default", null);
}