using System.Text.RegularExpressions;

namespace DSHLauncher.Core;

/// <summary>每次启动创建新实例。日志就绪只是信号，调用方仍须检查实际进程和监听端口。</summary>
public sealed class StartupOutputTracker
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex WebUrl = new(@"dsh\s+web:\s*(?<url>https?://127\.0\.0\.1:\d+/\?token=\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public Uri? ServiceUrl { get; private set; }
    public bool SawBrowserOpening { get; private set; }
    public bool IsReady => ServiceUrl is not null && SawBrowserOpening;

    public void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        line = Ansi.Replace(line, "");
        Match match = WebUrl.Match(line);
        if (match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri))
            ServiceUrl = uri;
        if (line.Contains("opening the default browser", StringComparison.OrdinalIgnoreCase))
            SawBrowserOpening = true;
    }
}
