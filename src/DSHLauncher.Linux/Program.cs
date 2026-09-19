using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using DSHLauncher.Core;

namespace DSHLauncher.Linux;

internal static class Program
{
    private static readonly ConcurrentQueue<Action> Ui = new();
    private static readonly List<Native.Activate> Callbacks = new();
    private static readonly Native.Tick Timer = Tick;
    private static Service service = null!;
    private static IntPtr status, start, stop, update, check, exit, indicator;
    private static bool busy, quitting;
    private static bool wasAlive;
    private static DshVersionInfo? versions;
    private static string? lastStatus;

    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("请在 Linux 桌面运行。"); return 1; }
        Runtime.Initialize();
        if (args.Length == 3 && args[0] == "--update-worker") return await UpdateWorker(args[1], args[2]);
        service = new();
        if (args.Contains("--smoke-test")) {
            if (service.Alive) { Console.Error.WriteLine("已有托管服务，跳过测试。"); return 2; }
            try { await service.Start(); Console.WriteLine("PASS service startup and HTTP"); await service.Stop(); Console.WriteLine("PASS service stop"); return 0; }
            finally { await service.Stop(); }
        }
        FileStream single;
        try { single = new FileStream(Path.Combine(Runtime.Data, "launcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { Console.WriteLine("DSH Launcher 已在运行。"); return 0; }
        using var singleLifetime = single;
        if (Native.gtk_init_check(IntPtr.Zero, IntPtr.Zero) == 0) { Console.Error.WriteLine("无法连接桌面，请从麒麟桌面启动。"); return 1; }
        string icon = Path.Combine(Runtime.Cache, "launcher.ico");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("launcher.ico")!)
        using (var output = File.Create(icon)) resource.CopyTo(output);
        var menu = Native.gtk_menu_new();
        status = Native.gtk_image_menu_item_new_with_label("DSH：正在初始化");
        Native.gtk_image_menu_item_set_always_show_image(status, 1);
        Native.gtk_menu_shell_append(menu, status);
        Separator(menu);
        update = Item(menu, "更新稳定版本", () => Execute(Update));
        check = Item(menu, "检查版本更新", () => Execute(Check));
        Item(menu, "打开 DSH 配置目录", () => { Directory.CreateDirectory(DshCommands.ConfigDirectory); Runtime.Open(DshCommands.ConfigDirectory); });
        Separator(menu);
        start = Item(menu, "启动 DSH 服务", () => Execute(Start));
        stop = Item(menu, "关闭 DSH 服务", () => Execute(Stop));
        Separator(menu);
        exit = Item(menu, "退出 DSH Launcher", () => Execute(async () => { await service.Stop(); Ui.Enqueue(() => { quitting = true; Native.app_indicator_set_status(indicator, 0); Native.gtk_main_quit(); }); }));
        indicator = Native.app_indicator_new("dsh-launcher", icon, 0);
        Native.app_indicator_set_title(indicator, "DSH Launcher");
        Native.app_indicator_set_menu(indicator, menu);
        Native.app_indicator_set_status(indicator, 1);
        Native.gtk_widget_show_all(menu);
        Native.g_timeout_add(250, Timer, IntPtr.Zero);
        Execute(async () => { await Check(); await Start(); });
        Native.gtk_main();
        return 0;
    }

    private static IntPtr Item(IntPtr menu, string label, Action? action)
    {
        var item = Native.gtk_menu_item_new_with_label(label);
        Native.gtk_menu_shell_append(menu, item);
        if (action is null) Native.gtk_widget_set_sensitive(item, 0);
        else {
            Native.Activate handler = (_, _) => { try { action(); } catch (Exception e) { Runtime.Notify("DSH Launcher", e.Message); } };
            Callbacks.Add(handler);
            Native.g_signal_connect_data(item, "activate", handler, IntPtr.Zero, IntPtr.Zero, 0);
        }
        return item;
    }
    private static void Separator(IntPtr menu) => Native.gtk_menu_shell_append(menu, Native.gtk_separator_menu_item_new());
    private static void Execute(Func<Task> operation)
    {
        if (busy) return;
        busy = true; Refresh();
        _ = Task.Run(async () => {
            try { await operation(); }
            catch (Exception e) { Runtime.Notify("DSH 操作失败", e.Message); }
            finally { Ui.Enqueue(() => { busy = false; wasAlive = service.Alive; Refresh(); }); }
        });
    }
    private static int Tick(IntPtr data)
    {
        try {
            while (Ui.TryDequeue(out var action)) action();
            if (!busy && wasAlive && !service.Alive) Runtime.Notify("DSH 服务已停止", "DSH 进程意外退出。");
            wasAlive = service.Alive;
            Refresh();
        } catch (Exception e) { Console.Error.WriteLine(e.Message); }
        return quitting ? 0 : 1;
    }
    private static void Refresh()
    {
        bool alive = service.Alive;
        string state = busy ? "busy" : alive ? "running" : "stopped";
        if (state != lastStatus) {
            lastStatus = state;
            string color = busy ? "#e5a50a" : alive ? "#26a269" : "#888888";
            string path = Path.Combine(Runtime.Cache, $"status-{state}.svg");
            File.WriteAllText(path, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"{color}\"/></svg>");
            Native.gtk_image_menu_item_set_image(status, Native.gtk_image_new_from_file(path));
            Native.gtk_menu_item_set_label(status, busy ? "DSH：正在处理…" : alive ? "DSH 服务：运行中" : "DSH 服务：未运行");
            Native.gtk_widget_show_all(status);
        }
        Native.gtk_widget_set_sensitive(start, !busy && !alive ? 1 : 0);
        Native.gtk_widget_set_sensitive(stop, !busy && alive ? 1 : 0);
        Native.gtk_widget_set_sensitive(check, busy ? 0 : 1);
        Native.gtk_widget_set_sensitive(exit, busy ? 0 : 1);
        Native.gtk_widget_set_sensitive(update, busy ? 0 : 1);
        Native.gtk_widget_set_visible(update, versions is not null && DshVersions.IsNewer(versions.LatestStable, versions.Current) ? 1 : 0);
        if (versions is not null) Native.gtk_menu_item_set_label(update, $"更新至 {versions.LatestStable} 稳定版本");
    }
    private static async Task Check()
    {
        try {
            var value = DshVersions.Parse(await Runtime.Run(DshCommands.LocalVersion, 20), await Runtime.Run(DshCommands.PublishedVersions));
            Ui.Enqueue(() => versions = value);
            Runtime.Notify("DSH 版本信息", DshVersions.IsNewer(value.LatestStable, value.Current) ? $"当前版本：{value.Current}\n最新稳定版本：{value.LatestStable}" : $"当前版本：{value.Current}\n最新预览版本：{value.LatestPreview ?? "暂无"}");
        } catch (Exception e) { Runtime.Notify("DSH 版本检查失败", e.Message); }
    }
    private static async Task Start() { await service.Start(); Runtime.Notify("DSH 服务已启动", "服务已就绪，可在浏览器中使用。"); }
    private static async Task Stop() { await service.Stop(); Runtime.Notify("DSH 服务已关闭", "服务进程已停止。"); }
    private static async Task Update()
    {
        string? target = versions?.LatestStable;
        if (target is null) return;
        DshCommands.Update(target);
        await service.Stop();
        Runtime.Notify("DSH 正在更新", "已关闭服务，更新终端将显示日志。");
        string result = Path.Combine(Runtime.Data, $"update-{Guid.NewGuid():N}.result");
        bool finished = false;
        try {
            bool mate = File.Exists("/usr/bin/mate-terminal");
            var command = mate
                ? new CommandSpec("/usr/bin/mate-terminal", new[] { "--disable-factory", "--", Environment.ProcessPath!, "--update-worker", target, result })
                : new CommandSpec("x-terminal-emulator", new[] { "-e", Environment.ProcessPath!, "--update-worker", target, result });
            using var terminal = Process.Start(Runtime.Info(command, false)) ?? throw new IOException("无法打开更新终端。");
            var deadline = DateTime.UtcNow.AddMinutes(30);
            while (!File.Exists(result)) {
                if (mate && terminal.HasExited) { finished = true; throw new IOException("更新窗口已关闭或无法启动。"); }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("未收到更新结果，请检查更新终端。");
                await Task.Delay(500);
            }
            finished = true;
            if (File.ReadAllText(result).Trim() != "0") throw new IOException("更新失败，请查看终端日志。");
            Runtime.Notify("DSH 更新成功", $"已更新至 {target}。");
        } finally { if (finished) { File.Delete(result); await Check(); await Start(); } }
    }
    private static async Task<int> UpdateWorker(string version, string result)
    {
        int code = 1;
        try {
            Console.WriteLine($"正在更新 DSH 至 {version}…");
            using var p = Process.Start(Runtime.Info(DshCommands.Update(version), false))!;
            using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => { context.Cancel = true; try { p.Kill(true); } catch { } });
            using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; try { p.Kill(true); } catch { } });
            await p.WaitForExitAsync(); code = p.ExitCode;
        } catch (Exception e) { Console.Error.WriteLine(e.Message); }
        File.WriteAllText(result + ".tmp", code.ToString()); File.Move(result + ".tmp", result, true);
        if (code != 0) { Console.WriteLine("更新失败，按 Enter 关闭窗口。"); Console.ReadLine(); }
        return code;
    }
}
