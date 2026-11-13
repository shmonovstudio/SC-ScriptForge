using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using ShmonovStudio.AI;

internal static partial class Checks
{
    private const string BrokenInventory = """
        Import-Module System.Management
        Get-CimInstance Win32_OperatingSystem | Select-Object CSName, Caption, Version, LastBootUpTime
        Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model, TotalPhysicalMemory
        Get-CimInstance Win32_LogicalDisk -Filter "DriveType = 3" | Select-Object DeviceID, Size, FreeSpace
        Get-CimInstance Win32_ComputerSystemMonitor -Filter "Connected = true" | Select-Object Model, Manufacturer, Connected, VideoBus
        """;
    private const string FixedInventory = """
        Get-CimInstance Win32_OperatingSystem | Select-Object CSName, Caption, Version, LastBootUpTime
        Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model, TotalPhysicalMemory
        Get-CimInstance Win32_LogicalDisk -Filter "DriveType = 3" | Select-Object DeviceID, Size, FreeSpace
        Get-CimInstance Win32_DesktopMonitor | Select-Object Name, MonitorManufacturer, MonitorType, Status
        """;
    private static async Task AiRepairAsync()
    {
        var root = Fixture("runtime-repair");
        var repository = new ScriptRepository(root); var parser = new PowerShellContextService();
        var service = new ScriptAiService(repository, parser); var dependencies = new PowerShellDependencyService();
        var diagnostics = await dependencies.InspectAsync(BrokenInventory);
        Check((await parser.InspectAsync(BrokenInventory)).Errors.Length == 0 && diagnostics.Errors.Length == 2
            && diagnostics.Errors.Any(e => e.Contains("System.Management")) && diagnostics.Errors.Any(e => e.Contains("Win32_ComputerSystemMonitor")),
            "runtime repair catches both original failures that the PowerShell Parser accepts");
        var wrongFields = await dependencies.InspectAsync("Get-CimInstance Win32_DesktopMonitor -Filter 'Connected = true' | Select-Object Model, Manufacturer, Connected, VideoBus");
        Check(wrongFields.Errors.Length == 1 && wrongFields.Errors[0].Contains("Connected") && wrongFields.Errors[0].Contains("VideoBus"), "changing only the CIM class cannot hide invalid filters and selected fields");
        Check((await dependencies.InspectAsync("Get-CimInstance Win32_DesktopMonitor -Filter 'Status = 1'")).Errors.Any(e => e.Contains("String")),
            "a replacement filter cannot silently compare textual monitor status to a guessed numeric value");
        Check((await dependencies.InspectAsync(FixedInventory)).Errors.Length == 0, "real inventory classes, filter and property names pass local metadata checks");
        var marker = Path.Combine(root, "must-not-execute.txt").Replace("'", "''");
        var dynamic = await dependencies.InspectAsync("param($x=$(Set-Content '" + marker + "' unsafe))\nGet-CimInstance -ClassName $(Set-Content '" + marker + "' unsafe)\nSet-Content '" + marker + "' unsafe");
        Check(!File.Exists(marker) && dynamic.Notes.Length > 0, "dependency inspection never evaluates defaults, dynamic arguments or the script body");
        var remote = await dependencies.InspectAsync("Get-CimInstance -ComputerName example.invalid -ClassName MissingClass");
        Check(remote.Errors.Length == 0 && remote.Notes.Length == 1, "dependency inspection does not connect to script-supplied remote hosts");
        var entry = repository.Create("System Inventory", "Windows", "", "", [], false, "Draft");
        repository.WriteCode(entry, BrokenInventory);
        var context = await service.ContextAsync(entry); var fix = AiTaskDefinition.Get("fix");
        const string failure = "Указанный модуль System.Management не найден.\nНедопустимый класс Win32_ComputerSystemMonitor";
        var request = await service.RequestAsync(fix, context, context.Choices[0], 0, 0, "Исправь", failure, null, 2048,
            [new("user", "Ранее не помог Import-Module System.Diagnostics"), new("assistant", "Предыдущее неудачное предложение")], default);
        Check(request.Messages.Any(m => m.Content.Contains("Ранее не помог")) && request.Messages.Last().Content.Contains("Подтверждённые проблемы")
            && request.Messages.Last().Content.Contains("Win32_DesktopMonitor"), "fix requests retain the prior attempt and verified local metadata");
        var unchanged = await service.ProposeAsync(fix, context, context.Choices[0], 0, 0, new("Код менять не нужно", BrokenInventory, "", "", "", []), default);
        Check(unchanged is { SyntaxValid: true, IsValid: false } && unchanged.DependencyErrors.Length == 2, "an unchanged but broken candidate is rejected instead of blaming the environment");
        var refused = false; try { await service.ApplyAsync(unchanged!); } catch (InvalidOperationException) { refused = true; }
        Check(refused && repository.ReadForReview(entry).ContentHash == context.Snapshot.ContentHash, "verified dependency errors prevent saving a broken AI fix");

        var store = new AiSettingsStore(root);
        var model = Path.Combine(root, "fixture.gguf"); var native = Path.Combine(root, "llama-server.exe");
        File.WriteAllText(model, "fixture"); File.WriteAllText(native, "fixture");
        store.Save(new() { ModelPath = model, RuntimePath = native });
        using var library = new LibraryViewModel(repository, new LibrarySearch(), new());
        await library.RefreshAsync();
        using var runtime = new InteractionRuntime();
        using var vm = new AiAssistantViewModel(runtime, service, store, library, new DiagnosticLog(root));
        vm.SelectedEntry = entry; await vm.Preparation; vm.ErrorText = failure; vm.Instruction = "Исправь ошибку";
        string Reply(string code) => JsonSerializer.Serialize(new AiResult("Предложено исправление.", code, "", "", "", []));
        runtime.Responses.Enqueue(Reply(BrokenInventory.Replace("System.Management", "System.Diagnostics")));
        runtime.Responses.Enqueue(Reply(BrokenInventory));
        runtime.Responses.Enqueue(Reply(FixedInventory));
        await vm.GenerateAsync();
        Check(runtime.Requests == 3 && vm.CanApply && vm.Proposal is { IsValid: true, DependenciesChecked: true }
            && vm.Proposal.Code!.Contains("Win32_DesktopMonitor"), "runtime failures trigger two bounded repair attempts even when every candidate passes Parser");
        Check(vm.Status.Contains("ещё не проверялось") && repository.ReadForReview(entry).ContentHash == context.Snapshot.ContentHash, "a preflight pass is not presented as a successful execution and does not write the script");
        vm.DiscardProposal(); runtime.Response = Reply(FixedInventory);
        vm.Instruction = "Не помогло, исправь ещё раз";
        var retryIsFix = vm.EffectiveTask.Id == "fix";
        await vm.GenerateAsync();
        Check(retryIsFix && runtime.LastRequest!.Messages.Any(m => m.Content.Contains("Предыдущая попытка"))
            && runtime.LastRequest.Messages.Last().Content.Contains(failure), "a retry keeps the runtime error after the visible composer was cleared");
        var first = vm.Proposal!; await vm.ApplyAsync(first);
        runtime.Response = Reply(FixedInventory); vm.Instruction = "Исправь";
        await vm.GenerateAsync();
        Check(!vm.HasProposal && !vm.Status.Contains("причина ошибки не в скрипте"), "no-op answers do not claim a proven environmental root cause");
        vm.NewConversation(); repository.WriteCode(entry, BrokenInventory); await vm.PrepareAsync();
        runtime.Response = Reply(BrokenInventory); vm.ErrorText = failure; vm.Instruction = "Исправь";
        var before = runtime.Requests; await vm.GenerateAsync();
        Check(runtime.Requests == before + 3 && vm.Proposal is { IsValid: false } && !vm.CanApply, "three bad candidates stop with a blocked proposal rather than an endless retry or false success");
        var other = repository.Create("Другой скрипт", "Windows", "", "", [], false, "Draft"); repository.WriteCode(other, "Get-Date\n");
        vm.SelectedEntry = other; await vm.Preparation; runtime.Response = Reply("Get-Date -Format yyyy-MM-dd\n"); vm.Instruction = "Исправь";
        await vm.GenerateAsync();
        Check(runtime.LastRequest!.Messages.Count == 2 && !runtime.LastRequest.Messages.Last().Content.Contains(failure), "repair memory is cleared when switching to another script");

        var badPath = Path.Combine(root, "runtime-error.ps1"); File.WriteAllText(badPath, BrokenInventory, new UTF8Encoding(true));
        var output = new Output(); var engine = PowerShellHost.InstalledEngines().First();
        var run = await new ScriptExecutionService(root).RunAsync(new(badPath, ScriptParameterService.Fingerprint(badPath), engine, [], []), output);
        Check(run.ExitCode != 0 && output.Lines.Any(l => l.IsError && l.Text.Contains("FullyQualifiedErrorId"))
            && output.Lines.Any(l => l.IsError && l.Text.Contains("Win32_ComputerSystemMonitor")), "execution errors now retain command names, source lines and error IDs for AI diagnosis");
        var goodPath = Path.Combine(root, "fixed-inventory.ps1"); File.WriteAllText(goodPath, FixedInventory, new UTF8Encoding(true));
        output = new Output();
        run = await new ScriptExecutionService(root).RunAsync(new(goodPath, ScriptParameterService.Fingerprint(goodPath), engine, [], []), output);
        Check(run.ExitCode == 0 && !output.Lines.Any(l => l.IsError) && output.Lines.Any(l => l.Fields.Any(f => f.Name == "MonitorManufacturer")),
            "the corrected read-only inventory runs locally with monitor data and no runtime errors");
    }

    private static async Task AiRepairLiveAsync()
    {
        var workspace = Path.GetFullPath(Path.Combine(_root, "..", ".."));
        var root = Fixture("live-repair"); var repository = new ScriptRepository(root);
        var entry = repository.Create("System Inventory", "Windows", "", "", [], false, "Draft"); repository.WriteCode(entry, BrokenInventory);
        var service = new ScriptAiService(repository, new PowerShellContextService());
        var store = new AiSettingsStore(root);
        store.Save(new() { RuntimePath = Path.Combine(workspace, "AI", "Runtime", "llama-server.exe"), ModelPath = Path.Combine(workspace, "AI", "Models", AiCatalog.Qwen4B.FileName), ContextTokens = 4096, MaximumOutputTokens = 1024, Threads = 8 });
        using var library = new LibraryViewModel(repository, new LibrarySearch(), new()); await library.RefreshAsync();
        using var runtime = new LlamaCppRuntime(store.Load());
        using var vm = new AiAssistantViewModel(runtime, service, store, library, new DiagnosticLog(root));
        vm.SelectedEntry = entry; await vm.Preparation;
        vm.ErrorText = "Import-Module System.Management: модуль не найден. Get-CimInstance Win32_ComputerSystemMonitor: Недопустимый класс.";
        vm.Instruction = "Исправь обе ошибки, сохрани инвентаризацию ОС, компьютера, дисков и мониторов.";
        var watch = Stopwatch.StartNew(); await vm.GenerateAsync();
        File.WriteAllText(Path.Combine(_root, "answer.txt"), vm.Answer + "\n\n" + vm.Status + "\n\n" + vm.Validation);
        if (vm.Proposal?.Code is { } code) File.WriteAllText(Path.Combine(_root, "proposed-inventory.ps1"), code, new UTF8Encoding(true));
        Check(vm.CanApply && vm.Proposal is { IsValid: true, DependenciesChecked: true } && !vm.Proposal.Code!.Contains("Import-Module System."), "real Qwen3.5 4B produces an inventory fix that passes both syntax and local dependency validation");
        Check(repository.ReadForReview(entry).Code == BrokenInventory, "live model verification leaves the original script unapplied");
        Results.Add("OBSERVATION real local inventory repair: " + watch.Elapsed.TotalSeconds.ToString("F1") + " s; " + vm.Status);
    }
}
