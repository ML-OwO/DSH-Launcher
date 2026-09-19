using DSHLauncher.Core;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine($"PASS {message}");
}

var versions = DshVersions.Parse("dsh 1.0.0-rc.1", "[\"1.0.0-rc.1\",\"1.0.0-rc.2\",\"1.0.0-alpha.3\"]");
Check(DshVersions.IsNewer("1.0.0-rc.10", "1.0.0-rc.2"), "RC numeric precedence");
Check(!DshVersions.IsNewer("1.0.0-rc.9", "1.1.0-alpha.1"), "Never downgrade newer preview version");
Check(!DshVersions.IsNewer("1.0.0-rc.2", "1.0.0-rc.2"), "Equal version is not an update");
Check(versions.Current == "1.0.0-rc.1" && versions.LatestStable == "1.0.0-rc.2" && versions.LatestPreview == "1.0.0-alpha.3", "RC and Alpha channels remain separate");
Check(DshVersions.Parse("1.0.0-rc.2", "\"1.0.0-rc.2\"").LatestPreview is null, "Single npm version without Alpha");
try { DshVersions.Parse("1.0.0", "[\"1.0.0-alpha.1\"]"); throw new Exception("Missing RC accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS missing RC is explicit error"); }
try { DshCommands.Update("1.0.0; touch /tmp/example"); throw new Exception("Unsafe version accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS unsafe update target rejected"); }
var tracker = new StartupOutputTracker();
tracker.Observe("\u001b[32mdsh web: http://127.0.0.1:8080/?token=test-only\u001b[0m");
Check(!tracker.IsReady, "URL alone does not signal readiness");
tracker.Observe("dsh web: opening the default browser; pass --no-open to disable");
Check(tracker.IsReady && tracker.ServiceUrl?.Port == 8080, "Colored startup output detected");
var second = new StartupOutputTracker();
second.Observe("dsh web: opening the default browser; pass --no-open to disable");
second.Observe("dsh web: http://127.0.0.1:8081/?token=test-only");
Check(second.IsReady && second.ServiceUrl?.Port == 8081, "Startup messages can arrive in either order");
Check(!new StartupOutputTracker().IsReady, "New start has no stale readiness state");
