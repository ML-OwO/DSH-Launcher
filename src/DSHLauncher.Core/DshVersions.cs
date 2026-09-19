using System.Text.Json;
using System.Text.RegularExpressions;

namespace DSHLauncher.Core;

public sealed record DshVersionInfo(string Current, string LatestStable, string? LatestPreview);

/// <summary>沿用 Windows 版的 RC/Alpha 通道选择规则；npm 返回版本顺序决定最新版本。</summary>
public static class DshVersions
{
    public static bool IsNewer(string candidate, string current)
    {
        var a = candidate.Split('-', 2); var b = current.Split('-', 2);
        int main = Version.Parse(a[0]).CompareTo(Version.Parse(b[0]));
        if (main != 0) return main > 0;
        if (a.Length != b.Length) return a.Length == 1;
        if (a.Length == 1) return false;
        var x = a[1].Split('.'); var y = b[1].Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) {
            bool xn = long.TryParse(x[i], out long xv), yn = long.TryParse(y[i], out long yv);
            int cmp = xn && yn ? xv.CompareTo(yv) : xn != yn ? (xn ? -1 : 1) : string.CompareOrdinal(x[i], y[i]);
            if (cmp != 0) return cmp > 0;
        }
        return x.Length > y.Length;
    }

    private static readonly Regex VersionPattern = new(@"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", RegexOptions.Compiled);

    public static DshVersionInfo Parse(string localOutput, string npmVersionsJson)
    {
        Match local = VersionPattern.Match(localOutput);
        if (!local.Success)
            throw new FormatException("无法识别本地 DSH 版本。");

        using JsonDocument document = JsonDocument.Parse(npmVersionsJson);
        var root = document.RootElement;
        string[] versions = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray()
                .Select(item => item.GetString()).OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            JsonValueKind.String => new[] { root.GetString()! },
            _ => throw new FormatException("npm 版本列表格式不正确。")
        };
        string? rc = versions.LastOrDefault(value => Regex.IsMatch(value, @"-rc(?:\.|$)", RegexOptions.IgnoreCase));
        if (rc is null)
            throw new InvalidOperationException("npm 未返回稳定 RC 版本。");
        string? alpha = versions.LastOrDefault(value => Regex.IsMatch(value, @"-alpha(?:\.|$)", RegexOptions.IgnoreCase));
        return new(local.Value, rc, alpha);
    }
}
