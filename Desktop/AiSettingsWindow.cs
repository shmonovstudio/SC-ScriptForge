using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using ShmonovStudio.AI;

namespace SC.ScriptForge;

/// <summary>
/// Настройки SC AI: источник модели (встроенная GGUF или свой сервер) и производительность.
/// Производительность задаётся ползунками в пределах возможностей ПК, а не числами.
/// </summary>
public static class AiSettingsWindow
{
    private sealed record Preset(string Name, string Endpoint, string Hint);
    private static readonly Preset[] Presets =
    [
        new("Ollama","http://127.0.0.1:11434/v1","ollama pull qwen2.5-coder:7b"),
        new("LM Studio","http://127.0.0.1:1234/v1","Developer → Start Server"),
        new("llama.cpp","http://127.0.0.1:8080/v1","llama-server -m model.gguf"),
        new("Jan","http://127.0.0.1:1337/v1","Settings → Local API Server")
    ];

    public static async Task ShowAsync(Window owner,AiAssistantViewModel model,AiSettingsStore store)
    {
        if (model.IsBusy) { await Dialogs.MessageAsync(owner,"ИИ занят","Остановите генерацию перед изменением модели."); return; }
        var window = Dialogs.Window("Модель SC AI",840,800); window.MinWidth = 600; window.DataContext = model;
        var options = model.Options; var hardware = AiAssistantViewModel.Hardware;
        var external = options.IsExternal; var updating = false;
        var status = Ui.Text("Выберите модель и папку, затем нажмите «Скачать и подключить».","muted",12);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false };
        CancellationTokenSource? installation = null; var closeRequested = false; Action modelChanged = () => { };
        Func<AiRuntimeOptions> readOptions = () => options;
        Action<bool> lockPerformance = _ => { };
        status.Name = "AiInstallStatus"; progress.Name = "AiInstallProgress";

        // ── Текущее состояние ──────────────────────────────────────────────
        var currentName = Ui.Bound(nameof(model.ModelLabel),"section"); currentName.FontSize = 15;
        var currentState = Ui.Bound(nameof(model.RuntimeLabel),"muted"); currentState.FontSize = 11;
        var unload = Ui.Button("Освободить память",model.UnloadAsync,"ghost"); unload.Bind(Control.IsEnabledProperty,new Binding(nameof(model.IsModelLoaded)));
        var currentText = Ui.Stack(Ui.Text("Текущая модель","muted",12),currentName,currentState); currentText.Spacing = 6;
        var currentContent = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14 };
        currentContent.Children.Add(Ui.IconBadge("brain",18)); currentContent.Children.Add(currentText.Place(0,1)); currentContent.Children.Add(unload.Place(0,2));
        var currentCard = Ui.Card(currentContent);
        currentCard.Bind(Control.IsVisibleProperty,new Binding(nameof(model.HasModel)));

        // ── Источник ───────────────────────────────────────────────────────
        Button SourceCard(string icon,string title,string detail)
        {
            var content = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
            var text = Ui.Stack(Ui.Text(title,"section",14),Ui.Text(detail,"muted",12)); text.Spacing = 4;
            content.Children.Add(Ui.IconBadge(icon,17)); content.Children.Add(text.Place(0,1));
            var button = Ui.Action(title,() => { },"sourceCard"); button.Content = content; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; return button;
        }
        var builtinCard = SourceCard("cpu","Локальный ИИ","Скачайте модель здесь. После установки работает без интернета, на CPU или видеокарте."); builtinCard.Name = "AiSourceBuiltin";
        var externalCard = SourceCard("server","Своя ИИ на сервере","Ollama, LM Studio, llama.cpp server, Jan — подключите модель мощнее."); externalCard.Name = "AiSourceExternal";
        var sources = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 }; sources.Children.Add(builtinCard); sources.Children.Add(externalCard.Place(0,1));

        // ── Встроенная модель ──────────────────────────────────────────────
        var path = Ui.Input(options.ModelPath,"Путь к файлу .gguf"); path.Name = "AiModelPath";
        var runtime = Ui.Input(options.RuntimePath,"Путь к llama-server.exe");
        var modelInfo = Ui.Text("","muted",11);
        long ModelBytes() { try { return File.Exists(path.Text) ? new FileInfo(path.Text!).Length : AiHardwareProfile.DefaultModelBytes; } catch (IOException) { return AiHardwareProfile.DefaultModelBytes; } }
        async Task DescribeModel()
        {
            var file = path.Text?.Trim() ?? "";
            if (!File.Exists(file)) { modelInfo.Text = "Файл модели не выбран. Скачайте Qwen ниже или выберите свой GGUF."; return; }
            try
            {
                var metadata = await Task.Run(() => GgufMetadata.Read(file));
                var fits = hardware.CanRun(metadata.Bytes);
                modelInfo.Text = $"{metadata.Name} · {metadata.Architecture} · {AiHardwareProfile.FormatBytes(metadata.Bytes)}"
                    + (metadata.Verified ? " · проверенная архитектура" : " · архитектура не проверялась, но llama.cpp её может поддерживать")
                    + (fits ? "" : " · ⚠ может не хватить памяти этого ПК");
            }
            catch (Exception e) { modelInfo.Text = "⚠ " + e.Message; }
        }
        Grid PathRow(TextBox input,Button button) { var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 }; row.Children.Add(input); row.Children.Add(button.Place(0,1)); return row; }
        var chooseModel = Ui.Button("Выбрать…",async () =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new() { Title = "Модель в формате GGUF", FileTypeFilter = [new("GGUF") { Patterns = ["*.gguf"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } selected) return;
            path.Text = selected; await DescribeModel(); modelChanged();
        });
        var chooseRuntime = Ui.Button("Выбрать…",async () =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new() { Title = "llama-server.exe с DLL той же сборки", FileTypeFilter = [new("llama.cpp") { Patterns = ["llama-server.exe"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } selected) runtime.Text = selected;
        });
        var folder = Ui.Input(store.InstallationFolder); folder.Name = "AiInstallFolder";
        var chooseFolder = Ui.Button("Выбрать…",async () => { var folders = await window.StorageProvider.OpenFolderPickerAsync(new() { Title = "Папка для движка и моделей" }); if (folders.FirstOrDefault()?.TryGetLocalPath() is { } selected) folder.Text = selected; });
        var modelChoice = new ComboBox { Name = "AiInstallModel", ItemsSource = AiCatalog.Models.Select(m => m.Name).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        modelChoice.SelectedIndex = options.ModelPath.Contains("9B",StringComparison.OrdinalIgnoreCase) ? 2 : options.ModelPath.Contains("2B",StringComparison.OrdinalIgnoreCase) || hardware.MemoryGigabytes < 12 ? 0 : 1;
        var compute = new ComboBox { Name = "AiComputeBackend", ItemsSource = new[] { "Процессор · CPU", "Видеокарта · GPU (Vulkan)" }, SelectedIndex = (int)options.ComputeBackend, HorizontalAlignment = HorizontalAlignment.Stretch };
        var choiceDescription = Ui.Text("","soft",12); var requirements = Ui.Text("","muted",11); var computeHint = Ui.Text("","muted",11);
        void DescribeChoice()
        {
            var selected = AiCatalog.Models[Math.Clamp(modelChoice.SelectedIndex,0,AiCatalog.Models.Count - 1)];
            choiceDescription.Text = selected.Description;
            requirements.Text = $"Модель: {AiHardwareProfile.FormatBytes(selected.Bytes)} · рекомендуется ОЗУ от {selected.RecommendedRamGb} ГБ"
                + (compute.SelectedIndex == 1 ? $" и видеопамять от {selected.RecommendedVramGb} ГБ." : ". Видеокарта не требуется.")
                + (hardware.MemoryGigabytes > 0 && hardware.MemoryGigabytes < selected.RecommendedRamGb - 0.5 ? " У этого ПК меньше рекомендуемой ОЗУ — лучше выбрать модель легче." : "")
                + " Требования ориентировочные, для диалога 4–8 тыс. токенов.";
            computeHint.Text = compute.SelectedIndex == 1
                ? "NVIDIA / AMD / Intel с поддержкой Vulkan и актуальным драйвером. Движок скачивается автоматически; используется первая Vulkan-видеокарта. При нехватке видеопамяти выберите модель легче или CPU."
                : "Windows 10/11 x64, современный процессор. Для слабого ПК начните с 2B; для 9B рекомендуется GPU.";
        }
        modelChoice.SelectionChanged += (_,_) => DescribeChoice(); compute.SelectionChanged += (_,_) => DescribeChoice(); DescribeChoice();
        var install = new Button(); Button? save = null;
        var cancelInstall = Ui.Action("Остановить · сохранить для докачивания",() => installation?.Cancel(),"ghost"); cancelInstall.IsVisible = false; cancelInstall.Name = "AiCancelInstall";
        async Task Install()
        {
            if (installation is not null) return;
            var selected = AiCatalog.Models[Math.Clamp(modelChoice.SelectedIndex,0,AiCatalog.Models.Count - 1)];
            var backend = compute.SelectedIndex == 1 ? AiComputeBackend.Vulkan : AiComputeBackend.Cpu;
            installation = new(); var cancellation = installation; progress.IsVisible = true; progress.Value = 0; cancelInstall.IsVisible = true;
            var filesReady = false;
            var locked = new Control[] { chooseModel,chooseRuntime,chooseFolder,save!,install,modelChoice,compute,runtime,path,folder,builtinCard,externalCard,unload };
            foreach (var control in locked) control.IsEnabled = false;
            lockPerformance(true);
            try
            {
                store.RememberInstallationFolder(folder.Text ?? "");
                await model.UnloadAsync();
                status.Text = "Готовим установку…";
                var updates = new Progress<AiInstallProgress>(value =>
                {
                    if (filesReady || installation != cancellation) return;
                    status.Text = value.Stage + $" · {value.Percent:0}%" + (value.TotalBytes > 0 ? $" · {AiHardwareProfile.FormatBytes(value.DownloadedBytes)} из {AiHardwareProfile.FormatBytes(value.TotalBytes)}" : "");
                    progress.Value = value.Percent;
                });
                var result = await new AiInstaller().InstallAsync(folder.Text ?? store.InstallationFolder,selected,backend,updates,cancellation.Token);
                filesReady = true;
                runtime.Text = result.RuntimePath; path.Text = result.ModelPath; await DescribeModel(); modelChanged();
                status.Text = "Проверяем запуск модели · " + result.ComputeLabel + "… Первый запуск может занять несколько минут.";
                progress.IsIndeterminate = true;
                var ready = readOptions() with { Provider = AiRuntimeOptions.BuiltinProvider, InstallationDirectory = result.InstallationDirectory };
                await model.ActivateInstalledAsync(ready,cancellation.Token);
                options = ready;
                status.Text = "Готово · " + selected.Name + " · " + ready.ComputeLabel + ". Модель подключена, настройки сохранены. Закройте окно и задайте вопрос.";
            }
            catch (OperationCanceledException) { status.Text = "Остановлено. Скачанные данные сохранены. Выберите ту же модель и папку и нажмите «Скачать и подключить», чтобы продолжить."; }
            catch (Exception e) { status.Text = e.Message + (filesReady ? " Файлы сохранены. Настройки прежней модели оставлены; выберите CPU или модель легче и повторите подключение." : " После обрыва повторите скачивание с той же моделью и папкой — сохранённая часть будет использована."); }
            finally
            {
                installation = null; cancellation.Dispose(); progress.IsVisible = false; progress.IsIndeterminate = false; cancelInstall.IsVisible = false;
                foreach (var control in locked) control.IsEnabled = true;
                unload.ClearValue(Control.IsEnabledProperty); unload.Bind(Control.IsEnabledProperty,new Binding(nameof(model.IsModelLoaded)));
                lockPerformance(false);
                if (closeRequested) window.Close();
            }
        }
        install = Ui.Button("Скачать и подключить",Install,"primary"); install.Name = "AiDownloadInstall";
        var installBody = Ui.Stack(Ui.Field("Модель · три варианта",modelChoice),choiceDescription,requirements,Ui.Field("Где выполнять вычисления",compute),computeHint,Ui.Field("Куда скачать движок и модель",PathRow(folder,chooseFolder)),
            Ui.Text("Скачивание с GitHub и Hugging Face. Размер и SHA256 проверяются. После обрыва или остановки загрузка продолжается с сохранённого места. Готовые файлы используются повторно.","muted",11)); installBody.Margin = new(16);
        var installer = new Expander { Header = "Скачать и подключить локальный ИИ", Content = installBody, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var engineBody = Ui.Stack(Ui.Field("llama-server.exe",PathRow(runtime,chooseRuntime)),Ui.Text("Обычно менять не нужно — движок ставится вместе с моделью.","muted",11)); engineBody.Margin = new(16);
        var engine = new Expander { Header = "Движок llama.cpp", Content = engineBody, HorizontalAlignment = HorizontalAlignment.Stretch };
        var manualBody = Ui.Stack(Ui.Field("Файл модели (GGUF)",PathRow(path,chooseModel)),modelInfo,engine); manualBody.Margin = new(16);
        var manual = new Expander { Header = "Своя модель GGUF и движок", Content = manualBody, HorizontalAlignment = HorizontalAlignment.Stretch };
        var builtinPanel = Ui.Stack(installer,manual); builtinPanel.Spacing = 12;

        // ── Внешний сервер ─────────────────────────────────────────────────
        var endpoint = Ui.Input(options.Endpoint,"http://127.0.0.1:11434/v1"); endpoint.Name = "AiEndpoint";
        var externalModel = Ui.Input(options.ExternalModel,"Например: qwen2.5-coder:14b"); externalModel.Name = "AiExternalModel";
        var apiKey = Ui.Input(options.ApiKey,"Не требуется для Ollama и LM Studio"); apiKey.PasswordChar = '•';
        var presetHint = Ui.Text("","muted",11); presetHint.IsVisible = false;
        var presets = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var preset in Presets)
        {
            var button = Ui.Action(preset.Name,() => { endpoint.Text = preset.Endpoint; presetHint.Text = "Запустите " + preset.Name + ": " + preset.Hint; presetHint.IsVisible = true; },"chip"); button.Margin = new(0,0,6,6); presets.Children.Add(button);
        }
        var models = new ListBox { MaxHeight = 160, IsVisible = false, Classes = { "catalog" } };
        models.SelectionChanged += (_,_) => { if (models.SelectedItem is string selected) externalModel.Text = selected; };
        var remoteWarning = Ui.Text("","muted",11);
        void CheckRemote() => remoteWarning.Text = OpenAiCompatibleRuntime.IsLocal(endpoint.Text ?? "") ? "🔒 Локальный адрес — код скриптов не покидает вашу сеть." : "⚠ Адрес не локальный: код и вопросы будут отправляться на этот сервер.";
        endpoint.TextChanged += (_,_) => CheckRemote(); CheckRemote();
        var discover = Ui.Button("Найти модели",async () =>
        {
            status.Text = "Подключаемся к серверу…";
            try
            {
                var found = await OpenAiCompatibleRuntime.ListModelsAsync(endpoint.Text ?? "",apiKey.Text ?? "");
                models.ItemsSource = found; models.IsVisible = found.Count > 0;
                if (found.Count > 0 && string.IsNullOrWhiteSpace(externalModel.Text)) externalModel.Text = found[0];
                status.Text = found.Count > 0 ? $"Сервер доступен · моделей: {found.Count}. Выберите модель из списка." : "Сервер доступен, но моделей нет. Загрузите модель в Ollama / LM Studio.";
            }
            catch (Exception e) { models.IsVisible = false; status.Text = e.Message; }
        });
        discover.Content = Ui.Row(Ui.Icon("refresh",14),Ui.Text("Найти модели")); discover.Name = "AiDiscoverModels";
        var externalPanel = Ui.Stack(Ui.Text("Готовые адреса","muted",11),presets,presetHint,Ui.Field("Адрес сервера (OpenAI API)",endpoint),remoteWarning,
            Ui.Field("Модель",PathRow(externalModel,discover)),models,Ui.Field("Ключ доступа",apiKey),
            Ui.Text("SC AI не устанавливает и не запускает сервер — модель и её память под управлением выбранной программы.","muted",11)); externalPanel.Spacing = 10;

        // ── Производительность ─────────────────────────────────────────────
        var machine = Ui.Text($"Ваш ПК: {hardware.LogicalCores} потоков CPU · {hardware.MemoryGigabytes:0.#} ГБ оперативной памяти","soft",12);
        Slider StepSlider(int maximum,string name) => new() { Name = name, Minimum = 0, Maximum = Math.Max(1,maximum), TickFrequency = 1, IsSnapToTickEnabled = true, TickPlacement = TickPlacement.BottomRight, HorizontalAlignment = HorizontalAlignment.Stretch };
        Control Scale(Slider slider,string minimum,string maximum,TextBlock value,string title,string icon)
        {
            var ends = new Grid { ColumnDefinitions = new("*,*") }; var left = Ui.Text(minimum,"muted",10); var right = Ui.Text(maximum,"muted",10); right.HorizontalAlignment = HorizontalAlignment.Right; ends.Children.Add(left); ends.Children.Add(right.Place(0,1));
            var head = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 }; head.Children.Add(Ui.Icon(icon,15)); head.Children.Add(Ui.Text(title,"section",13).Place(0,1)); head.Children.Add(value.Place(0,2));
            value.HorizontalAlignment = HorizontalAlignment.Right; value.TextAlignment = TextAlignment.Right;
            var stack = Ui.Stack(head,slider,ends); stack.Spacing = 2; return stack;
        }
        var profile = StepSlider(2,"AiProfile"); var profileValue = Ui.Text("","accent",12); profileValue.FontWeight = FontWeight.Medium;
        var profileDetail = Ui.Text("","muted",11);
        var contextSlider = StepSlider(0,"AiContextSlider"); var contextValue = Ui.Text("","soft",11);
        var threadSlider = new Slider { Name = "AiThreadsSlider", Minimum = 1, Maximum = hardware.MaxThreads, TickFrequency = 1, IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch }; var threadValue = Ui.Text("","soft",11);
        var outputSlider = StepSlider(AiHardwareProfile.OutputSteps.Count - 1,"AiOutputSlider"); var outputValue = Ui.Text("","soft",11);
        IReadOnlyList<int> contextSteps = [];
        int Context() => contextSteps.Count == 0 ? 4096 : contextSteps[Math.Clamp((int)Math.Round(contextSlider.Value),0,contextSteps.Count - 1)];
        int Output() => AiHardwareProfile.OutputSteps[Math.Clamp((int)Math.Round(outputSlider.Value),0,AiHardwareProfile.OutputSteps.Count - 1)];
        void SetContext(int value) => contextSlider.Value = Math.Max(0,contextSteps.Select((s,i) => (s,i)).Where(x => x.s <= value).Select(x => x.i).DefaultIfEmpty(0).Max());
        void SetOutput(int value) => outputSlider.Value = Math.Max(0,AiHardwareProfile.OutputSteps.Select((s,i) => (s,i)).Where(x => x.s <= value).Select(x => x.i).DefaultIfEmpty(0).Max());
        void Describe()
        {
            var bytes = ModelBytes(); var context = Context(); var output = Output(); var threads = (int)threadSlider.Value;
            var memory = AiHardwareProfile.EstimateMemory(bytes,context); var heavy = hardware.MemoryBytes > 0 && memory > hardware.MemoryBytes * 0.7;
            contextValue.Text = $"≈ {context / 12} строк кода · ОЗУ ≈ {AiHardwareProfile.FormatBytes(memory)}" + (heavy ? " ⚠" : "");
            threadValue.Text = $"{threads} из {hardware.MaxThreads}";
            outputValue.Text = external ? $"до ≈ {output / 12} строк" : output > context / 2 ? "больше половины памяти — уменьшится" : $"до ≈ {output / 12} строк";
        }
        void ApplyProfile()
        {
            var level = (AiPerformanceLevel)(int)Math.Round(profile.Value);
            var preset = hardware.Apply(new AiRuntimeOptions(),level,ModelBytes());
            updating = true; SetContext(preset.ContextTokens); threadSlider.Value = preset.Threads; SetOutput(preset.MaximumOutputTokens); updating = false;
            profileValue.Text = level switch { AiPerformanceLevel.Economy => "Экономно", AiPerformanceLevel.Balanced => "Сбалансированно", _ => "Максимум" };
            profileDetail.Text = level switch
            {
                AiPerformanceLevel.Economy => "Меньше памяти и нагрузки на процессор. Подходит для коротких вопросов и небольших скриптов.",
                AiPerformanceLevel.Balanced => "Оптимально для этого ПК: хватает на большие скрипты, компьютер остаётся отзывчивым.",
                _ => "Самые длинные скрипты и ответы. Модель займёт больше памяти, остальные программы могут притормаживать."
            } + (level == hardware.Recommended(ModelBytes()) ? "  ·  Рекомендуется" : "");
            Describe();
        }
        void Recalculate(bool keepProfile)
        {
            var maximum = hardware.MaxContext(ModelBytes()); var current = Context();
            contextSteps = AiHardwareProfile.ContextSteps.Where(s => s <= maximum).ToArray();
            updating = true; contextSlider.Maximum = Math.Max(1,contextSteps.Count - 1); SetContext(contextSteps.Count > 0 && current > 0 ? current : 4096); updating = false;
            if (keepProfile) ApplyProfile(); else Describe();
        }
        profile.ValueChanged += (_,_) => { if (!updating) ApplyProfile(); };
        foreach (var slider in new[] { contextSlider,threadSlider,outputSlider })
            slider.ValueChanged += (_,_) => { if (updating) return; profileValue.Text = "Своя настройка"; profileDetail.Text = "Значения подобраны вручную. Передвиньте общий ползунок, чтобы вернуться к готовому профилю."; Describe(); };
        var profileEnds = new Grid { ColumnDefinitions = new("*,*,*") };
        foreach (var (label,index) in new[] { ("Экономно",0),("Баланс",1),("Максимум",2) })
        {
            var text = Ui.Text(label,"muted",10); text.HorizontalAlignment = index switch { 0 => HorizontalAlignment.Left, 1 => HorizontalAlignment.Center, _ => HorizontalAlignment.Right }; profileEnds.Children.Add(text.Place(0,index));
        }
        var profileHead = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 }; profileHead.Children.Add(Ui.Icon("speed",16)); profileHead.Children.Add(Ui.Text("Режим работы","section",14).Place(0,1)); profileHead.Children.Add(profileValue.Place(0,2));
        var profileBlock = Ui.Stack(profileHead,profile,profileEnds,profileDetail); profileBlock.Spacing = 4;
        var contextScale = Scale(contextSlider,"Короче","Длиннее",contextValue,"Память диалога","memory");
        var threadScale = Scale(threadSlider,"Тише","Быстрее",threadValue,"Ядра процессора","cpu");
        var outputScale = Scale(outputSlider,"Кратко","Подробно",outputValue,"Длина ответа","document");
        var fineBody = Ui.Stack(contextScale,threadScale,outputScale,Ui.Text("Память диалога включает ваш скрипт, вопрос и ответ. Пределы ползунков рассчитаны по объёму ОЗУ и числу ядер этого ПК.","muted",11)); fineBody.Spacing = 16; fineBody.Margin = new(16);
        var fine = new Expander { Header = "Тонкая настройка", Content = fineBody, HorizontalAlignment = HorizontalAlignment.Stretch };
        var performanceBody = Ui.Stack(machine,profileBlock,fine); performanceBody.Spacing = 14;
        var performance = new Border { Child = performanceBody, Padding = new(18), Classes = { "libraryPane" } };
        lockPerformance = value => performance.IsEnabled = !value;

        modelChanged = () => Recalculate(keepProfile: true);
        // Начальные значения: текущие настройки пользователя внутри пределов ПК.
        Recalculate(keepProfile: false);
        updating = true;
        profile.Value = (int)hardware.Recommended(ModelBytes());
        SetContext(options.ContextTokens); threadSlider.Value = Math.Clamp(options.Threads,1,hardware.MaxThreads); SetOutput(options.MaximumOutputTokens);
        updating = false;
        var matches = Enumerable.Range(0,3).FirstOrDefault(i => hardware.Apply(new(),(AiPerformanceLevel)i,ModelBytes()) is var p && p.ContextTokens == Context() && p.Threads == (int)threadSlider.Value && p.MaximumOutputTokens == Output(),-1);
        if (matches >= 0) { updating = true; profile.Value = matches; updating = false; ApplyProfile(); }
        else { profileValue.Text = "Своя настройка"; profileDetail.Text = "Сохранены ваши значения. Передвиньте ползунок, чтобы выбрать готовый режим."; Describe(); }
        _ = DescribeModel();

        // ── Переключение источника ─────────────────────────────────────────
        var sourceBody = new ContentControl();
        void Source(bool value)
        {
            external = value; builtinCard.Classes.Set("active",!value); externalCard.Classes.Set("active",value);
            sourceBody.Content = value ? externalPanel : builtinPanel;
            install.IsVisible = !value;
            contextScale.IsVisible = threadScale.IsVisible = !value; machine.IsVisible = !value;
            profileBlock.IsVisible = !value; fine.IsExpanded = value || fine.IsExpanded; fine.Header = value ? "Длина ответа" : "Тонкая настройка";
            Describe();
        }
        builtinCard.Click += (_,_) => Source(false); externalCard.Click += (_,_) => Source(true); Source(external);

        readOptions = () =>
        {
            var context = Context(); var output = external ? Output() : Math.Min(Output(),AiHardwareProfile.MaxOutput(context));
            return options with
            {
                Provider = external ? AiRuntimeOptions.ExternalProvider : AiRuntimeOptions.BuiltinProvider,
                RuntimePath = runtime.Text?.Trim() ?? "", ModelPath = path.Text?.Trim() ?? "",
                ComputeBackend = compute.SelectedIndex == 1 ? AiComputeBackend.Vulkan : AiComputeBackend.Cpu,
                Endpoint = endpoint.Text?.Trim() ?? "", ExternalModel = externalModel.Text?.Trim() ?? "", ApiKey = apiKey.Text?.Trim() ?? "",
                ContextTokens = context, Threads = (int)threadSlider.Value, MaximumOutputTokens = output
            };
        };
        save = Ui.Action("Сохранить",() =>
        {
            try
            {
                var chosen = readOptions();
                if (chosen != model.Options) model.SaveOptions(chosen);
                window.Close();
            }
            catch (Exception e) { status.Text = e.Message; }
        },"primary");
        save.Name = "AiSettingsSave";

        Control Section(string title,Control content) { var stack = Ui.Stack(Ui.Text(title,"section",15),content); stack.Spacing = 10; return stack; }
        var body = Ui.Stack(Ui.Text("Модель SC AI","heading",26),Ui.Text("Выберите, где работает ИИ, и сколько ресурсов ему можно взять.","muted",12),currentCard,
            Section("Источник",Ui.Stack(sources,sourceBody)),Section("Производительность",performance)); body.Spacing = 18;
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        install.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(install);
        footer.Children.Add(Ui.Row(Ui.Action("Закрыть",() => window.Close(),"ghost"),save).Place(0,1));
        var feedback = Ui.Stack(status,progress,cancelInstall); feedback.Spacing = 6;
        var root = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new(24), RowSpacing = 12 }; root.Children.Add(Ui.Scroll(body)); root.Children.Add(feedback.Place(1,0)); root.Children.Add(footer.Place(2,0)); window.Content = root;
        window.Closing += (_,e) => { if (installation is not null) { e.Cancel = true; closeRequested = true; installation.Cancel(); } };
        await window.ShowDialog(owner);
    }
}
