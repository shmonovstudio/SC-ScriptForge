using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using SC.ScriptForge.Windows;

internal static partial class Checks
{
    private static readonly List<string> Results = [];
    private static string _root = "", _worker = "";
    private static int _failed;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture-worker")
        {
            if (args[1] == "hang") Thread.Sleep(30_000);
            if (args[1] == "invalid") { Console.Write("not JSON"); return 0; }
            if (args[1] == "large") { Console.Write(new string('x', 2_100_000)); return 0; }
            return 1;
        }
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var referenceDesign = args.Contains("--reference-design") || args.Contains("--reference-native");
        var motionOnly = args.Contains("--motion-only");
        var libraryOnly = args.Contains("--library-only");
        var resultLayoutOnly = args.Contains("--result-layout-only");
        var aiRepair = args.Contains("--ai-repair-only") || args.Contains("--ai-repair-live");
        var aiRepairLive = args.Contains("--ai-repair-live");
        var runCloseOnly = args.Contains("--run-close-only") || args.Contains("--run-close-native");
        var runDesignOnly = args.Contains("--run-design") || args.Contains("--run-design-native");
        var nativeOnly = args.Contains("--native-ui") || args.Contains("--reference-native") || args.Contains("--run-close-native") || args.Contains("--run-design-native");
        var studioDesign = args.Contains("--studio-design");
        var aiOnly = args.Contains("--ai-only") || args.Contains("--ai-live") || args.Contains("--ai-design");
        var aiInstall = args.Contains("--ai-install");
        var aiDownload = args.Contains("--ai-download-only") || args.Contains("--ai-download-live");
        var aiPublished = args.Contains("--ai-published");
        var aiLive = args.Contains("--ai-live");
        var uiOnly = args.Contains("--ui-only") || motionOnly || referenceDesign || studioDesign || nativeOnly || aiOnly || aiInstall || aiDownload || aiPublished || libraryOnly || resultLayoutOnly || aiRepair || runCloseOnly || runDesignOnly;
        _root = Path.Combine(workspace, "artifacts", studioDesign ? "studio-design-validation" : args.Contains("--icons") ? nativeOnly ? "icons-validation/native" : aiOnly ? "icons-ai-validation" : "icons-validation" : args.Contains("--ai-design") ? "ai-design-validation" : aiPublished ? "ai-publish-validation" : aiInstall ? "ai-install-validation" : aiOnly ? "ai-validation" : args.Contains("--regression") ? "ai-regression" : nativeOnly ? "library-validation/native" : uiOnly ? "library-validation" : "avalonia-validation"); Directory.CreateDirectory(_root);
        if (referenceDesign) { _root = Path.Combine(workspace, "artifacts", "reference-design", nativeOnly ? "native" : "headless"); Directory.CreateDirectory(_root); }
        if (motionOnly) { _root = Path.Combine(workspace,"artifacts","motion-validation"); Directory.CreateDirectory(_root); }
        if (resultLayoutOnly) { _root = Path.Combine(workspace,"artifacts","result-layout-validation"); Directory.CreateDirectory(_root); }
        if (aiRepair) { _root = Path.Combine(workspace,"artifacts",aiRepairLive ? "ai-repair-live" : "ai-repair-validation"); Directory.CreateDirectory(_root); }
        if (runCloseOnly) { _root = Path.Combine(workspace,"artifacts","run-close-validation",nativeOnly ? "native" : "headless"); Directory.CreateDirectory(_root); }
        if (runDesignOnly) { _root = Path.Combine(workspace,"artifacts","run-window-design",nativeOnly ? "native" : "headless"); Directory.CreateDirectory(_root); }
        if (aiDownload) { _root = Path.Combine(workspace,"artifacts","ai-download-validation"); Directory.CreateDirectory(_root); }
        _worker = Path.Combine(workspace, "bin", "Release", "net10.0-windows", "SC.ScriptForge.exe");
        var builder = AppBuilder.Configure<App>();
        if (nativeOnly) builder.UsePlatformDetect(); else builder.UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia();
        builder.SetupWithoutStarting();
        using var quit = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (!uiOnly)
                {
                    await Group("data", DataAsync);
                    await Group("bundled library", ScriptLibraryAsync);
                    await Group("result layout", ResultLayoutAsync);
                    await Group("run form layout", () => RunDesignAsync(false));
                    await Group("permissions and privacy", PermissionsAsync);
                    await Group("worker isolation", WorkerAsync);
                    await Group("PowerShell", ScriptsAsync);
                    await Group("AI runtime repairs", AiRepairAsync);
                }
                if (aiDownload) { await Group("AI downloads", AiDownloadAsync); if (args.Contains("--ai-download-live")) await Group("AI activation on CPU and GPU", AiDownloadLiveAsync); }
                else if (runDesignOnly) await Group("run window design", () => RunDesignAsync(nativeOnly));
                else if (runCloseOnly) await Group("closing script windows", () => RunCloseAsync(nativeOnly));
                else if (aiRepair) { await Group("AI runtime repairs", AiRepairAsync); if (aiRepairLive) await Group("live AI repair", AiRepairLiveAsync); }
                else if (resultLayoutOnly) await Group("result layout", ResultLayoutAsync);
                else if (libraryOnly) await Group("bundled library", ScriptLibraryAsync);
                else if (motionOnly) await Group("motion", MotionAsync);
                else if (referenceDesign) await Group("Reference design", () => ReferenceDesignAsync(nativeOnly));
                else if (aiPublished) await Group("AI published runtime",AiPublishedAsync);
                else if (aiInstall) await Group("AI installation",AiInstallAsync);
                else if (aiOnly) { await Group("AI contracts and review", AiAsync); if (args.Contains("--ai-design")) await Group("AI interaction",AiInteractionAsync); if (aiLive) await Group("real Qwen3.5 inference", AiLiveAsync); }
                else if (nativeOnly) await Group("native reference UI", () => ReferenceDesignAsync(true));
                else { await Group("Avalonia UI", () => ReferenceDesignAsync(false)); await Group("closing script windows", () => RunCloseAsync(false)); await Group("motion", MotionAsync); }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(_root, "results.txt"), Results);
                Console.WriteLine($"Checks: {Results.Count(r => r.StartsWith("PASS ") || r.StartsWith("FAIL "))}; failures: {_failed}; observations: {Results.Count(r => r.StartsWith("OBSERVATION "))}"); quit.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(quit.Token); return _failed == 0 ? 0 : 1;
    }
    private static async Task Group(string name, Func<Task> action)
    { try { await action(); } catch (Exception e) { Check(false, name + ": " + e); } }
    private static void Check(bool condition, string message)
    { var line = (condition ? "PASS " : "FAIL ") + message; Results.Add(line); Console.WriteLine(line); if (!condition) _failed++; }
    private static string Fixture(string name) { var path = Path.Combine(_root, name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static async Task DataAsync()
    {
        var root = Fixture("data"); var repository = new ScriptRepository(root);
        var entry = repository.Create("Проверка кириллицы", "Windows", "Описание", "Заметки", ["тег"], true, "Ready");
        var id = entry.Id; repository.WriteCode(entry, "param([string]$Name)\nWrite-Output $Name");
        var loaded = repository.LoadAll().Single();
        Check(loaded.Id == id && loaded.Favorite && loaded.Status == "Ready" && loaded.Tags.Single() == "тег", "sidecar metadata and stable ID roundtrip");
        var version = repository.GetVersions(entry).First(); repository.WriteCode(entry, "'second'"); repository.Restore(entry, version);
        Check(ScriptRepository.ReadCode(entry.FilePath).Contains("param("), "restore keeps prior code snapshots");
        var trash = repository.MoveToTrash(entry); Check(repository.LoadAll().Count == 0 && repository.GetTrash().Count == 1, "trash preserves manifest and metadata");
        repository.RestoreFromTrash(trash); Check(repository.LoadAll().Single().Id == id, "trash restore preserves ID");
        var duplicate = repository.Duplicate(repository.LoadAll().Single()); Check(duplicate.Id != id && repository.LoadAll().Count == 2, "duplicate creates new ID");
        repository.CreateCategory("Тесты"); Check(repository.Categories.Contains("Тесты"), "custom categories survive reload");
        var store = new PreferencesStore(root); File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Width\":1234,\"Sort\":1,\"ReduceMotion\":true}");
        var preferences = store.Load(); Check(preferences.Width == 1234 && preferences.ReduceMotion && preferences.Theme == "Light", "0.9 preferences upgrade preserves values and defaults to the light theme"); store.Save(preferences);
        using var library = new LibraryViewModel(repository, new LibrarySearch(), preferences); await library.RefreshAsync(); library.Query = "param"; await library.PendingSearch;
        Check(library.FilteredEntries.Count > 0, "background search includes script text");
        var profiles = new RunProfileStore(root); profiles.Save(id, new() { EngineId = "windows", Parameters = new() { ["Name"] = new() { TypeName = "System.String", Value = "тест" } } });
        Check(profiles.Load(id).Parameters["Name"].Value == "тест", "run profile JSON compatibility");
        var history = new RunHistoryStore(root); await history.RecordAsync(new(id, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), RunOutcome.Success, 0));
        Check(new RunHistoryStore(root).LatestFor(id)?.Outcome == RunOutcome.Success, "run history compatibility");
        var backup = Path.Combine(_root, Guid.NewGuid() + ".zip"); repository.CreateBackup(backup); Check(File.Exists(backup), "ZIP backup outside Data");
        var traversal = false; try { repository.RestoreFromTrash(new() { Folder = Path.Combine(root, ".trash", "missing"), OriginalRelativePath = "..\\escape.ps1" }); } catch (Exception) { traversal = true; }
        Check(traversal, "trash path traversal rejected");
        Check(DiffService.Compare("a\nb", "a\nc").Any(l => l.Kind == DiffKind.Added) && DiffService.Compare("a\nb", "a\nc").Any(l => l.Kind == DiffKind.Removed), "version comparison detects changes");
    }
    private static async Task PermissionsAsync()
    {
        var privilege = new WindowsPrivilegeService(); Check((await privilege.CheckAsync(Operations.Inventory, default)).Allowed, "inventory uses current-user context");
        Check(!(await privilege.CheckAsync(new("unknown", PermissionRequirement.CurrentUser, ""), default)).Allowed, "unknown operation denied");
        Check(!(await privilege.CheckAsync(Operations.External, default)).Allowed, "local token does not authorize remote resources");
        Check(!(await new PrivilegedOperationBroker().ExecuteAsync(new((PrivilegedOperation)42), default)).Allowed, "unregistered helper operation denied");
        Results.Add("OBSERVATION process token elevated: " + privilege.IsProcessElevated());
        var fields = new[] { new InventoryField("memory", "Память", "fixture") { Status = DataStatus.Success, Number = 1024, Unit = "bytes" }, new InventoryField("user", "Пользователь", "fixture") { Status = DataStatus.Success, Text = "secret-identity", Sensitive = true } };
        var snapshot = new InventorySnapshot(DateTimeOffset.UtcNow, [new("memory", "Память", [new("Memory", fields)])]);
        var selected = InventoryExport.Select(snapshot, new HashSet<string> { "memory" }); var json = InventoryExport.Json(selected);
        Check(!json.Contains("secret-identity") && JsonDocument.Parse(json).RootElement.GetProperty("Categories")[0].GetProperty("Records")[0].GetProperty("Fields")[0].GetProperty("Number").GetDouble() == 1024, "default export excludes private fields and retains numeric values");
        Check(InventoryExport.Json(InventoryExport.Select(snapshot, new HashSet<string> { "memory" }, true)).Contains("secret-identity"), "private export requires explicit opt-in");
        var isolated = new ComputerInventory([new FakeCategory("cpu", true), new FakeCategory("memory", false)]);
        var result = await isolated.RefreshAsync(default); Check(result.Categories[0].Error is not null && result.Categories[1].Error is null, "one category failure does not discard other data");
    }
    private sealed class FakeCategory(string id, bool fail) : IInventoryCategoryService
    {
        public string CategoryId => id;
        public Task<InventoryCategory> CollectAsync(CancellationToken cancellationToken) => fail ? throw new UnauthorizedAccessException() : Task.FromResult(new InventoryCategory(id, id, []));
    }
    private static async Task WorkerAsync()
    {
        IReadOnlyList<string> FixtureArgs(string mode) => Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Assembly.GetExecutingAssembly().Location, "--fixture-worker", mode] : ["--fixture-worker", mode];
        var timer = Stopwatch.StartNew(); var timeout = new IsolatedInventoryService("cpu", Environment.ProcessPath!, FixtureArgs("hang"), TimeSpan.FromMilliseconds(300));
        var result = await timeout.CollectAsync(default); Check(result.Error?.Kind == ErrorKind.Timeout && timer.Elapsed < TimeSpan.FromSeconds(5), "hung native worker killed by deadline");
        using var cancel = new CancellationTokenSource(200); var cancelled = false;
        try { await new IsolatedInventoryService("cpu", Environment.ProcessPath!, FixtureArgs("hang")).CollectAsync(cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "native worker cancellation propagates");
        Check((await new IsolatedInventoryService("cpu", Environment.ProcessPath!, FixtureArgs("invalid")).CollectAsync(default)).Error?.Kind == ErrorKind.ApiError, "invalid worker JSON is isolated");
        Check((await new IsolatedInventoryService("cpu", Environment.ProcessPath!, FixtureArgs("large"), TimeSpan.FromSeconds(2)).CollectAsync(default)).Error is not null, "oversized worker response is bounded");
        var inventory = new ComputerInventory(InventoryCategory.Names.Keys.Select(id => (IInventoryCategoryService)new IsolatedInventoryService(id, _worker)).ToArray());
        timer.Restart(); var actual = await inventory.RefreshAsync(default);
        Check(actual.Categories.Count == 7, "all seven real Windows inventory categories return structured outcomes");
        Check(actual.Categories.SelectMany(c => c.Records).SelectMany(r => r.Fields).Any(f => f.Key == "total" && f.Number > 0), "native GlobalMemoryStatusEx returns real memory");
        Check(!actual.Categories.SelectMany(c => c.Records).SelectMany(r => r.Fields).Any(f => f.Key.Contains("serial", StringComparison.OrdinalIgnoreCase) || f.Key.Contains("mac", StringComparison.OrdinalIgnoreCase)), "inventory does not collect serial numbers or MAC addresses");
        var counts = actual.Categories.Select(c => c.Id + ": " + (c.Error?.Kind.ToString() ?? string.Join(",", c.Records.SelectMany(r => r.Fields).GroupBy(f => f.Status).Select(g => g.Key + "=" + g.Count()))));
        Results.Add("OBSERVATION native inventory duration: " + timer.Elapsed.TotalSeconds.ToString("F2") + "s; " + string.Join("; ", counts));
        File.WriteAllText(Path.Combine(_root, "inventory-redacted.json"), InventoryExport.Json(InventoryExport.Select(actual, InventoryCategory.Names.Keys.ToHashSet())));
    }
    private sealed class Output : IProgress<ScriptOutput>
    { public ConcurrentQueue<ScriptOutput> Lines { get; } = new(); public void Report(ScriptOutput value) => Lines.Enqueue(value); }
    private static async Task ScriptsAsync()
    {
        var root = Fixture("scripts"); var engines = PowerShellHost.InstalledEngines();
        if (engines.Count == 0) { Results.Add("NOT RUN PowerShell: no runtime installed"); return; }
        foreach (var engine in engines)
        {
            var parser = new ScriptParameterService(); var execution = new ScriptExecutionService(root);
            var script = Path.Combine(root, "тест with spaces.ps1"); var marker = Path.Combine(root, "must-not-run.txt");
            File.WriteAllText(script, "param([string]$Value = $(Set-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value 'unsafe'))\nSet-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value 'unsafe'", new UTF8Encoding(true));
            await parser.InspectAsync(script, engine); Check(!File.Exists(marker), engine.Name + ": AST inspection does not execute defaults or body");
            File.WriteAllText(script, "param([string]$Value,[int]$Count,[switch]$Enabled,[string[]]$Items)\n[pscustomobject]@{Value=$Value;Count=$Count;Enabled=[bool]$Enabled;Items=($Items -join '|')}\nWrite-Warning 'test warning'", new UTF8Encoding(true));
            var definition = await parser.InspectAsync(script, engine); var output = new Output(); var special = "' ; $(Write-Error unsafe) ` \" кириллица";
            var request = new ScriptExecutionRequest(script, definition.Fingerprint, engine, [new("Value", "System.String", special), new("Count", "System.Int32", 12), new("Enabled", "System.Management.Automation.SwitchParameter", false), new("Items", "System.String[]", new[] { "a b", "x'y" })], []);
            var result = await execution.RunAsync(request, output);
            var policy = await new ExecutionPolicyService().ReadAsync(engine);
            Results.Add("OBSERVATION " + engine.Name + " effective policy: " + policy.Effective);
            if (result.ExecutionPolicyBlocked) { Results.Add("NOT RUN " + engine.Name + " script execution blocked by existing policy; policy left unchanged"); continue; }
            Check(result.ExitCode == 0 && output.Lines.Any(l => l.Fields.Any(f => f.Name == "Value" && f.Value == special)), engine.Name + ": literal JSON arguments and structured output");
            Check(output.Lines.Any(l => l.Kind == ScriptOutputKind.Warning), engine.Name + ": warning stream retained");
            Check(!Directory.EnumerateFiles(Path.Combine(root, "Runs"), "parameters.json", SearchOption.AllDirectories).Any(), engine.Name + ": parameter payload removed");
            var presentation = new RunResultPresentation(); presentation.Reset(root); foreach (var line in output.Lines) presentation.Accept(line); presentation.Complete(RunOutcome.Success);
            Check(presentation.HasTables && presentation.HasWarnings && presentation.Report().Contains(special), engine.Name + ": result tables and report remain available");
            File.WriteAllText(script, "Write-Error 'expected-test-failure'", new UTF8Encoding(true));
            request = request with { Fingerprint = ScriptParameterService.Fingerprint(script), Parameters = [] }; output = new(); result = await execution.RunAsync(request, output);
            Check(result.ExitCode != 0 && output.Lines.Any(l => l.IsError), engine.Name + ": script errors produce failure exit and stderr");
            File.WriteAllText(script, "Start-Sleep -Seconds 30", new UTF8Encoding(true)); request = request with { Fingerprint = ScriptParameterService.Fingerprint(script), Timeout = TimeSpan.FromMilliseconds(900) };
            result = await execution.RunAsync(request); Check(result.TimedOut && !result.Cancelled, engine.Name + ": execution deadline terminates process");
            using var cancellation = new CancellationTokenSource(700); result = await execution.RunAsync(request with { Timeout = TimeSpan.FromSeconds(30) }, cancellationToken: cancellation.Token);
            Check(result.Cancelled && !result.TimedOut, engine.Name + ": user cancellation distinct from timeout");
            var changed = false; try { await execution.RunAsync(request with { Fingerprint = "changed" }); } catch (InvalidOperationException) { changed = true; }
            Check(changed, engine.Name + ": changed script fingerprint rejects stale form");
        }
        Check(!PowerShellHost.StartInfo(engines.First(), "'x'").ArgumentList.Contains("-ExecutionPolicy"), "ordinary launch never disables execution policy");
        var field = new ScriptParameterViewModel(new() { Name = "Password", TypeName = "System.String" }); Check(field.IsSecret, "secret parameter identified for profile exclusion");
        var managed = new ExecutionPolicyService((_, _, _) => Task.FromResult("{\"Effective\":\"AllSigned\",\"MachinePolicy\":\"AllSigned\",\"UserPolicy\":\"Undefined\",\"CurrentUser\":\"Undefined\"}"));
        var managedRejected = false; try { await managed.AllowLocalScriptsAsync(engines.First()); } catch (InvalidOperationException) { managedRejected = true; }
        Check(managedRejected, "managed policy does not trigger automatic recovery or changes");
        var stressScript = Path.Combine(root, "bounded-output.ps1"); File.WriteAllText(stressScript, "[Console]::Out.Write(('x' * 1200000))", new UTF8Encoding(true));
        var stressOutput = new Output(); var stressResult = await new ScriptExecutionService(root).RunAsync(new(stressScript, ScriptParameterService.Fingerprint(stressScript), engines.First(), [], []), stressOutput);
        Check(stressResult.ExitCode == 0 && stressOutput.Lines.All(l => l.Text.Length <= 16_010), "newline-free output is drained with bounded preview");
        var childScript = Path.Combine(root, "child-process.ps1"); var executable = engines.First().ExecutablePath.Replace("'", "''");
        File.WriteAllText(childScript, "$info = New-Object Diagnostics.ProcessStartInfo; $info.FileName='" + executable + "'; $info.Arguments='-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30'; $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $child=[Diagnostics.Process]::Start($info); Write-Output ('ChildPid: ' + $child.Id); Start-Sleep -Seconds 30", new UTF8Encoding(true));
        var childOutput = new Output(); var childResult = await new ScriptExecutionService(root).RunAsync(new(childScript, ScriptParameterService.Fingerprint(childScript), engines.First(), [], []) { Timeout = TimeSpan.FromSeconds(3) }, childOutput);
        var pidLine = childOutput.Lines.FirstOrDefault(l => l.Text.StartsWith("ChildPid: "))?.Text;
        var childExited = false;
        if (int.TryParse(pidLine?[10..], out var childPid)) { try { using var child = Process.GetProcessById(childPid); childExited = child.HasExited; } catch (ArgumentException) { childExited = true; } }
        Check(childResult.TimedOut && childExited, "Windows Job Object terminates spawned descendants on deadline");
    }
    private static T Named<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task MotionAsync()
    {
        Motion.Reduced = false;
        var clicks = 0; var button = Ui.Action("Проверка действия", () => clicks++, "primary"); button.Width = 200;
        var content = Ui.Stack(Ui.Text("Движение Shmonov Studio", "heading"), button, Ui.Card(Ui.Text("Наведение и нажатие", "section")));
        content.Margin = new(24);
        var window = new Window { Width = 520, Height = 300, Content = content }; window.Show(); await Task.Delay(50);
        try
        {
            var reveal = Motion.RevealAsync(content, 300, 10); await Task.Delay(90); using (window.CaptureRenderedFrame()) { }
            Check(content.Opacity is > 0 and < 1, "page entrance has a real intermediate opacity");
            await reveal; Check(content.Opacity == 1, "page entrance restores fully readable content");
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center); await Task.Delay(220); using (window.CaptureRenderedFrame()) { }
            Check(button.RenderTransform is TransformGroup group && group.Children.OfType<TranslateTransform>().Any(t => t.Y < 0), "button hover produces a subtle lift");
            window.MouseDown(center, Avalonia.Input.MouseButton.Left); await Task.Delay(180); using (window.CaptureRenderedFrame()) { }
            Check(button.RenderTransform is TransformGroup pressed && pressed.Children.OfType<ScaleTransform>().All(t=>t.ScaleX<1&&t.ScaleY<1),"pressed button visibly compresses before release");
            window.MouseUp(center, Avalonia.Input.MouseButton.Left); await Task.Delay(60);
            Check(clicks == 1, "animated button retains click behavior"); SaveFrame(window, "motion-hover");
            var active = Motion.RevealAsync(content, 1000, 10); await Task.Delay(40); Motion.Reduced = true; await active;
            Check(content.Opacity == 1 && Motion.ActiveCount == 0, "reduced motion immediately cancels running entrances");
            Check(button.RenderTransform is TransformGroup reduced && reduced.Children.OfType<TranslateTransform>().All(t => t.Y == 0), "reduced motion resets hover displacement");
            var watch = Stopwatch.StartNew(); await Motion.RevealAsync(content, 1000, 10); Check(watch.ElapsedMilliseconds < 100 && content.Opacity == 1, "reduced motion skips future page effects");
            Motion.Reduced = false; var detached = Motion.RevealAsync(content, 1000, 10); window.Content = Ui.Text("Следующий экран");
            await detached; Check(Motion.ActiveCount == 0, "detached views release animation clocks and cancellation state");
            var cards=Ui.Stack(Enumerable.Range(0,3).Select(i=>(Control)Ui.Card(Ui.Text("Карточка "+i))).ToArray());
            cards.Margin=new(24);window.Content=cards;await Task.Delay(40);Motion.Enter(cards,cascade:true);await Task.Delay(90);using(window.CaptureRenderedFrame()){}
            Check(cards.Children.Any(c=>c.Opacity is >0 and <1),"cards have a real intermediate entrance frame");
            window.Content=Ui.Text("Другой экран");await Task.Delay(100);Check(Motion.ActiveCount==0,"changing page cancels every staggered entrance");
            var tall=new Border{Height=1500,Child=Ui.Text("Длинная панель")};var scroll=Ui.Scroll(tall);window.Content=scroll;await Task.Delay(100);window.MouseMove(new Point(200,100));window.MouseWheel(new Point(200,100),new Vector(0,-3));await Task.Delay(100);
            Check(scroll.Offset.Y>0,"hidden scrollbar retains mouse wheel scrolling");
            Check(!scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any(b=>b.IsEffectivelyVisible),"scrollbar chrome is not visible even while scrolling");
            scroll.ScrollToEnd();await Task.Delay(60);Check(scroll.Offset.Y>500,"long content remains reachable without a visible scrollbar");
        }
        finally { window.Close(); Motion.Reduced = false; }
    }
    private static void SaveFrame(Window window, string name)
    { window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); if (frame is null) throw new Exception("No rendered frame: " + name); frame.Save(Path.Combine(_root, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a,b) + .05) / (Math.Min(a,b) + .05);
    }
    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > timeout) throw new TimeoutException("UI initialization timed out"); await Task.Delay(100); } }
}
