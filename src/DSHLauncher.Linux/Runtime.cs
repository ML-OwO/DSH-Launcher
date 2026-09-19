using System.Diagnostics;
using DSHLauncher.Core;

namespace DSHLauncher.Linux;

internal static class Runtime
{
    internal static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal static readonly string Data = Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? Path.Combine(Home, ".local/state"), "dsh-launcher");
    internal static readonly string Cache = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Home, ".cache"), "dsh-launcher");

    internal static void Initialize()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Cache);
        // Desktop launchers do not necessarily source .bashrc.
        string[] paths = { Path.Combine(Home, ".local/opt/node-current/bin"), Path.Combine(Home, ".local/bin"), "/usr/local/bin" };
        Environment.SetEnvironmentVariable("PATH", string.Join(":", paths) + ":" + Environment.GetEnvironmentVariable("PATH"));
    }

    internal static ProcessStartInfo Info(CommandSpec spec, bool capture = true)
    {
        var info = new ProcessStartInfo(spec.Executable) { UseShellExecute = false, WorkingDirectory = Home, RedirectStandardOutput = capture, RedirectStandardError = capture };
        foreach (string arg in spec.Arguments) info.ArgumentList.Add(arg);
        return info;
    }

    internal static async Task<string> Run(CommandSpec spec, int seconds = 45)
    {
        using var p = Process.Start(Info(spec)) ?? throw new IOException($"无法执行 {spec.Executable}");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); await p.WaitForExitAsync(); throw new TimeoutException($"{spec.Executable} 执行超时"); }
        string text = await output;
        string errors = await error;
        if (p.ExitCode != 0) throw new IOException($"{spec.Executable} 失败：{errors}");
        return text.Trim();
    }

    internal static void Open(string target) => Process.Start(Info(new("xdg-open", new[] { target }), false))?.Dispose();

    internal static void OpenBrowser(Uri url)
    {
        string browser = Path.Combine(Home, ".local/opt/dsh-browser/opt/google/chrome/google-chrome");
        if (File.Exists(browser))
            Process.Start(Info(new(browser, new[] { "--no-first-run", url.AbsoluteUri }), false))?.Dispose();
        else Open(url.AbsoluteUri);
    }

    internal static void Notify(string title, string message)
    {
        // Consume both output streams and observe errors without blocking GTK.
        _ = Task.Run(async () => {
            try { await Run(new("notify-send", new[] { "--app-name=DSH Launcher", "--icon=" + Path.Combine(Cache, "launcher.ico"), title, message }), 10); }
            catch (Exception e) { Console.Error.WriteLine(e.Message); }
        });
    }
}
