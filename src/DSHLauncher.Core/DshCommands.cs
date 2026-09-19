using System.Text.RegularExpressions;

namespace DSHLauncher.Core;

/// <summary>参数必须交给 ProcessStartInfo.ArgumentList，不要拼接为 shell 命令。</summary>
public sealed record CommandSpec(string Executable, IReadOnlyList<string> Arguments);

public static class DshCommands
{
    public const string PackageName = "@deepseek-ai/dsh";
    public static CommandSpec LocalVersion => new("dsh", new[] { "--version" });
    public static CommandSpec PublishedVersions => new("npm", new[] { "view", PackageName, "versions", "--json" });
    public static CommandSpec Start => new("dsh", new[] { "web" });
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");

    public static CommandSpec Update(string version)
    {
        if (string.IsNullOrEmpty(version) || !Regex.IsMatch(version, @"\A[0-9A-Za-z.+-]+\z"))
            throw new ArgumentException("目标版本号格式不正确。", nameof(version));
        return new("npm", new[] { "install", "-g", $"{PackageName}@{version}", "--force", "--progress=true", "--loglevel=info" });
    }
}

public enum ServiceState { Stopped, Starting, Running, Stopping, Updating }
