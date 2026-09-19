using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using DSHLauncher;

internal static class Regression
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Get(TrayContext c, string name) => typeof(TrayContext).GetField(name, Flags).GetValue(c);
    static void Set(TrayContext c, string name, object value) => typeof(TrayContext).GetField(name, Flags).SetValue(c, value);
    static object Call(TrayContext c, string name, params object[] args) => typeof(TrayContext).GetMethod(name, Flags).Invoke(c, args);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new TrayContext(false);
        var menu = (ContextMenuStrip)Get(context, "menu");
        menu.BeginInvoke((Action)(async () =>
        {
            Process child = null;
            try
            {
                // A real long-lived process stands in for the bound listener. Only this
                // test-owned PID is ever stopped; installed DSH services are read-only.
                child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 120 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true });
                using var wrapper = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c exit 0") { UseShellExecute = false, CreateNoWindow = true });
                await wrapper.WaitForExitAsync();
                Set(context, "commandProcess", wrapper);
                Set(context, "listenerProcess", child);
                await (Task)Call(context, "ReconcileAsync");
                Check(Get(context, "serviceState").ToString() == "Running", "exited wrapper + live listener => Running");
                Check(!((ToolStripMenuItem)Get(context, "startItem")).Enabled && ((ToolStripMenuItem)Get(context, "stopItem")).Enabled, "menu enables Stop, disables Start");
                Call(context, "HandleCommandExit", wrapper);
                await Task.Delay(1800);
                Check(Get(context, "serviceState").ToString() == "Running", "late wrapper exit cannot reset running state");
                Set(context, "suppressExitNotifications", true);
                using var childProbe = Process.GetProcessById(child.Id);
                await (Task)Call(context, "StopDshAsync", false, true);
                Check(childProbe.HasExited, "stop actually terminates owned process");
                Check(Get(context, "serviceState").ToString() == "Updating", "update stop preserves Updating state");
                using var discovered = (Process)typeof(TrayContext).GetMethod("DiscoverDshListener", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Console.WriteLine(discovered == null ? "Probe succeeded: no installed DSH listener" : "Probe succeeded: installed DSH listener PID " + discovered.Id);
                Console.WriteLine("Regression tests complete.");
            }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally
            {
                try { if (child != null && !child.HasExited) child.Kill(true); } catch { }
                Application.ExitThread();
            }
        }));
        Application.Run();
    }
}
