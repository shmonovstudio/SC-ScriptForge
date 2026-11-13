using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using ShmonovStudio.AI;

internal static partial class Checks
{
    private static async Task AiPublishedAsync()
    {
        var workspace=Path.GetFullPath(Path.Combine(_root,"..","..")); var folder=Path.Combine(workspace,"artifacts","publish-win-x64","AI","Runtime");
        Check(File.Exists(Path.Combine(folder,"llama-server.exe")) && Directory.GetFiles(folder,"*.dll").Length>10,"published native AI executable and dependencies remain outside the .NET bundle");
        using var runtime=new LlamaCppRuntime(new() {RuntimePath=Path.Combine(folder,"llama-server.exe"),ModelPath=Path.Combine(workspace,"AI","Models",AiCatalog.Qwen4B.FileName),ContextTokens=2048,Threads=6,MaximumOutputTokens=128});
        var answer=new StringBuilder();
        await foreach(var chunk in runtime.GenerateAsync(new([new("user","Ответь одним словом: Привет.")],32))) answer.Append(chunk.Text);
        Check(answer.Length>0 && runtime.State.Loaded,"published CPU runtime performs real Qwen3.5 inference");
        File.WriteAllText(Path.Combine(_root,"answer.txt"),answer.ToString()); await runtime.UnloadAsync();
        Check(!runtime.State.Loaded && runtime.OwnedProcessId is null,"published AI runtime unloads cleanly");
    }
    private static async Task AiInstallAsync()
    {
        var workspace=Path.GetFullPath(Path.Combine(_root,"..","..")); var folder=Path.Combine(workspace,"AI");
        var model=Path.Combine(folder,"Models",AiCatalog.Qwen4B.FileName); var stamp=File.GetLastWriteTimeUtc(model);
        var options=await new AiInstaller().InstallAsync(folder,AiCatalog.Qwen4B);
        options.Validate();
        Check(File.Exists(options.RuntimePath) && GgufMetadata.Read(options.ModelPath).Architecture=="qwen35","production installer downloads and verifies pinned CPU runtime and real model");
        Check(File.GetLastWriteTimeUtc(model)==stamp,"installer reuses matching model without rewriting or downloading it");
        Check(typeof(AiInstaller).Assembly.GetManifestResourceNames().Count(n=>n.Contains("Licenses"))==2,"reusable AI module includes model and runtime license resources");
        File.WriteAllText(Path.Combine(_root,"installation.json"),JsonSerializer.Serialize(options));
        using var cancel=new CancellationTokenSource(); cancel.Cancel(); var cancelled=false; var target=Path.Combine(_root,"cancelled.zip");
        try { await AiInstaller.DownloadAsync(AiCatalog.RuntimeUrl,target,19537886,AiCatalog.RuntimeSha256,"test",null,cancel.Token); } catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled && !File.Exists(target) && Directory.GetFiles(_root,"*.download").Length==0,"cancelled installation leaves no incomplete artifact");
    }
    private static async Task AiAsync()
    {
        var root = Fixture("contracts"); var repository = new ScriptRepository(root); var parser = new PowerShellContextService(); var service = new ScriptAiService(repository,parser);
        var marker = Path.Combine(root,"must-not-execute.txt").Replace("'","''");
        var entry = repository.Create("ИИ проверка", "Windows", "Исходное описание", "Заметки", ["test"], false, "Draft");
        repository.WriteCode(entry,"param([string]$Name = $(Set-Content -LiteralPath '" + marker + "' -Value 'unsafe'))\nfunction Get-Пример {\n  'Привет'\n}\nSet-Content -LiteralPath '" + marker + "' -Value 'unsafe'");
        var context = await service.ContextAsync(entry);
        Check(!File.Exists(marker) && context.Inspection.Functions.Single().Name == "Get-Пример", "AI AST context identifies Unicode functions without executing defaults or body");
        var function = context.Choices.Last();
        var request = await service.RequestAsync(AiTaskDefinition.All.First(t => t.Id == "explain"),context,function,0,0,"","",null,512,[],default);
        Check(request.Messages.Last().Content.Contains("function Get-Пример") && !request.Messages.Last().Content.Contains(marker), "AI function context excludes unrelated script body");
        var range = ScriptAiService.Range(context,null,2,4); Check(context.Snapshot.Code[range.Start..range.End].Contains("function Get-Пример"), "explicit line range maps to original source offsets");
        var partial = "{\"answer\":\"Текст \\n \\u0410 \\\"цитата\\\"\",\"replacement\":\"$x=1\"}";
        Check(StreamingJson.StringProperty(partial,"answer") == "Текст \n А \"цитата\"" && StreamingJson.StringProperty(partial[..15],"answer").Length > 0, "stream projector decodes partial JSON and escaped Unicode");
        var valid = new AiResult("Добавлен комментарий","function Get-Пример {\n  # Понятный комментарий\n  'Привет'\n}","","","",[]);
        var versions = repository.GetVersions(entry).Count; var hash = repository.ReadForReview(entry).ContentHash;
        var proposal = await service.ProposeAsync(AiTaskDefinition.All.First(t => t.Id == "comments"),context,function,0,0,valid,default);
        Check(proposal is { SyntaxValid:true } && repository.ReadForReview(entry).ContentHash == hash && repository.GetVersions(entry).Count == versions, "AI proposal and Parser validation do not write a file or revision");
        var changed = await service.ApplyAsync(proposal!);
        Check(repository.GetVersions(changed).Count > versions && ScriptRepository.ReadCode(changed.FilePath).Contains("Понятный комментарий") && !File.Exists(marker), "reviewed AI code creates history without executing script");
        var stale = false; try { await service.ApplyAsync(proposal!); } catch (IOException) { stale = true; }
        Check(stale,"AI cannot overwrite source modified after its snapshot");
        context = await service.ContextAsync(changed); function = context.Choices.Last();
        var invalid = await service.ProposeAsync(AiTaskDefinition.All.First(t => t.Id == "comments"),context,function,0,0,valid with { Replacement="function {" },default);
        var rejected = false; try { await service.ApplyAsync(invalid!); } catch (InvalidOperationException) { rejected = true; }
        Check(invalid?.SyntaxValid == false && rejected,"Parser errors block applying AI code");
        var fixTask = AiTaskDefinition.Get("fix");
        var repair = await service.RepairRequestAsync(fixTask,context,function,0,0,"function Get-Пример {\n  if ($true) {\n  'Привет'\n}",512,default);
        var repairLines = System.Text.RegularExpressions.Regex.Matches(repair?.Messages.Last().Content ?? "",@"Строка (\d+):").Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Check(repair is not null && repair.Messages.Last().Content.Contains("не проходит проверку PowerShell Parser") && repairLines.Length > 0 && repairLines.All(n => n is >= 1 and <= 4),"AI repair request reports Parser errors in fragment line numbers");
        Check(await service.RepairRequestAsync(fixTask,context,function,0,0,valid.Replacement,512,default) is null,"valid AI code needs no repair request");
        var brokenEntry = repository.Create("Сломанный","Windows","","",[],false,"Draft");
        repository.WriteCode(brokenEntry,"Get-Date\nif ($true) {\n  'Привет'\n");
        var brokenContext = await service.ContextAsync(brokenEntry);
        var fixRequest = await service.RequestAsync(fixTask,brokenContext,brokenContext.Choices[0],0,0,"","",null,512,[],default);
        Check(brokenContext.Inspection.Errors.Length > 0 && fixRequest.Messages.Last().Content.Contains("PowerShell Parser нашёл") && fixRequest.JsonSchema!.Contains("minLength"),"fix request shows Parser errors to the model and requires corrected code");
        var metadata = await service.ProposeAsync(AiTaskDefinition.All.First(t => t.Id == "tags"),context,function,0,0,new("Название и теги","","Переименованный ИИ пример","","",["powershell","пример"]),default);
        var id = changed.Id; var history = repository.GetVersions(changed).Count; changed = await service.ApplyAsync(metadata!);
        Check(changed.Id == id && changed.Name == "Переименованный ИИ пример" && repository.GetVersions(changed).Count >= history && changed.Tags.Contains("пример"),"reviewed metadata rename preserves stable ID and history");
        var unchanged = repository.ReadForReview(changed); var unchangedVersions = repository.GetVersions(changed).Count;
        repository.ApplyReviewedChange(unchanged,unchanged.Code,null);
        Check(repository.GetVersions(changed).Count == unchangedVersions,"identical AI content creates no unnecessary revisions");
        var unicode = repository.Create("Unicode","Windows","","",[],false,"Draft");
        File.WriteAllText(unicode.FilePath,"# Пример\r\n'Один'\r\n",Encoding.Unicode);
        var unicodeSnapshot = repository.ReadForReview(unicode); repository.ApplyReviewedChange(unicodeSnapshot,unicodeSnapshot.Code+"'Два'\r\n",null);
        Check(File.ReadAllBytes(unicode.FilePath).AsSpan().StartsWith(new byte[]{0xFF,0xFE}) && ScriptRepository.ReadCode(unicode.FilePath).Contains("\r\n"),"reviewed AI edit preserves UTF16 BOM and CRLF");
        var metaContext = await service.ContextAsync(changed);
        var metaProposal = await service.ProposeAsync(AiTaskDefinition.All.First(t=>t.Id=="summary"),metaContext,metaContext.Choices[0],0,0,new("Описание","","","Новое описание","",[]),default);
        changed.Favorite = !changed.Favorite; repository.SaveMeta(changed); var staleMetadata = false;
        try { await service.ApplyAsync(metaProposal!); } catch(IOException) { staleMetadata = true; }
        Check(staleMetadata,"AI cannot overwrite metadata changed after generation");
        var protectedDownload = Path.Combine(root,"download.gguf"); File.WriteAllText(protectedDownload,"test"); var protectedHash = false;
        try { await AiInstaller.DownloadAsync("https://invalid.invalid/",protectedDownload,4,new string('0',64),"test",null,default); } catch(IOException) { protectedHash = true; }
        Check(protectedHash && File.ReadAllText(protectedDownload)=="test","installer refuses a hash mismatch without overwriting existing file");
        var logger = new DiagnosticLog(root); logger.Failure("ai.failure",new IOException("secret-ai-prompt-password"));
        Check(!File.ReadAllText(logger.Path).Contains("secret-ai-prompt-password"),"AI diagnostics exclude prompt and exception message");
        using var missing = new LlamaCppRuntime(new()); var unavailable = false;
        try { await foreach (var _ in missing.GenerateAsync(new([new("user","Hello")],128))) { } } catch (InvalidOperationException) { unavailable = true; }
        Check(unavailable && !missing.State.Loaded && missing.OwnedProcessId is null,"missing AI installation reports real diagnostics without fake generation");
        var settingsStore = new AiSettingsStore(root);
        var runtimeFixture = Path.Combine(root,"llama-server.exe");
        settingsStore.Save(new AiRuntimeOptions { RuntimePath=runtimeFixture, ModelPath="" });
        using var services = Bootstrap.CreateServices(root,_worker,installBundledScripts:false); var window = services.GetRequiredService<MainWindow>();
        window.Show(); await WaitUntil(() => window.Library.Entries.Count > 0,TimeSpan.FromSeconds(10)); window.Navigate("ai"); await Task.Delay(250);
        var vm = services.GetRequiredService<AiAssistantViewModel>();
        vm.Instruction="Объясни Get-Date"; await vm.GenerateAsync();
        Check(vm.Answer.Length==0 && vm.Status.Contains("Не установлен llama.cpp"),"production assistant without runtime shows setup guidance and no synthetic answer");
        // Validation stops at the missing model, so this stub is never executed.
        File.WriteAllText(runtimeFixture,"fixture");
        vm.Instruction="Объясни Get-Date"; await vm.GenerateAsync();
        Check(vm.Answer.Length==0 && vm.Status.Contains("Модель не установлена"),"production assistant without model shows setup guidance and no synthetic answer");
        Check(!services.GetRequiredService<IAiRuntime>().State.Loaded,"opening SC AI screen does not load model into RAM");
        Check(window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "AiGenerate") && !Named<Button>(window,"AiApply").IsEffectivelyVisible,"AI has working composer and no apply action before proposal");
        SaveFrame(window,"ai-dark"); window.Width=960; window.Height=640; await Task.Delay(180); SaveFrame(window,"ai-960");
        var generate = Named<Button>(window,"AiGenerate");
        Check(generate.TranslatePoint(new Avalonia.Point(generate.Bounds.Width,generate.Bounds.Height),window) is { } bottom && bottom.X <= window.Bounds.Width && bottom.Y <= window.Bounds.Height,"AI composer fits minimum window size");
        Avalonia.Application.Current!.RequestedThemeVariant=ThemeVariant.Light; await Task.Delay(100); SaveFrame(window,"ai-light");
        var settingsTask = AiSettingsWindow.ShowAsync(window,vm,services.GetRequiredService<AiSettingsStore>()); await Task.Delay(180); var settings = window.OwnedWindows.Single(); SaveFrame(settings,"ai-settings"); settings.Close(); await settingsTask;
        var errorTransferred=false; var runner=new ScriptRunWindow(changed,root,services.GetRequiredService<RunHistoryStore>(),services.GetRequiredService<DiagnosticLog>(),(script,error)=> {errorTransferred=script.Id==changed.Id && error.Contains("Тестовая ошибка");return true;});
        runner.Show(window); await WaitUntil(()=>!runner.ViewModel.IsLoading,TimeSpan.FromSeconds(15)); runner.ViewModel.ShowMessage("Тестовая ошибка");
        Click(runner.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Исправить ошибку в SC AI")); await Task.Delay(50);
        Check(errorTransferred,"run result transfers selected script and error to AI only on explicit action"); runner.Close();
        await WaitUntil(() => !services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(90)); window.Close();
        Avalonia.Application.Current.RequestedThemeVariant=ThemeVariant.Dark;
    }

    private sealed class InteractionRuntime : IAiRuntime
    {
        public AiRuntimeState State { get; private set; } = new("Тестовый поток",false);
        public event Action<AiRuntimeState>? StateChanged;
        public AiRequest? LastRequest { get; private set; }
        public bool Hold { get; set; }
        public string Response { get; set; } = JsonSerializer.Serialize(new AiResult("### Назначение\n**Get-Date** возвращает дату.\n\n```powershell\nGet-Date -Format 'yyyy-MM-dd'\n```","","","","",[]));
        /// <summary>Ответы по очереди для многошаговых сценариев; когда очередь пуста — <see cref="Response"/>.</summary>
        public Queue<string> Responses { get; } = new();
        public int Requests { get; private set; }
        public void Configure(AiRuntimeOptions options) { }
        public async IAsyncEnumerable<AiDelta> GenerateAsync(AiRequest request,[EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request; Requests++; State = new("Тестовый поток",true); StateChanged?.Invoke(State);
            if (Hold) { yield return new("{\"answer\":\"Неполный ответ"); await Task.Delay(Timeout.Infinite,cancellationToken); }
            var response = Responses.Count > 0 ? Responses.Dequeue() : Response;
            for (var offset=0;offset<response.Length;offset+=40) { cancellationToken.ThrowIfCancellationRequested(); yield return new(response.Substring(offset,Math.Min(40,response.Length-offset)),offset+40>=response.Length); await Task.Delay(10,cancellationToken); }
        }
        public Task UnloadAsync(CancellationToken cancellationToken = default) { State = new("Выгружено",false); StateChanged?.Invoke(State); return Task.CompletedTask; }
        public void Dispose() { }
    }
    private static async Task AiInteractionAsync()
    {
        foreach (var (text,script,draft,expected) in new[]
        {
            ("Напиши скрипт, который удаляет временные файлы старше 7 дней",false,false,"create"),
            ("Удали временные файлы старше 7 дней",false,false,"create"),
            ("Как в PowerShell проверить, запущена ли служба?",false,false,"ask"),
            ("Почему появляется ошибка «running scripts is disabled on this system»?",false,false,"error"),
            ("Get-Item : Cannot find path\n+ CategoryInfo : ObjectNotFound\n+ FullyQualifiedErrorId : PathNotFound",true,false,"fix"),
            ("Get-Item : Cannot find path\n+ CategoryInfo : ObjectNotFound\n+ FullyQualifiedErrorId : PathNotFound",false,false,"error"),
            ("Исправь синтаксическую ошибку",true,false,"fix"),
            ("Скрипт не запускается, выдаёт ошибку",true,false,"fix"),
            ("Почему скрипт падает?",true,false,"fix"),
            ("Исправь ошибку в строке 3",false,true,"create"),
            ("",true,false,"explain"),
            ("Объясни, что делает этот скрипт",true,false,"explain"),
            ("Найди проблемы и слабые места",true,false,"audit"),
            ("Добавь обработку ошибок и журналирование",true,false,"complete"),
            ("Добавь комментарии",true,false,"comments"),
            ("Оптимизируй цикл",true,false,"optimize"),
            ("Придумай название и теги",true,false,"tags"),
            ("Сделай подробное описание для коллег",true,false,"description"),
            ("Что изменилось по сравнению с прошлой версией?",true,false,"changes"),
            ("Добавь параметр для пути",false,true,"create"),
            ("Напиши новый скрипт для отчёта по дискам",true,false,"create")
        })
            Check(AiIntentRouter.Detect(text,script,draft)==expected,$"intent router: «{text.Split('\n')[0]}» → {expected}");
        Check(ScriptAiService.Parse("<think>план</think>\n```json\n{\"answer\":\"Ок\",\"replacement\":\"```powershell\\nGet-Date\\n```\",\"tags\":\"a, b\"}\n```") is { Answer:"Ok" or "Ок", Replacement:"Get-Date", Tags.Length:2 },"lenient parser accepts reasoning, fences and missing fields from external servers");

        var root = Fixture("interaction"); var repository = new ScriptRepository(root);
        var entry = repository.Create("Проверка системы и подготовка подробного отчёта об окружении", "Windows", "", "",[],false,"Draft");
        repository.WriteCode(entry,"function Get-EnvironmentReport {\n    Get-Date\n}\nGet-EnvironmentReport\n");
        var hash = repository.ReadForReview(entry).ContentHash;
        var modelFile = Path.Combine(root,"fixture.gguf"); var runtimeFile = Path.Combine(root,"llama-server.exe"); File.WriteAllText(modelFile,"fixture"); File.WriteAllText(runtimeFile,"fixture");
        var store = new AiSettingsStore(root); store.Save(new() { ModelPath=modelFile,RuntimePath=runtimeFile });
        using var services = Bootstrap.CreateServices(root,_worker,installBundledScripts:false); var library = services.GetRequiredService<LibraryViewModel>();
        using var runtime = new InteractionRuntime(); using var vm = new AiAssistantViewModel(runtime,services.GetRequiredService<ScriptAiService>(),store,library,services.GetRequiredService<DiagnosticLog>());
        var window = new MainWindow(services.GetRequiredService<ScriptRepository>(),library,services.GetRequiredService<ComputerViewModel>(),services.GetRequiredService<PreferencesStore>(),services.GetRequiredService<AppPreferences>(),services.GetRequiredService<RunHistoryStore>(),services.GetRequiredService<DiagnosticLog>(),services.GetRequiredService<IPrivilegeService>(),vm,store);
        window.Width=1370; window.Height=880; window.Show(); await WaitUntil(()=>library.Entries.Count>0,TimeSpan.FromSeconds(10)); window.Navigate("ai"); await Task.Delay(250);
        SaveFrame(window,"ai-welcome");

        // Новый скрипт без прикреплённого контекста: живой код, черновик, сохранение в библиотеку.
        vm.Instruction="Напиши скрипт, который выводит текущую дату"; await Task.Delay(50);
        Check(vm.AutoDetect && vm.EffectiveTask.Id=="create" && Named<Button>(window,"AiIntent").IsEffectivelyVisible,"composer shows the automatically detected task instead of a long task list");
        runtime.Response=JsonSerializer.Serialize(new AiResult("Скрипт выводит дату. Запустите без параметров.","[CmdletBinding()]\nparam([string]$Format = 'yyyy-MM-dd')\nGet-Date -Format $Format","Текущая дата","Выводит текущую дату в заданном формате.","",["дата","пример"]));
        await vm.GenerateAsync(); await Task.Delay(200);
        Check(vm.HasNewScript && vm.NewScript!.SyntaxValid && vm.Draft.Contains("Get-Date") && Named<Border>(window,"AiNewScriptCard").IsEffectivelyVisible && Named<Button>(window,"AiSaveScript").IsEnabled,"AI writes a new script as a reviewable draft with save action");
        Check(runtime.LastRequest!.JsonSchema!.Contains("minLength") && runtime.LastRequest.Messages.Last().Content.Contains("Напиши новый законченный PowerShell-скрипт"),"new script request uses the dedicated generation contract");
        SaveFrame(window,"ai-new-script");
        Click(Named<Button>(window,"AiOpenCode")); await Task.Delay(150); SaveFrame(window,"ai-new-script-code");
        Check(Named<TabControl>(window,"AiTabs").SelectedIndex==2 && !Named<Border>(window,"AiComposer").IsEffectivelyVisible,"generated code opens in a full-height code view");
        Named<TabControl>(window,"AiTabs").SelectedIndex=0; await Task.Delay(100);
        vm.Instruction="Добавь параметр для имени компьютера";
        Check(vm.EffectiveTask.Id=="create" && vm.IntentLabel=="Доработать черновик","follow-up after generation refines the unsaved draft");
        runtime.Response=JsonSerializer.Serialize(new AiResult("Добавлен параметр.","[CmdletBinding()]\nparam([string]$Format = 'yyyy-MM-dd',[string]$ComputerName = $env:COMPUTERNAME)\n\"${ComputerName}: $(Get-Date -Format $Format)\"","Текущая дата","Выводит дату и имя компьютера.","",["дата"]));
        await vm.GenerateAsync();
        Check(runtime.LastRequest!.Messages.Last().Content.Contains("Текущий черновик") && vm.NewScript!.Code.Contains("ComputerName"),"refinement passes the previous draft and replaces it");
        var before = library.Entries.Count;
        var created = await vm.SaveNewScriptAsync(vm.NewScript!,"Текущая дата","Windows","Выводит дату",["дата"]);
        await WaitUntil(()=>library.Entries.Count>before,TimeSpan.FromSeconds(5));
        Check(created is not null && ScriptRepository.ReadCode(created.FilePath).Contains("ComputerName") && !vm.HasNewScript && library.Entries.Any(e=>e.Id==created.Id),"saving the draft creates a library script without running it");

        // Работа с прикреплённым скриптом.
        vm.NewConversation(); vm.SelectedEntry = library.Entries.First(e=>e.Id==entry.Id); await vm.Preparation; await Task.Delay(250); SaveFrame(window,"ai-ready");
        Click(Named<Button>(window,"AiExample_1")); await Task.Delay(50);
        Check(vm.Instruction.Length>0 && vm.EffectiveTask.Id=="audit" && !vm.IsBusy,"example fills the prompt and auto-detects the task without starting inference");
        Click(Named<Button>(window,"AiContext")); await WaitUntil(()=>window.OwnedWindows.Any(),TimeSpan.FromSeconds(3)); var dialog = window.OwnedWindows.Single();
        await WaitUntil(()=>vm.Scopes.Count>1 && !vm.IsPreparing,TimeSpan.FromSeconds(10)); Named<ComboBox>(dialog,"AiContextScope").SelectedItem=vm.Scopes.Last(); await Task.Delay(100); SaveFrame(dialog,"ai-context"); dialog.Close();
        Check(vm.SelectedScope==vm.Scopes.Last() && repository.ReadForReview(entry).ContentHash==hash,"context dialog selects an AST function without modifying source");
        runtime.Response=JsonSerializer.Serialize(new AiResult("### Назначение\n**Get-Date** возвращает дату.\n\n```powershell\nGet-Date -Format 'yyyy-MM-dd'\n```","","","","",[]));
        vm.SelectedTask=vm.Tasks.First(t=>t.Id=="ask"); Check(!vm.AutoDetect,"manual task choice overrides auto-detection"); vm.Instruction="Первый вопрос"; var generation=vm.GenerateAsync();
        await WaitUntil(()=>vm.HasTurns,TimeSpan.FromSeconds(2));
        Check(vm.Instruction.Length==0 && Named<Button>(window,"AiCancel").IsEffectivelyVisible && !Named<Button>(window,"AiContext").IsEnabled,"accepted request clears composer and exposes stop while context is locked");
        await generation; Check(vm.AutoDetect,"manual choice applies to one message, then auto-detection resumes");
        var original=vm.Turns[0].Answer; vm.Instruction="Уточняющий вопрос"; await vm.GenerateAsync();
        Check(vm.Turns.Count==2 && vm.Turns[0].Answer==original && runtime.LastRequest!.Messages.Any(m=>m.Role=="user" && m.Content=="Первый вопрос"),"follow-up keeps both visible replies and supplies prior dialogue to inference");
        await Task.Delay(250); SaveFrame(window,"ai-dialogue-fixture");
        runtime.Hold=true; vm.Instruction="Остановить этот ответ"; generation=vm.GenerateAsync(); await WaitUntil(()=>vm.Turns.Count==3 && vm.Answer.Length>0,TimeSpan.FromSeconds(2));
        Click(Named<Button>(window,"AiCancel")); await generation; runtime.Hold=false;
        Check(!vm.IsBusy && !vm.HasProposal && vm.Turns.Last().Finished && vm.Turns.Last().Completion.Contains("Остановлено"),"stop preserves a clearly marked partial reply and creates no applicable change");
        Click(Named<Button>(window,"AiNewConversation"));
        Check(!vm.HasTurns && vm.Answer.Length==0 && vm.Instruction.Length==0 && vm.SelectedEntry?.Id==entry.Id,"new dialogue clears transcript and retains selected script");
        vm.Instruction="Добавь комментарии"; Check(vm.EffectiveTask.Id=="comments","edit request on an attached script is detected as a code change");
        runtime.Response=JsonSerializer.Serialize(new AiResult("Добавлен комментарий.","function Get-EnvironmentReport {\n    # Текущая дата\n    Get-Date\n}","","","",[])); await vm.GenerateAsync();
        Check(vm.HasProposal && repository.ReadForReview(entry).ContentHash==hash,"code proposal is shown without writing the file");
        Click(Named<Button>(window,"AiReviewProposal")); await Task.Delay(120);
        Check(Named<TabControl>(window,"AiTabs").SelectedIndex==1 && !Named<Border>(window,"AiComposer").IsEffectivelyVisible && Named<Button>(window,"AiApply").IsEffectivelyVisible,"proposal action opens dedicated review and gives code the full workspace");
        window.Width=960; window.Height=640; await Task.Delay(160); var apply=Named<Button>(window,"AiApply"); SaveFrame(window,"ai-diff-960");
        var point=apply.TranslatePoint(new Point(apply.Bounds.Width,apply.Bounds.Height),window)!.Value;
        Check(point.X<=window.Bounds.Width && point.Y<=window.Bounds.Height && apply.Bounds.Width>100 && apply.IsEnabled,"dedicated review keeps save control within the minimum window");
        var validation=Named<TextBlock>(window,"AiReviewValidation");
        Check(validation.TranslatePoint(new Point(0,validation.Bounds.Height),window) is { } validationEnd && apply.TranslatePoint(default,window) is { } applyStart && validationEnd.Y<=applyStart.Y,"review validation wraps above save actions without overlap at minimum size");
        Click(apply); await WaitUntil(()=>window.OwnedWindows.Any(),TimeSpan.FromSeconds(3));
        Check(repository.ReadForReview(entry).ContentHash==hash,"saving a proposal asks for confirmation before writing"); window.OwnedWindows.Single().Close(); await Task.Delay(100);
        vm.SelectedScope=vm.Scopes.First(); await Task.Delay(100);
        Check(!vm.HasProposal && Named<TabControl>(window,"AiTabs").SelectedIndex==0 && repository.ReadForReview(entry).ContentHash==hash,"changing context invalidates the proposal and returns to dialogue without writing");
        // Разбор ошибки с исправлением: сначала вопрос «Применить?», затем Diff и вопрос «Сохранить?».
        window.Width=1370; window.Height=880; vm.NewConversation();
        runtime.Response=JsonSerializer.Serialize(new AiResult("Ошибка в строке 2. Исправленный фрагмент:\n\n```powershell\nGet-Date -Format 'yyyy-MM-dd'\n```","","","","",[]));
        vm.SelectedTask=vm.Tasks.First(t=>t.Id=="error"); vm.Instruction="Get-EnvironmentReport : The term '$if' is not recognized"; await vm.GenerateAsync(); await Task.Delay(150);
        Check(vm.HasSuggestedFix && !vm.HasProposal && Named<Border>(window,"AiFixCard").IsEffectivelyVisible && repository.ReadForReview(entry).ContentHash==hash,"an answer with a fix asks whether to apply it to the script without writing");
        SaveFrame(window,"ai-fix-question");
        runtime.Response=JsonSerializer.Serialize(new AiResult("Исправление внесено.","function Get-EnvironmentReport {\n    Get-Date -Format 'yyyy-MM-dd'\n}\nGet-EnvironmentReport\n","","","",[]));
        Click(Named<Button>(window,"AiApplyFix")); await WaitUntil(()=>!vm.IsBusy && vm.HasProposal,TimeSpan.FromSeconds(5)); await Task.Delay(150);
        Check(runtime.LastRequest!.Messages.Last().Content.Contains("Внеси в код исправление") && !vm.HasSuggestedFix && Named<Border>(window,"AiProposalCard").IsEffectivelyVisible && Named<Button>(window,"AiQuickSave").IsEnabled,"applying the fix produces a diff and an explicit save question");
        SaveFrame(window,"ai-save-question");
        Click(Named<Button>(window,"AiDiscardProposal")); await Task.Delay(100);
        Check(!vm.HasProposal && repository.ReadForReview(entry).ContentHash==hash,"declining the save leaves the file unchanged");
        // Ошибка при прикреплённом скрипте: SC AI сразу правит код, свой ответ с ошибкой Parser исправляет сам и спрашивает о сохранении.
        vm.NewConversation(); vm.Instruction="Скрипт выдаёт ошибку, исправь"; Check(vm.EffectiveTask.Id=="fix","an error report on an attached script is detected as a code fix");
        runtime.Responses.Enqueue(JsonSerializer.Serialize(new AiResult("Исправлен формат даты.","function Get-EnvironmentReport {\n    Get-Date -Format 'yyyy-MM-dd'\n\nGet-EnvironmentReport\n","","","",[])));
        runtime.Responses.Enqueue(JsonSerializer.Serialize(new AiResult("Добавлена закрывающая скобка функции.","function Get-EnvironmentReport {\n    Get-Date -Format 'yyyy-MM-dd'\n}\nGet-EnvironmentReport\n","","","",[])));
        var requests=runtime.Requests; await vm.GenerateAsync(); await Task.Delay(150);
        Check(runtime.Requests==requests+2 && runtime.LastRequest!.Messages.Last().Content.Contains("не проходит проверку PowerShell Parser") && vm.CanApply && vm.Answer.Contains("Проверка синтаксиса") && vm.Status.Contains("Сохранить изменения")
            && Named<Border>(window,"AiProposalCard").IsEffectivelyVisible && repository.ReadForReview(entry).ContentHash==hash,"fix repairs its own Parser errors, then offers to save without writing");
        SaveFrame(window,"ai-fix-repaired");
        Click(Named<Button>(window,"AiDiscardProposal")); await Task.Delay(100);
        window.Width=960; window.Height=640; vm.NewConversation(); vm.SelectedTask=vm.Tasks.First(t=>t.Id=="error"); vm.ErrorText="Get-EnvironmentReport : Access is denied"; await Task.Delay(100); SaveFrame(window,"ai-error-960");
        var send=Named<Button>(window,"AiGenerate"); point=send.TranslatePoint(new Point(send.Bounds.Width,send.Bounds.Height),window)!.Value;
        Check(point.X<=window.Bounds.Width && point.Y<=window.Bounds.Height && vm.HasErrorText,"attached run error and send action fit together at minimum size");
        Application.Current!.RequestedThemeVariant=ThemeVariant.Light; await Task.Delay(100); SaveFrame(window,"ai-error-light-960");
        window.SetRenderScaling(1.5); await Task.Delay(100); SaveFrame(window,"ai-150pct"); window.SetRenderScaling(1);
        Application.Current.RequestedThemeVariant=ThemeVariant.Dark;
        var settingsTask = AiSettingsWindow.ShowAsync(window,vm,store); await Task.Delay(250); var settings = window.OwnedWindows.Single(); SaveFrame(settings,"ai-settings-builtin"); settings.Height=1900; foreach (var expander in settings.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded=true; await Task.Delay(250); SaveFrame(settings,"ai-settings-builtin-full"); settings.Height=800;
        Check(Named<Slider>(settings,"AiProfile").IsEffectivelyVisible && !settings.GetVisualDescendants().OfType<NumericUpDown>().Any(),"performance is configured with hardware-bounded sliders instead of numbers");
        Click(Named<Button>(settings,"AiSourceExternal")); await Task.Delay(150); SaveFrame(settings,"ai-settings-external");
        Check(Named<TextBox>(settings,"AiEndpoint").IsEffectivelyVisible && Named<Button>(settings,"AiDiscoverModels").IsEffectivelyVisible,"own local AI server can be configured");
        settings.Close(); await settingsTask;
        await WaitUntil(()=>!services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(90)); window.Close();
    }

    private static async Task AiLiveAsync()
    {
        var workspace = Path.GetFullPath(Path.Combine(_root,"..",".."));
        var options = new AiRuntimeOptions { RuntimePath=Path.Combine(workspace,"AI","Runtime","llama-server.exe"),ModelPath=Path.Combine(workspace,"AI","Models",AiCatalog.Qwen4B.FileName),Threads=6,ContextTokens=4096,MaximumOutputTokens=1024 };
        options.Validate(); var gguf = await Task.Run(() => GgufMetadata.Read(options.ModelPath)); Check(gguf.Architecture=="qwen35","real downloaded GGUF declares qwen35 architecture");
        var root=Fixture("live"); var repository=new ScriptRepository(root); var entry=repository.Create("Отчёт о дате","Windows","","",[],false,"Draft");
        repository.WriteCode(entry,"param([string]$Format = 'yyyy-MM-dd')\nfunction Get-ReportDate {\n    param([string]$Pattern)\n    Get-Date -Format $Pattern\n}\nGet-ReportDate -Pattern $Format\n");
        var service=new ScriptAiService(repository,new()); var context=await service.ContextAsync(entry);
        using var runtime=new LlamaCppRuntime(options); Check(!runtime.State.Loaded && runtime.OwnedProcessId is null,"local runtime starts lazy before first real request");
        async Task<AiResult> Generate(string task,string instruction,AiContextChoice scope,int tokens=1024)
        {
            var request=await service.RequestAsync(AiTaskDefinition.All.First(t=>t.Id==task),context,scope,0,0,instruction,"",null,tokens,[],default);
            var buffer=new StringBuilder(); var chunks=0; var truncated=false; var watch=Stopwatch.StartNew();
            await foreach(var delta in runtime.GenerateAsync(request)) { buffer.Append(delta.Text); if(delta.Text.Length>0) chunks++; truncated |= delta.Truncated; }
            File.WriteAllText(Path.Combine(_root,"live-"+task+".json"),buffer.ToString());
            Check(chunks>1 && !truncated,"real Qwen3.5 streamed complete "+task+" answer");
            Results.Add("OBSERVATION Qwen3.5 4B CPU "+task+": "+watch.Elapsed.TotalSeconds.ToString("F2")+"s; stream chunks "+chunks);
            return ScriptAiService.Parse(buffer.ToString());
        }
        var explanation=await Generate("explain","Объясни в двух коротких предложениях.",context.Choices[0]);
        var created=await Generate("create","Напиши скрипт, который выводит 5 самых больших файлов в указанной папке.",context.Choices[0],2048);
        var draft=await service.DraftAsync(created,default);
        Check(draft.Code.Contains("param",StringComparison.OrdinalIgnoreCase) && draft.Title.Length>0 && !draft.Code.Contains("Get-ReportDate"),"real local model writes a new standalone script with a title");
        Results.Add("OBSERVATION new script: "+draft.Title+"; Parser valid: "+draft.SyntaxValid+"; lines: "+draft.Code.Split((char)10).Length);
        Check(explanation.Answer.Any(c=>c is >= 'А' and <= 'я') && explanation.Answer.Length>20,"real local model answers in Russian");
        var tags=await Generate("tags","Назови кратко и дай 3 тега.",context.Choices[0]);
        var metadata=await service.ProposeAsync(AiTaskDefinition.All.First(t=>t.Id=="tags"),context,context.Choices[0],0,0,tags,default);
        Check(metadata is { Metadata:not null,Code:null },"real model metadata becomes a reviewable proposal");
        var code=await Generate("comments","Добавь один полезный русский комментарий, не меняя код.",context.Choices.Last());
        var proposal=await service.ProposeAsync(AiTaskDefinition.All.First(t=>t.Id=="comments"),context,context.Choices.Last(),0,0,code,default);
        Check(proposal is { SyntaxValid:true,Code:not null } && repository.ReadForReview(entry).ContentHash==context.Snapshot.ContentHash,"real model code passes Parser and remains unapplied");
        var pid=runtime.OwnedProcessId;
        using(var native=Process.GetProcessById(pid!.Value)) Results.Add("OBSERVATION Qwen3.5 4B CPU working set after generation: "+(native.WorkingSet64/1024d/1024d).ToString("F1")+" MiB");
        using var cancel=new CancellationTokenSource(); var cancelled=false; var received=false;
        try { await foreach(var delta in runtime.GenerateAsync(new([new("user","Перечисли 100 советов по PowerShell с пояснениями.")],1024),cancel.Token)) { if(delta.Text.Length>0) { received=true; cancel.Cancel(); } } } catch(OperationCanceledException) { cancelled=true; }
        var exited=false; try { using var process=Process.GetProcessById(pid!.Value); exited=process.HasExited; } catch(ArgumentException) { exited=true; }
        Check(received && cancelled && !runtime.State.Loaded && exited,"cancelling live generation stops native CPU process and frees model");
        // The real result is shown through the production view model and review UI, with no fake chat transcript.
        using var services=Bootstrap.CreateServices(root,_worker,installBundledScripts:false); var window=services.GetRequiredService<MainWindow>(); window.Width=1370; window.Height=880; window.Show();
        await WaitUntil(()=>window.Library.Entries.Count>0,TimeSpan.FromSeconds(10)); var vm=services.GetRequiredService<AiAssistantViewModel>();
        vm.SelectedEntry=window.Library.Entries.First(); await vm.Preparation; vm.SelectedTask=vm.Tasks.First(t=>t.Id=="comments"); vm.SelectedScope=vm.Scopes.Last(); vm.Instruction="Добавь один русский комментарий к функции. Не меняй её поведение.";
        window.Navigate("ai"); await Task.Delay(200); SaveFrame(window,"ai-live-ready");
        var liveGeneration=vm.GenerateAsync(); await WaitUntil(()=>vm.Answer.Length>8 || liveGeneration.IsCompleted,TimeSpan.FromSeconds(90)); SaveFrame(window,"ai-live-streaming"); await liveGeneration; await Task.Delay(250); SaveFrame(window,"ai-live-proposal");
        Check(vm.Turns.Count==1 && vm.Turns[0].Answer==vm.Answer && vm.Instruction.Length==0,"real streamed answer becomes a complete selectable dialogue turn");
        Check(vm.HasProposal && vm.CanApply,"production AI view model exposes a Parser checked real model proposal");
        Check(Named<Button>(window,"AiApply").IsEnabled,"real AI proposal can be saved after confirmation");
        Named<TabControl>(window,"AiTabs").SelectedIndex=1; await Task.Delay(100); SaveFrame(window,"ai-live-diff");
        var reviewedProposal=vm.Proposal; window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.Enter,KeyModifiers=Avalonia.Input.KeyModifiers.Control });
        Check(ReferenceEquals(vm.Proposal,reviewedProposal) && !vm.IsBusy,"Ctrl+Enter does not replace a proposal while reviewing changes");
        window.Width=960; window.Height=640; await Task.Delay(180); SaveFrame(window,"ai-live-diff-960");
        var apply=Named<Button>(window,"AiApply"); var end=apply.TranslatePoint(new Point(apply.Bounds.Width,apply.Bounds.Height),window)!.Value;
        Check(end.X<=window.Bounds.Width && end.Y<=window.Bounds.Height,"review and apply controls fit the minimum window with a real proposal");
        Application.Current!.RequestedThemeVariant=ThemeVariant.Light; await Task.Delay(100); SaveFrame(window,"ai-live-diff-960-light"); Application.Current.RequestedThemeVariant=ThemeVariant.Dark;
        var beforeReview=repository.ReadForReview(entry).ContentHash; var revisionCount=repository.GetVersions(entry).Count;
        Click(apply); await WaitUntil(()=>window.OwnedWindows.Any(),TimeSpan.FromSeconds(3));
        Check(repository.ReadForReview(entry).ContentHash==beforeReview,"review dialog does not write before user confirmation");
        var confirmation=window.OwnedWindows.Single(); Click(confirmation.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Подтвердить"));
        await WaitUntil(()=>!vm.IsBusy && !vm.HasProposal,TimeSpan.FromSeconds(15));
        Check(repository.ReadForReview(entry).ContentHash!=beforeReview && repository.GetVersions(entry).Count>revisionCount && vm.SelectedEntry?.Id==entry.Id,"confirmed UI review creates a new version and preserves current selection");
        await vm.UnloadAsync(); Check(!services.GetRequiredService<IAiRuntime>().State.Loaded,"unload action releases real model after generation");
        var settingsTask=AiSettingsWindow.ShowAsync(window,vm,services.GetRequiredService<AiSettingsStore>()); await Task.Delay(150); var modelWindow=window.OwnedWindows.Single(); SaveFrame(modelWindow,"ai-live-model"); modelWindow.Close(); await settingsTask;
        await WaitUntil(()=>!services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(90)); window.Close();
    }
}
