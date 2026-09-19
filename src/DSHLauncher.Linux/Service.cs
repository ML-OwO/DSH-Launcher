using System.Diagnostics;
using System.Net.Http;
using DSHLauncher.Core;

namespace DSHLauncher.Linux;

internal sealed class Service
{
    private Process? process;
    private int group;
    private long identity;
    private readonly object gate = new();
    private StartupOutputTracker tracker = new();
    internal Uri? Url { get { lock (gate) return tracker.ServiceUrl; } }
    private string PidFile => Path.Combine(Runtime.Data, "service.pid");

    internal Service()
    {
        // PID + kernel start time prevents stopping a recycled, unrelated PID.
        try {
            var parts = File.ReadAllText(PidFile).Split(' ');
            group = int.Parse(parts[0]); identity = long.Parse(parts[1]);
            if (!Alive) group = 0;
        } catch { group = 0; }
    }

    private static long StartTime(int pid)
    {
        string stat = File.ReadAllText($"/proc/{pid}/stat");
        return long.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19]);
    }

    internal bool Alive
    {
        get {
            try {
                if (group <= 1 || StartTime(group) != identity) return false;
                string stat = File.ReadAllText($"/proc/{group}/stat");
                return stat[stat.LastIndexOf(')') + 2] != 'Z';
            } catch { return false; }
        }
    }

    internal async Task Start()
    {
        if (Alive) return;
        lock (gate) tracker = new();
        process?.Dispose();
        process = new Process { StartInfo = Runtime.Info(new("setsid", new[] { "dsh", "web", "--no-open" })) };
        process.OutputDataReceived += (_, e) => Observe(e.Data);
        process.ErrorDataReceived += (_, e) => Observe(e.Data);
        process.Start();
        group = process.Id;
        identity = StartTime(group);
        File.WriteAllText(PidFile, $"{group} {identity}");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (int i = 0; i < 120; i++) {
            if (!Alive) throw new IOException("DSH 进程在就绪前退出，请在终端运行 dsh web 检查原因。");
            Uri? url;
            lock (gate) url = tracker.ServiceUrl;
            if (url is not null) {
                try {
                    using var response = await http.GetAsync(url);
                    if (response.IsSuccessStatusCode) { Runtime.OpenBrowser(url); return; }
                }
                catch (HttpRequestException) { } catch (TaskCanceledException) { }
            }
            await Task.Delay(500);
        }
        await Stop();
        throw new TimeoutException("DSH 未在规定时间内就绪。");
    }

    private void Observe(string? line)
    {
        if (line is null) return;
        lock (gate) {
            tracker.Observe(line);
            try {
                string path = Path.Combine(Runtime.Data, "service.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".old", true);
                var safe = System.Text.RegularExpressions.Regex.Replace(line, @"(?i)(token=)[^\s&]+", "$1[hidden]");
                File.AppendAllText(path, safe + Environment.NewLine);
            } catch { }
        }
    }

    internal async Task Stop()
    {
        if (Alive) {
            Native.kill(-group, 15);
            for (int i = 0; i < 100 && Alive; i++) await Task.Delay(100);
            if (Alive) { Native.kill(-group, 9); for (int i = 0; i < 20 && Alive; i++) await Task.Delay(100); }
            if (Alive) throw new IOException("DSH 进程仍然运行，关闭失败。");
        }
        group = 0;
        File.Delete(PidFile);
        lock (gate) tracker = new();
    }
}
