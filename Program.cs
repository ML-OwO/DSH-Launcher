using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.WinUI.Notifications;

#nullable enable

namespace DSHLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "DSHLauncher.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("DSH Launcher 已在运行。", "DSH Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private const string PackageName = "@deepseek-ai/dsh";
    private static readonly Regex VersionPattern = new(@"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", RegexOptions.Compiled);
    private static readonly Regex WebUrlPattern = new(@"dsh\s+web:\s*(?<url>https?://127\.0\.0\.1:\d+/\?token=\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListeningPattern = new(@"^\s*TCP\s+\S+:(?<port>\d+)\s+\S+\s+LISTENING\s+(?<pid>\d+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly StatusMenuItem statusItem;
    private readonly ToolStripMenuItem updateItem;
    private readonly ToolStripMenuItem startItem;
    private readonly ToolStripMenuItem stopItem;
    private readonly Icon trayIcon;
    private readonly string configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    private readonly Queue<string> recentOutput = new();

    private Process? commandProcess;
    private Process? listenerProcess;
    private string? latestStableVersion;
    private string? serviceUrl;
    private bool sawBrowserOpening;
    private bool readinessTriggered;
    private bool suppressExitNotifications;
    private bool shuttingDown;
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 2500 };
    private bool reconciling;
    private int generation;
    private ServiceState serviceState = ServiceState.Stopped;

    public TrayContext(bool initialize = true)
    {
        trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();

        statusItem = new StatusMenuItem("DSH 服务：未运行")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 45, 45),
            DotColor = Color.FromArgb(135, 135, 135),
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(2, 5, 2, 1),
            AutoSize = true
        };
        updateItem = CreateMenuItem("正在检查版本…", async (_, _) => await UpdateDshAsync());
        updateItem.Enabled = false;
        updateItem.Visible = false;
        startItem = CreateMenuItem("启动 DSH 服务", async (_, _) => await StartDshAsync());
        stopItem = CreateMenuItem("关闭 DSH 服务", async (_, _) => await StopDshAsync(showNotification: true, forUpdate: false));

        menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 10F),
            BackColor = Color.FromArgb(248, 248, 248),
            ForeColor = Color.FromArgb(28, 28, 28),
            Padding = new Padding(6),
            MinimumSize = new Size(270, 0),
            DropShadowEnabled = true,
            Renderer = new LauncherRenderer()
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(updateItem);
        menu.Items.Add(CreateMenuItem("打开 DSH 配置目录", (_, _) => OpenConfigDirectory()));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("退出 DSH Launcher", (_, _) => ExitThread()));
        _ = menu.Handle;
        menu.Opened += (_, _) =>
        {
            PrepareMenuWindow();
            menu.BeginInvoke((Action)(() =>
            {
                if (menu.Visible) PrepareMenuWindow();
            }));
        };
        menu.Closed += (_, _) => NativeMethods.SetWindowPos(
            menu.Handle,
            NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        tray = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "DSH 服务：未运行",
            Visible = true
        };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button is MouseButtons.Left or MouseButtons.Right)
            {
                if (menu.Visible)
                    menu.Close();
                else
                    menu.Show(Cursor.Position);
            }
        };

        UpdateMenu();
        monitor.Tick += async (_, _) => await ReconcileAsync();
        if (initialize)
        {
            monitor.Start();
            menu.BeginInvoke((Action)(async () => await InitializeAsync()));
        }
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler) => new(text, null, handler)
    {
        Padding = new Padding(12, 6, 12, 6),
        Margin = new Padding(2, 1, 2, 1),
        AutoSize = true
    };

    private static ToolStripSeparator CreateSeparator() => new() { Margin = new Padding(3, 5, 3, 5) };

    private void PrepareMenuWindow()
    {
        Region? oldRegion = menu.Region;
        menu.Region = null;
        oldRegion?.Dispose();
        int cornerPreference = 2;
        _ = NativeMethods.DwmSetWindowAttribute(
            menu.Handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        _ = NativeMethods.SetWindowPos(
            menu.Handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
        _ = NativeMethods.SetForegroundWindow(menu.Handle);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private async Task InitializeAsync()
    {
        await CheckVersionsAsync(showNotification: true);
        if (!shuttingDown)
            await StartDshAsync();
    }

    private async Task CheckVersionsAsync(bool showNotification)
    {
        updateItem.Visible = true;
        updateItem.Enabled = false;
        updateItem.Text = "正在检查版本…";

        try
        {
            VersionInfo info = await Task.Run(GetVersionInfo);
            latestStableVersion = info.LatestRc;

            if (!string.Equals(info.Local, info.LatestRc, StringComparison.OrdinalIgnoreCase))
            {
                updateItem.Text = $"更新至 {info.LatestRc} 稳定版本";
                updateItem.Enabled = serviceState is not ServiceState.Updating;
                if (showNotification)
                    Notify("DSH 发现稳定版本更新", $"当前版本：{info.Local}\n最新稳定版本：{info.LatestRc}");
            }
            else
            {
                updateItem.Visible = false;
                if (showNotification)
                    Notify("DSH 已是最新稳定版本", $"当前版本：{info.Local}\n最新预览版本：{info.LatestAlpha ?? "暂无"}");
            }
        }
        catch (Exception ex)
        {
            latestStableVersion = null;
            updateItem.Visible = false;
            if (showNotification)
                Notify("DSH 版本检查失败", ToSingleLine(ex.Message));
        }
    }

    private static VersionInfo GetVersionInfo()
    {
        CommandResult localResult = RunCommand("dsh --version", timeoutSeconds: 20).RequireSuccess("无法读取本地 dsh 版本");
        Match localMatch = VersionPattern.Match(localResult.Output);
        if (!localMatch.Success)
            throw new InvalidOperationException($"无法识别本地 dsh 版本：{ToSingleLine(localResult.Output)}");

        CommandResult versionsResult = RunCommand($"npm view {PackageName} versions --json", timeoutSeconds: 45)
            .RequireSuccess("无法从 npm 获取 DSH 版本信息");
        using JsonDocument document = JsonDocument.Parse(versionsResult.Output);
        string[] versions = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(item => item.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
            : new[] { document.RootElement.GetString() ?? string.Empty };

        string? latestRc = versions.LastOrDefault(version => Regex.IsMatch(version, @"-rc(?:\.|$)", RegexOptions.IgnoreCase));
        if (string.IsNullOrWhiteSpace(latestRc))
            throw new InvalidOperationException("npm 未返回稳定 RC 版本。");

        string? latestAlpha = versions.LastOrDefault(version => Regex.IsMatch(version, @"-alpha(?:\.|$)", RegexOptions.IgnoreCase));
        return new VersionInfo(localMatch.Value, latestRc, latestAlpha);
    }

    private async Task UpdateDshAsync()
    {
        if (string.IsNullOrWhiteSpace(latestStableVersion) || serviceState is ServiceState.Updating) return;

        string targetVersion = latestStableVersion;
        suppressExitNotifications = true;
        updateItem.Enabled = false;
        serviceState = ServiceState.Updating;
        UpdateMenu();

        await StopDshAsync(showNotification: false, forUpdate: true);
        if (HasLiveTrackedProcess()) return;
        Notify("DSH 稳定版本更新", "已关闭 DSH 服务，正在打开更新终端。");

        bool updateSucceeded = false;
        try
        {
            int exitCode = await RunVisibleUpdateAsync(targetVersion);
            updateSucceeded = exitCode == 0;
            Notify(
                updateSucceeded ? "DSH 更新完成" : "DSH 更新失败",
                updateSucceeded ? $"已更新至稳定版本 {targetVersion}。" : "更新命令执行失败，请查看终端日志。");
        }
        catch (Exception ex)
        {
            Notify("DSH 更新失败", ToSingleLine(ex.Message));
        }
        finally
        {
            serviceState = ServiceState.Stopped;
            suppressExitNotifications = false;
            await CheckVersionsAsync(showNotification: false);
            await StartDshAsync();
        }
    }

    private static async Task<int> RunVisibleUpdateAsync(string targetVersion)
    {
        if (!Regex.IsMatch(targetVersion, @"^[0-9A-Za-z.+-]+$"))
            throw new InvalidOperationException("目标版本号格式不安全，已取消更新。");

        string npmCommand = ResolveNpmCommand();
        string escapedNpmCommand = npmCommand.Replace("'", "''");
        string scriptPath = Path.Combine(Path.GetTempPath(), $"DSHLauncher-update-{Guid.NewGuid():N}.ps1");
        string script = $$"""
            $Host.UI.RawUI.WindowTitle = 'DSH 稳定版本更新'
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $OutputEncoding = [Console]::OutputEncoding
            & "$env:SystemRoot\System32\chcp.com" 65001 | Out-Null
            Write-Host '正在更新 {{PackageName}} 到稳定版本 {{targetVersion}}...'
            Write-Host '=================================================='
            & '{{escapedNpmCommand}}' install -g '{{PackageName}}@{{targetVersion}}' --force --progress=true --loglevel=info
            $updateExitCode = $LASTEXITCODE
            Write-Host '=================================================='
            Write-Host
            if ($updateExitCode -ne 0) {
                Write-Host 'DSH 更新失败，请检查上方日志。' -ForegroundColor Red
                Read-Host '按 Enter 键关闭此窗口'
            }
            exit $updateExitCode
            """;

        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法打开 DSH 更新终端。");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static string ResolveNpmCommand()
    {
        CommandResult result = RunCommand("where.exe npm.cmd", 10).RequireSuccess("无法定位 npm.cmd");
        string? command = result.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .FirstOrDefault(File.Exists);
        return command ?? throw new InvalidOperationException("where.exe 找到了 npm，但没有可用的 npm.cmd 文件。");
    }

    private async Task StartDshAsync()
    {
        await ReconcileAsync();
        if (serviceState is not ServiceState.Stopped || HasLiveTrackedProcess()) return;

        generation++;
        serviceUrl = null;
        sawBrowserOpening = false;
        readinessTriggered = false;
        recentOutput.Clear();
        serviceState = ServiceState.Starting;
        UpdateMenu();

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /c dsh web",
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, args) => HandleServiceOutput(process, args.Data);
            process.ErrorDataReceived += (_, args) => HandleServiceOutput(process, args.Data);
            process.Exited += (_, _) => HandleCommandExit(process);
            commandProcess = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            commandProcess?.Dispose();
            commandProcess = null;
            serviceState = ServiceState.Stopped;
            UpdateMenu();
            Notify("DSH 服务启动失败", ToSingleLine(ex.Message));
        }

        await Task.CompletedTask;
    }

    private void HandleServiceOutput(Process source, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        InvokeOnUi(() => ProcessServiceOutput(source, line));
    }

    private void ProcessServiceOutput(Process source, string line)
    {
        if (!ReferenceEquals(commandProcess, source) || suppressExitNotifications) return;
        line = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", "");

        lock (recentOutput)
        {
            recentOutput.Enqueue(line);
            while (recentOutput.Count > 12) recentOutput.Dequeue();
        }

        Match urlMatch = WebUrlPattern.Match(line);
        if (urlMatch.Success)
            serviceUrl = urlMatch.Groups["url"].Value;
        if (line.Contains("opening the default browser", StringComparison.OrdinalIgnoreCase))
            sawBrowserOpening = true;

        if (serviceUrl is not null && sawBrowserOpening && !readinessTriggered)
        {
            readinessTriggered = true;
            _ = MarkServiceReadyAsync(source);
        }
    }

    private async Task MarkServiceReadyAsync(Process source)
    {
        if (!ReferenceEquals(commandProcess, source) || serviceState is not (ServiceState.Starting or ServiceState.Running) || serviceUrl is null) return;
        int epoch = generation;

        if (Uri.TryCreate(serviceUrl, UriKind.Absolute, out Uri? uri))
        {
            Process? actual = await FindListenerProcessAsync(uri.Port);
            if (epoch != generation || shuttingDown || suppressExitNotifications) { actual?.Dispose(); return; }
            if (actual is not null)
            {
                listenerProcess?.Dispose();
                listenerProcess = actual;
                actual.EnableRaisingEvents = true;
                actual.Exited += (_, _) => HandleListenerExit(actual);
            }
        }

        if (serviceState is not (ServiceState.Starting or ServiceState.Running) || !IsAlive(listenerProcess)) return;
        serviceState = ServiceState.Running;
        UpdateMenu();
        Notify("DSH 服务已启动", "DSH 服务已就绪，可在浏览器中使用。");
    }

    private static async Task<Process?> FindListenerProcessAsync(int port)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            CommandResult result = await Task.Run(() => RunCommand("netstat -ano -p tcp", timeoutSeconds: 5));
            if (result.Success)
            {
                foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Match match = ListeningPattern.Match(line);
                    if (!match.Success || !int.TryParse(match.Groups["port"].Value, out int foundPort) || foundPort != port) continue;
                    if (int.TryParse(match.Groups["pid"].Value, out int processId))
                    {
                        try { return Process.GetProcessById(processId); } catch { }
                    }
                }
            }
            await Task.Delay(200);
        }
        return null;
    }

    private void HandleCommandExit(Process process)
    {
        InvokeOnUi(async () =>
        {
            if (!ReferenceEquals(commandProcess, process)) return;
            // Output callbacks and listener discovery can arrive after the wrapper exits.
            await Task.Delay(1500);
            if (ReferenceEquals(commandProcess, process)) await ReconcileAsync();
        });
    }

    private void HandleListenerExit(Process process)
    {
        InvokeOnUi(() =>
        {
            if (!ReferenceEquals(listenerProcess, process)) return;
            listenerProcess = null;
            process.Dispose();

            if (suppressExitNotifications || serviceState is ServiceState.Stopping or ServiceState.Updating)
            {
                UpdateMenu();
                return;
            }
            _ = ReconcileAsync();
        });
    }

    private async Task ReconcileAsync()
    {
        if (reconciling)
        {
            while (reconciling && !shuttingDown) await Task.Delay(50);
            return;
        }
        if (shuttingDown || suppressExitNotifications || serviceState is ServiceState.Updating or ServiceState.Stopping) return;
        reconciling = true;
        int epoch = generation;
        try
        {
            Process? found = IsAlive(listenerProcess) ? listenerProcess : null;
            if (found is null)
                found = await Task.Run(DiscoverDshListener);
            if (epoch != generation || shuttingDown || suppressExitNotifications)
            {
                if (!ReferenceEquals(found, listenerProcess)) found?.Dispose();
                return;
            }
            if (found is not null && IsAlive(found))
            {
                if (!ReferenceEquals(found, listenerProcess))
                {
                    listenerProcess?.Dispose();
                    listenerProcess = found;
                    found.Exited += (_, _) => HandleListenerExit(found);
                    found.EnableRaisingEvents = true;
                }
                // Actual listener is authoritative for menu state, independently of log wording.
                serviceState = ServiceState.Running;
            }
            else if (!IsAlive(commandProcess))
            {
                bool wasActive = serviceState is ServiceState.Starting or ServiceState.Running;
                commandProcess?.Dispose();
                commandProcess = null;
                listenerProcess?.Dispose();
                listenerProcess = null;
                serviceState = ServiceState.Stopped;
                if (wasActive) Notify("DSH 服务已停止", "未检测到运行中的 DSH 服务进程。");
            }
            UpdateMenu();
        }
        catch (Exception) { /* A failed probe is not evidence that a service stopped. */ }
        finally { reconciling = false; }
    }

    private static Process? DiscoverDshListener()
    {
        // Query only Node processes whose command line identifies the installed DSH CLI
        // and web profile. Never adopt an arbitrary process merely because it owns a port.
        const string query = "powershell.exe -NoProfile -NonInteractive -Command \"@(Get-CimInstance Win32_Process -Filter 'Name = ''node.exe''' | Where-Object { $_.CommandLine -match '[/\\\\]@deepseek-ai[/\\\\]dsh[/\\\\]lib[/\\\\]bin\\.js' -and $_.CommandLine -match '(?:^|\\s)(?:web|--profile[= ]+web)(?:\\s|$)' } | Select-Object -ExpandProperty ProcessId) | ConvertTo-Json -Compress\"";
        CommandResult candidates = RunCommand(query, 10).RequireSuccess("无法检测 DSH 进程");
        if (string.IsNullOrWhiteSpace(candidates.Output)) return null;
        using JsonDocument json = JsonDocument.Parse(candidates.Output);
        var ids = json.RootElement.ValueKind == JsonValueKind.Array
            ? json.RootElement.EnumerateArray().Select(x => x.GetInt32()).ToHashSet()
            : new HashSet<int> { json.RootElement.GetInt32() };
        CommandResult sockets = RunCommand("netstat -ano -p tcp", 5).RequireSuccess("无法检测监听端口");
        foreach (string line in sockets.Output.Split('\n'))
        {
            Match match = ListeningPattern.Match(line.TrimEnd('\r'));
            if (match.Success && int.TryParse(match.Groups["pid"].Value, out int id) && ids.Contains(id))
            {
                try { return Process.GetProcessById(id); } catch (ArgumentException) { }
            }
        }
        return null;
    }

    private async Task StopDshAsync(bool showNotification, bool forUpdate)
    {
        generation++;
        if (!HasLiveTrackedProcess())
        {
            commandProcess?.Dispose();
            listenerProcess?.Dispose();
            commandProcess = null;
            listenerProcess = null;
            serviceState = forUpdate ? ServiceState.Updating : ServiceState.Stopped;
            UpdateMenu();
            return;
        }

        suppressExitNotifications = true;
        serviceState = forUpdate ? ServiceState.Updating : ServiceState.Stopping;
        UpdateMenu();

        var targets = new[] { listenerProcess, commandProcess }
            .Where(IsAlive)
            .Cast<Process>()
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (Process process in targets)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
        await Task.WhenAll(targets.Select(WaitForExitSafelyAsync));

        if (targets.Any(IsAlive))
        {
            serviceState = ServiceState.Running;
            suppressExitNotifications = false;
            UpdateMenu();
            Notify("DSH 服务关闭失败", "服务进程仍在运行，请重试关闭。");
            return;
        }

        foreach (Process process in targets)
        {
            try { process.Dispose(); } catch { }
        }
        commandProcess = null;
        listenerProcess = null;
        serviceUrl = null;
        serviceState = forUpdate ? ServiceState.Updating : ServiceState.Stopped;
        suppressExitNotifications = forUpdate;
        UpdateMenu();

        if (showNotification)
            Notify("DSH 服务已关闭", "DSH 服务及其关联进程已停止运行。");
    }

    private static async Task WaitForExitSafelyAsync(Process process)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch { }
    }

    private bool HasLiveTrackedProcess() => IsAlive(listenerProcess) || IsAlive(commandProcess);

    private static bool IsAlive(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; } catch { return false; }
    }

    private void OpenConfigDirectory()
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{configDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Notify("无法打开 DSH 配置目录", ToSingleLine(ex.Message));
        }
    }

    private string GetRecentOutputSummary()
    {
        lock (recentOutput)
        {
            return ToSingleLine(string.Join(" ", recentOutput.TakeLast(3)));
        }
    }

    private void UpdateMenu()
    {
        (string status, Color color) = serviceState switch
        {
            ServiceState.Starting => ("DSH 服务：正在启动", Color.FromArgb(214, 137, 16)),
            ServiceState.Running => ("DSH 服务：运行中", Color.FromArgb(25, 135, 84)),
            ServiceState.Stopping => ("DSH 服务：正在关闭", Color.FromArgb(214, 137, 16)),
            ServiceState.Updating => ("DSH：正在更新", Color.FromArgb(9, 105, 218)),
            _ => ("DSH 服务：未运行", Color.FromArgb(135, 135, 135))
        };
        statusItem.Text = status;
        statusItem.DotColor = color;
        statusItem.Invalidate();
        tray.Text = status;

        startItem.Enabled = serviceState == ServiceState.Stopped && !HasLiveTrackedProcess();
        stopItem.Enabled = serviceState is ServiceState.Starting or ServiceState.Running;
        if (updateItem.Visible)
            updateItem.Enabled = latestStableVersion is not null && serviceState is not ServiceState.Updating and not ServiceState.Stopping;
    }

    private void InvokeOnUi(Action action)
    {
        try
        {
            if (!menu.IsDisposed && menu.IsHandleCreated)
                menu.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (shuttingDown || menu.IsDisposed)
        {
        }
    }

    private void Notify(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            tray.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
        }
    }

    private static CommandResult RunCommand(string command, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c {command}",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(false, string.Empty, $"命令执行超时（{timeoutSeconds} 秒）。");
        }
        Task.WaitAll(outputTask, errorTask);
        return new CommandResult(process.ExitCode == 0, outputTask.Result, errorTask.Result);
    }

    private static string ToSingleLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    protected override void ExitThreadCore()
    {
        shuttingDown = true;
        monitor.Stop();
        monitor.Dispose();
        suppressExitNotifications = true;
        foreach (Process? process in new[] { listenerProcess, commandProcess })
        {
            if (!IsAlive(process)) continue;
            try { process!.Kill(entireProcessTree: true); } catch { }
        }
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        trayIcon.Dispose();
        base.ExitThreadCore();
    }

    private enum ServiceState { Stopped, Starting, Running, Stopping, Updating }

    private sealed record VersionInfo(string Local, string LatestRc, string? LatestAlpha);

    private sealed record CommandResult(bool Success, string Output, string Error)
    {
        public CommandResult RequireSuccess(string context)
        {
            if (!Success)
                throw new InvalidOperationException($"{context}：{ToSingleLine(string.IsNullOrWhiteSpace(Error) ? Output : Error)}");
            return this;
        }
    }

    private sealed class StatusMenuItem : ToolStripMenuItem
    {
        public StatusMenuItem(string text) : base(text)
        {
        }

        public Color DotColor { get; set; } = Color.FromArgb(135, 135, 135);

        public override Size GetPreferredSize(Size constrainingSize)
        {
            Size size = base.GetPreferredSize(constrainingSize);
            return new Size(size.Width + 18, size.Height);
        }
    }

    private sealed class LauncherRenderer : ToolStripProfessionalRenderer
    {
        public LauncherRenderer() : base(new LauncherColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;

            RectangleF bounds = new(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2));
            using GraphicsPath path = CreateRoundedPath(bounds, 8);
            using var brush = new SolidBrush(Color.FromArgb(232, 238, 245));
            SmoothingMode oldMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = oldMode;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Color.FromArgb(224, 224, 224));
            e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not StatusMenuItem statusItem)
            {
                base.OnRenderItemText(e);
                return;
            }

            const int dotDiameter = 10;
            Rectangle dotBounds = new(
                e.TextRectangle.X + 1,
                e.TextRectangle.Y + (e.TextRectangle.Height - dotDiameter) / 2,
                dotDiameter,
                dotDiameter);
            Rectangle textBounds = new(
                e.TextRectangle.X + 18,
                e.TextRectangle.Y,
                Math.Max(1, e.TextRectangle.Width),
                e.TextRectangle.Height);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            SmoothingMode oldMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(statusItem.DotColor))
                e.Graphics.FillEllipse(brush, dotBounds);
            e.Graphics.SmoothingMode = oldMode;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, Color.FromArgb(45, 45, 45), flags);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            RectangleF bounds = new(0.5F, 0.5F, Math.Max(1, e.ToolStrip.Width - 1.5F), Math.Max(1, e.ToolStrip.Height - 1.5F));
            using GraphicsPath path = CreateRoundedPath(bounds, 10);
            using var pen = new Pen(Color.FromArgb(210, 210, 210));
            SmoothingMode oldMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
            e.Graphics.SmoothingMode = oldMode;
        }
    }

    private sealed class LauncherColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(232, 238, 245);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color ToolStripDropDownBackground => Color.FromArgb(248, 248, 248);
        public override Color ImageMarginGradientBegin => Color.FromArgb(248, 248, 248);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(248, 248, 248);
        public override Color ImageMarginGradientEnd => Color.FromArgb(248, 248, 248);
        public override Color SeparatorDark => Color.FromArgb(224, 224, 224);
        public override Color SeparatorLight => Color.White;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HwndTopmost = new(-1);
        public static readonly IntPtr HwndNotTopmost = new(-2);
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;
        public const int DwmwaWindowCornerPreference = 33;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);
    }
}
