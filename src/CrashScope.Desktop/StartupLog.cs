using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace CrashScope.Desktop;

/// <summary>
/// Minimal append-only startup/crash log. Exists so a "double-click, cursor spins, nothing
/// appears" report can be diagnosed from a file instead of guesswork. Never throws.
/// </summary>
public static class StartupLog
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashScope", "logs");

    public static string LogPath => Path.Combine(LogDirectory, "startup.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    public static void WriteBanner()
    {
        string version;
        try
        {
            version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        }
        catch { version = "?"; }

        bool elevated = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        Info($"===== CrashScope.Desktop {version} starting | pid={Environment.ProcessId} | elevated={elevated} | " +
             $"os={Environment.OSVersion} | runtime={Environment.Version} | base={AppContext.BaseDirectory} | " +
             $"cmd={Environment.CommandLine}");
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, line);
            }
            catch
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"), line); } catch { }
            }
        }
        try { Trace.WriteLine(line); } catch { }
    }
}
