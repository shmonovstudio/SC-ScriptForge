using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

/// <summary>
/// Экран SC AI: одно поле ввода, задача определяется автоматически по тексту.
/// Ручной выбор задачи спрятан в меню под полем и сбрасывается после отправки.
/// </summary>
public sealed partial class AiAssistantView : UserControl
{
    private static readonly FontFamily Mono = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas");

    public AiAssistantView(Window owner, AiAssistantViewModel model, Func<Task> settings)
    {
        DataContext = model; Name = "AiAssistantPage";
        void Bind(AvaloniaObject control,AvaloniaProperty property,string path) => control.Bind(property,new Binding(path));
        Button IconButton(string label,string icon,Action action)
        {
            var button = Ui.Action(label,action,"ghost"); button.Content = Ui.Icon(icon,16); button.Classes.Add("iconAction"); ToolTip.SetTip(button,label); return button;
        }

        // ── Шапка ──────────────────────────────────────────────────────────
        var reset = Ui.Action("Новый диалог",model.NewConversation,"ghost"); reset.Name = "AiNewConversation"; Bind(reset,IsEnabledProperty,nameof(model.CanGenerate));
        reset.Content = Ui.Row(Ui.Icon("newchat",16),Ui.Text("Новый диалог","muted",12)); reset.Padding = new(10,8);
        var modelTitle = Ui.Bound(nameof(model.ModelDisplayName),"section"); modelTitle.FontSize = 12; modelTitle.MaxWidth = 200; modelTitle.TextWrapping = TextWrapping.NoWrap; modelTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var modelState = Ui.Bound(nameof(model.ModelStateLabel),"muted"); modelState.FontSize = 11;
        var modelButton = Ui.Button("Модель и настройки",settings,"aiModel"); modelButton.Name = "AiModelSettings";
        var modelDot = new Border { Width = 7, Height = 7, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center };
        var modelInfo = Ui.Stack(modelTitle,modelState); modelInfo.Spacing = 2; modelButton.Content = Ui.Row(modelDot,modelInfo,Ui.Icon("sliders",14));
        Bind(modelButton,IsEnabledProperty,nameof(model.CanGenerate)); ToolTip.SetTip(modelButton,"Модель, сервер ИИ и производительность");
        var title = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14, Margin = new(0,0,0,16) };
        var titleBadge = Ui.IconBadge("ai",18); titleBadge.Width = titleBadge.Height = 40; titleBadge.Classes.Add("aiHeroBadge");
        var titleLabel = Ui.Text("ИИ-помощник","heading",32);
        var subtitle = Ui.Text("Помогает писать, объяснять и отлаживать скрипты.","muted",12.5);
        var titleText = Ui.Stack(Ui.Text("ВАШ ПОМОЩНИК В АДМИНИСТРИРОВАНИИ","eyebrow"),titleLabel,subtitle); titleText.Spacing = 8;
        var titleRow = Ui.Row(titleBadge,titleText); titleRow.Spacing = 12; title.Children.Add(titleRow);
        reset.Content = Ui.Icon("newchat",17); reset.Width = 36; title.Children.Add(reset.Place(0,1));

        // ── Поле ввода ─────────────────────────────────────────────────────
        var query = Ui.Input(multiline:true); query.Name = "AiInstruction"; query.MinHeight = 52; query.MaxHeight = 160; query.FontSize = 14; query.Classes.Add("aiPrompt");
        query.Bind(TextBox.TextProperty,new Binding(nameof(model.Instruction)) { Mode = BindingMode.TwoWay }); Bind(query,TextBox.PlaceholderTextProperty,nameof(model.QueryHint)); Bind(query,IsEnabledProperty,nameof(model.CanEditContext));
        // Enter — отправить, Shift+Enter — новая строка.
        query.AddHandler(KeyDownEvent,async (_,e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            e.Handled = true; await model.GenerateAsync();
        },RoutingStrategies.Tunnel);

        var contextIcon = Ui.Icon("attach",14);
        var contextName = Ui.Bound(nameof(model.ContextLabel)); contextName.FontSize = 12; contextName.MaxWidth = 260; contextName.TextWrapping = TextWrapping.NoWrap; contextName.TextTrimming = TextTrimming.CharacterEllipsis;
        var contextButton = Ui.Button("Прикрепить скрипт",() => AiContextWindow.ShowAsync(owner,model),"aiChip"); contextButton.Name = "AiContext";
        contextButton.Content = Ui.Row(contextIcon,contextName); Bind(contextButton,IsEnabledProperty,nameof(model.CanEditContext)); Bind(contextButton,ToolTip.TipProperty,nameof(model.ContextStatus));
        var removeContext = IconButton("Открепить скрипт","close",() => model.SelectedEntry = null); removeContext.Name = "AiRemoveScript"; removeContext.Width = removeContext.Height = 26; Bind(removeContext,IsEnabledProperty,nameof(model.CanEditContext));
        var contextChip = new Border { Child = Ui.Row(contextButton,removeContext), Classes = { "aiChipFrame" } }; ((StackPanel)contextChip.Child).Spacing = 0;
        var errorChipText = Ui.Text("Ошибка из окна запуска","soft",12);
        var removeError = IconButton("Убрать текст ошибки","close",() => model.ErrorText = ""); removeError.Width = removeError.Height = 26;
        var errorIcon = Ui.Icon("bug",14); Ui.Resource(errorIcon,Lucide.Avalonia.LucideIcon.ForegroundProperty,"ScDanger");
        var errorChipRow = Ui.Row(errorIcon,errorChipText,removeError); errorChipRow.Spacing = 6;
        var errorChip = new Border { Child = errorChipRow, Padding = new(10,0,0,0), Classes = { "aiChipFrame" } }; Bind(errorChip,IsVisibleProperty,nameof(model.HasErrorText)); Bind(errorChip,ToolTip.TipProperty,nameof(model.ErrorText));
        var chips = new WrapPanel { Orientation = Orientation.Horizontal }; contextChip.Margin = errorChip.Margin = new(0,0,6,4); chips.Children.Add(contextChip); chips.Children.Add(errorChip);

        var intentIcon = new ContentControl();
        var intentPrefix = Ui.Text("Авто","accent",11); intentPrefix.FontWeight = FontWeight.Medium;
        var intentLabel = Ui.Bound(nameof(model.IntentLabel)); intentLabel.FontSize = 12; intentLabel.TextWrapping = TextWrapping.NoWrap;
        var intent = Ui.Action("Что сделать",() => { },"aiIntent"); intent.Name = "AiIntent";
        intent.Content = Ui.Row(intentIcon,intentPrefix,intentLabel,Ui.Icon("chevrondown",12)); Bind(intent,IsEnabledProperty,nameof(model.CanEditContext));
        ToolTip.SetTip(intent,"SC AI определяет задачу по тексту. Нажмите, чтобы выбрать вручную.");
        intent.Click += (_,_) => IntentMenu(model,intent).Open(intent);
        var generate = Ui.Button("Отправить",model.GenerateAsync,"primary"); generate.Name = "AiGenerate"; generate.Content = Ui.Row(Ui.Text("Отправить"),Ui.Icon("send",15)); ToolTip.SetTip(generate,"Отправить · Enter"); Bind(generate,IsEnabledProperty,nameof(model.CanGenerate));
        var cancel = Ui.Action("Остановить",model.Cancel); cancel.Name = "AiCancel"; cancel.Content = Ui.Row(Ui.Icon("stop",13),Ui.Text("Остановить")); Bind(cancel,IsVisibleProperty,nameof(model.IsBusy));
        var sendSlot = new Grid(); sendSlot.Children.Add(generate); sendSlot.Children.Add(cancel);
        var keys = Ui.Text("Enter — отправить · Shift+Enter — новая строка","muted",11);
        var composerTools = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 }; composerTools.Children.Add(intent); composerTools.Children.Add(keys.Place(0,1)); composerTools.Children.Add(sendSlot.Place(0,2));
        keys.HorizontalAlignment = HorizontalAlignment.Right;
        var composerBody = Ui.Stack(chips,query,composerTools); composerBody.Spacing = 6;
        var progress = new ProgressBar { IsIndeterminate = true, Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(20,0) }; Bind(progress,IsVisibleProperty,nameof(model.IsBusy));
        var composer = new Border { Name = "AiComposer", Child = composerBody, Padding = new(14,12), Classes = { "aiComposer" } };
        var composerLayer = new Grid { Margin = new(0,10,0,0) }; composerLayer.Children.Add(composer); composerLayer.Children.Add(progress);
        var status = Ui.Bound(nameof(model.Status),"muted"); status.Name = "AiStatus"; status.FontSize = 11; status.MaxLines = 2; status.Margin = new(6,8,6,0);
        var composerGroup = Ui.Stack(composerLayer,status); composerGroup.Spacing = 0;

        // ── Приветствие ────────────────────────────────────────────────────
        var heroBadge = Ui.IconBadge("ai",24); heroBadge.Width = heroBadge.Height = 54; heroBadge.CornerRadius = new(18); heroBadge.HorizontalAlignment = HorizontalAlignment.Center; heroBadge.Classes.Add("aiHeroBadge");
        var heroTitle = Ui.Text("","section",14); heroTitle.HorizontalAlignment = HorizontalAlignment.Left;
        var heroSubtitle = Ui.Text("","muted",13); heroSubtitle.TextAlignment = TextAlignment.Left; heroSubtitle.HorizontalAlignment = HorizontalAlignment.Left; heroSubtitle.MaxWidth = 560;
        var cards = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 10, Margin = new(0,10,0,0) };
        Button Example(int index,string icon,string label,string prompt)
        {
            var button = Ui.Action(label,() => { model.Instruction = prompt; query.Focus(); query.CaretIndex = prompt.Length; },"aiSuggestion");
            button.Name = "AiExample_" + index; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var content = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
            var badge = Ui.IconBadge(icon,16); badge.Width = badge.Height = 30; badge.CornerRadius = new(10); badge.VerticalAlignment = VerticalAlignment.Top;
            var detail = Ui.Text((index,model.HasScript) switch { (0,_) => "Создать решение задачи", (1,_) => "Разбор синтаксиса и примеры", (2,_) => "Найти и решить проблему", _ => "Сделать быстрее и надёжнее" },"muted",11); detail.MaxLines = 2; detail.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = Ui.Stack(Ui.Text(label,"section",12),detail); labels.Spacing = 4;
            content.Children.Add(badge); content.Children.Add(labels.Place(0,1)); button.Content = content; Bind(button,IsEnabledProperty,nameof(model.CanGenerate)); return button;
        }
        void Examples()
        {
            cards.Children.Clear();
            var items = model.HasScript
                ? new[] { ("script","Объяснить","Объясни, что делает этот скрипт и какие у него параметры"), ("search","Найти проблемы","Найди проблемы и слабые места в этом скрипте"),
                          ("shield","Сделать надёжнее","Добавь обработку ошибок и журналирование"), ("tag","Оформить","Придумай понятное название и теги") }
                : new[] { ("plus","Написать скрипт","Напиши скрипт, который удаляет временные файлы старше 7 дней"), ("settings","Объяснить команду","Объясни синтаксис команды Get-ChildItem -Recurse с примерами"),
                          ("tools","Исправить ошибку","Помоги найти и исправить ошибку в PowerShell-скрипте"), ("ai","Оптимизировать","Как ускорить обработку больших файлов в PowerShell?") };
            for (var i = 0; i < items.Length; i++) cards.Children.Add(Example(i,items[i].Item1,items[i].Item2,items[i].Item3).Place(i / 2,i % 2));
            heroTitle.Inlines = new InlineCollection();
            if (model.HasScript) { heroTitle.Inlines.Add(new Run("Что сделать со скриптом?")); heroSubtitle.Text = "«" + model.ContextLabel + "» прикреплён. Напишите задачу своими словами — SC AI объяснит, проверит или предложит изменения. Файл меняется только после вашего подтверждения."; }
            else
            {
                heroTitle.Inlines.Add(new Run("Здравствуйте! Я ваш ИИ-помощник в ScriptForge."));
                heroSubtitle.Text = "Я могу помочь с PowerShell-скриптами, настройкой Windows, Active Directory, диагностикой проблем и автоматизацией задач.\n\nЧем могу помочь?";
            }
        }
        var setupHint = Ui.Text("Для ответа выберите модель вверху справа.","muted",12); setupHint.TextAlignment = TextAlignment.Left;
        var setup = Ui.Stack(setupHint); setup.Spacing = 8; setup.Margin = new(0,4,0,0);
        var greetingBody = Ui.Stack(heroTitle,heroSubtitle); greetingBody.Spacing = 6;
        var greetingBubble = new Border { Child = greetingBody, Padding = new(16), CornerRadius = new(10) }; Ui.Resource(greetingBubble,Border.BackgroundProperty,"ScQuiet");
        var avatar = Ui.IconBadge("ai",19); avatar.Width = avatar.Height = 36; avatar.VerticalAlignment = VerticalAlignment.Top;
        avatar.Background = Brush.Parse("#172531"); avatar.Child = Ui.Icon("ai",19); ((Lucide.Avalonia.LucideIcon)avatar.Child).Foreground = Brush.Parse("#4EDDDD");
        var greeting = new Grid { ColumnDefinitions = new("36,*"), ColumnSpacing = 16 }; greeting.Children.Add(avatar); greeting.Children.Add(greetingBubble.Place(0,1));
        var hero = Ui.Stack(greeting,cards,setup); hero.Name = "AiWelcome"; hero.Spacing = 18; hero.Margin = new(0,22,0,0); hero.VerticalAlignment = VerticalAlignment.Top;
        var welcome = Ui.Scroll(hero);
        // ── Лента диалога ──────────────────────────────────────────────────
        var transcript = new ItemsControl { ItemsSource = model.Turns, ItemTemplate = new FuncDataTemplate<AiChatTurn>((turn,_) => turn is null ? new Border() : new AiReplyView(turn)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var tabs = new TabControl { Name = "AiTabs", Classes = { "aiWorkspace" } };
        // Живой код во время генерации: пользователь видит, что скрипт пишется.
        var liveCode = new SelectableTextBlock { FontFamily = Mono, FontSize = 11, LineHeight = 19, TextWrapping = TextWrapping.NoWrap, Margin = new(16,10,16,14) };
        var liveTitle = Ui.Row(Ui.Icon("terminal",14),Ui.Text("Пишу код…","accent",11));
        var liveStack = Ui.Stack(liveTitle,liveCode); liveStack.Spacing = 0; liveTitle.Margin = new(16,12,16,0);
        var live = new Border { Child = liveStack, CornerRadius = new(12), BorderThickness = new(1), Margin = new(44,0,0,18), MaxHeight = 260, ClipToBounds = true, IsVisible = false };
        Ui.Resource(live,Border.BackgroundProperty,"ScCodeBackground"); Ui.Resource(live,Border.BorderBrushProperty,"ScBorder");

        // Любое изменение файла заканчивается явным вопросом «Сохранить изменения?» и подтверждением.
        async Task SaveProposal()
        {
            if (model.Proposal is not { } proposal) return;
            if (await Dialogs.ConfirmAsync(owner,"Сохранить изменения в скрипт?",Path.GetFileName(proposal.Snapshot.Path) + (proposal.Code is null ? " · обновятся название, описание или теги." : " · текущий код останется в истории версий, изменения сохранятся новой версией.") + "\nСкрипт не будет запущен.")) await model.ApplyAsync(proposal);
        }
        var reviewJump = Ui.Button("Посмотреть изменения",() => { tabs.SelectedIndex = 1; return Task.CompletedTask; }); reviewJump.Name = "AiReviewProposal"; reviewJump.Content = Ui.Row(Ui.Icon("eye",14),Ui.Text("Посмотреть изменения"));
        var quickSave = Ui.Button("Сохранить",SaveProposal,"primary"); quickSave.Name = "AiQuickSave"; quickSave.Content = Ui.Row(Ui.Icon("save",14),Ui.Text("Да, сохранить…")); Bind(quickSave,IsEnabledProperty,nameof(model.CanApply));
        var discard = Ui.Action("Не сохранять",model.DiscardProposal,"ghost"); discard.Name = "AiDiscardProposal"; discard.Content = Ui.Text("Нет, не нужно","muted",12); Bind(discard,IsEnabledProperty,nameof(model.CanGenerate));
        var proposalTitle = Ui.Text("Сохранить изменения в скрипт?","section",14);
        var proposalSummary = Ui.Text("Проверьте изменения перед сохранением.","muted",11);
        var calloutGrid = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 14, RowSpacing = 12 }; var calloutText = Ui.Stack(proposalTitle,proposalSummary); calloutText.Spacing = 4;
        var proposalActions = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var b in new Control[] { quickSave,reviewJump,discard }) { b.Margin = new(0,0,8,4); proposalActions.Children.Add(b); }
        calloutGrid.Children.Add(Ui.IconBadge("edit",16)); calloutGrid.Children.Add(calloutText.Place(0,1)); calloutGrid.Children.Add(proposalActions.Place(1,1));
        var callout = new Border { Name = "AiProposalCard", Child = calloutGrid, Padding = new(16), CornerRadius = new(14), Margin = new(44,0,0,18), Classes = { "aiNotice" } }; Bind(callout,IsVisibleProperty,nameof(model.HasProposal));

        // Ответ с исправлением (разбор ошибки, поиск проблем) сам файл не меняет — спрашиваем, применить ли его.
        var applyFix = Ui.Button("Применить к скрипту",model.ApplySuggestedFixAsync,"primary"); applyFix.Name = "AiApplyFix"; applyFix.Content = Ui.Row(Ui.Icon("wand",14),Ui.Text("Да, применить к скрипту"));
        var dismissFix = Ui.Action("Не нужно",model.DismissSuggestedFix,"ghost"); dismissFix.Content = Ui.Text("Нет, только объяснение","muted",12);
        var fixTitle = Ui.Text("Применить это исправление к скрипту?","section",14);
        var fixText = Ui.Text("SC AI внесёт правку в код, покажет изменения и спросит, сохранять ли их. Файл не изменится без вашего подтверждения.","muted",11);
        var fixGrid = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 14, RowSpacing = 12 }; var fixStack = Ui.Stack(fixTitle,fixText); fixStack.Spacing = 4;
        var fixActions = Ui.Row(applyFix,dismissFix);
        fixGrid.Children.Add(Ui.IconBadge("wand",16)); fixGrid.Children.Add(fixStack.Place(0,1)); fixGrid.Children.Add(fixActions.Place(1,1));
        var fixCallout = new Border { Name = "AiFixCard", Child = fixGrid, Padding = new(16), CornerRadius = new(14), Margin = new(44,0,0,18), Classes = { "aiNotice" } }; Bind(fixCallout,IsVisibleProperty,nameof(model.HasSuggestedFix));

        var scriptTitle = Ui.Text("","section",14); scriptTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var scriptSummary = Ui.Text("","muted",11); scriptSummary.MaxLines = 2; scriptSummary.TextTrimming = TextTrimming.CharacterEllipsis;
        var scriptCheck = Ui.Text("","muted",11);
        var openCode = Ui.Action("Открыть код",() => tabs.SelectedIndex = 2); openCode.Name = "AiOpenCode"; openCode.Content = Ui.Row(Ui.Icon("eye",14),Ui.Text("Открыть код"));
        async Task SaveScript()
        {
            if (model.NewScript is not { } script) return;
            var categories = model.Library.Categories.Where(c => c != "Все категории").ToArray();
            var values = await Dialogs.MetadataAsync(owner,categories,null,new(script.Title,model.Library.Category is { } current && categories.Contains(current) ? current : categories.FirstOrDefault() ?? "Other",script.Summary,"",script.Tags,false,"Draft"));
            if (values is not null) await model.SaveNewScriptAsync(script,values.Name,values.Category,values.Description,values.Tags);
        }
        var saveScript = Ui.Button("Сохранить в библиотеку",SaveScript,"primary"); saveScript.Name = "AiSaveScript"; saveScript.Content = Ui.Row(Ui.Icon("save",14),Ui.Text("Сохранить в библиотеку")); Bind(saveScript,IsEnabledProperty,nameof(model.CanSaveNewScript));
        var scriptActions = Ui.Row(openCode,saveScript);
        var scriptHint = Ui.Text("Нужно что-то поменять? Просто напишите, например: «добавь параметр для пути».","muted",11);
        var scriptGrid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 14, RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 10 };
        var scriptText = Ui.Stack(scriptTitle,scriptSummary,scriptCheck); scriptText.Spacing = 4;
        scriptGrid.Children.Add(Ui.IconBadge("script",16)); scriptGrid.Children.Add(scriptText.Place(0,1)); scriptGrid.Children.Add(scriptActions.Place(1,1)); scriptGrid.Children.Add(scriptHint.Place(2,1));
        var scriptCallout = new Border { Name = "AiNewScriptCard", Child = scriptGrid, Padding = new(16), CornerRadius = new(14), Margin = new(44,0,0,18), Classes = { "aiNotice" } }; Bind(scriptCallout,IsVisibleProperty,nameof(model.HasNewScript));

        var chatStack = Ui.Stack(transcript,live,fixCallout,callout,scriptCallout); chatStack.Spacing = 0; chatStack.Margin = new(0,12,8,0);
        var chatScroll = Ui.Scroll(chatStack); chatScroll.Name = "AiTranscript";
        var follow = true; var scheduled = false;
        var latest = Ui.Action("К последнему ответу",() => { follow = true; chatScroll.ScrollToEnd(); },"aiModel"); latest.Name = "AiLatest"; latest.HorizontalAlignment = HorizontalAlignment.Right; latest.VerticalAlignment = VerticalAlignment.Bottom; latest.Margin = new(0,0,16,12); latest.IsVisible = false;
        void Follow()
        {
            if (!follow || scheduled) return; scheduled = true;
            Dispatcher.UIThread.Post(() => { scheduled = false; if (follow) chatScroll.ScrollToEnd(); },DispatcherPriority.Loaded);
        }
        chatScroll.ScrollChanged += (_,args) =>
        {
            if (args.ExtentDelta.Y != 0 || args.ViewportDelta.Y != 0) Follow();
            else if (args.OffsetDelta.Y != 0) follow = chatScroll.Extent.Height - chatScroll.Viewport.Height - chatScroll.Offset.Y < 70;
            latest.IsVisible = model.HasTurns && !follow;
        };
        var chat = new Grid(); chat.Children.Add(chatScroll); chat.Children.Add(welcome); chat.Children.Add(latest);

        // ── Просмотр изменений и кода ──────────────────────────────────────
        var diff = new SelectableTextBlock { Name = "AiDiff", FontFamily = Mono, FontSize = 12, LineHeight = 23, TextWrapping = TextWrapping.NoWrap, Margin = new(20), Inlines = new InlineCollection() };
        var diffScroll = new ScrollViewer { Content = diff, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
        var fileLabel = Ui.Text("","section",12); fileLabel.TextWrapping = TextWrapping.NoWrap; fileLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        var diffStats = Ui.Text("","accent",11); diffStats.FontFamily = Mono;
        var diffHeading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14, Margin = new(18,14) }; diffHeading.Children.Add(fileLabel); diffHeading.Children.Add(diffStats.Place(0,1));
        var diffTop = new Border { Child = diffHeading, BorderThickness = new(0,0,0,1) }; Ui.Resource(diffTop,Border.BorderBrushProperty,"ScBorder");
        var diffGrid = new Grid { RowDefinitions = new("Auto,*") }; diffGrid.Children.Add(diffTop); diffGrid.Children.Add(diffScroll.Place(1,0));
        var diffPane = new Border { Child = diffGrid, CornerRadius = new(14), BorderThickness = new(1), Margin = new(0,12,0,0), ClipToBounds = true }; Ui.Resource(diffPane,Border.BackgroundProperty,"ScCodeBackground"); Ui.Resource(diffPane,Border.BorderBrushProperty,"ScBorder");
        var draft = new SelectableTextBlock { Name = "AiDraft", FontFamily = Mono, FontSize = 12, LineHeight = 22, Margin = new(20), TextWrapping = TextWrapping.NoWrap }; Bind(draft,TextBlock.TextProperty,nameof(model.Draft));
        var copyCode = Ui.Button("Копировать код",async () => { if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(model.Draft); },"ghost"); copyCode.Content = Ui.Row(Ui.Icon("copy",14),Ui.Text("Копировать","muted",12));
        var codeTitle = Ui.Text("","section",12); codeTitle.TextTrimming = TextTrimming.CharacterEllipsis; codeTitle.TextWrapping = TextWrapping.NoWrap;
        var codeHeading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14, Margin = new(18,8,8,8) }; codeHeading.Children.Add(codeTitle); codeHeading.Children.Add(copyCode.Place(0,1));
        var codeTop = new Border { Child = codeHeading, BorderThickness = new(0,0,0,1) }; Ui.Resource(codeTop,Border.BorderBrushProperty,"ScBorder");
        // Во время генерации — простой текст; готовый код — с подсветкой и номерами строк.
        var plainCode = new ScrollViewer { Content = draft, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
        var codeHost = new ContentControl { Content = plainCode, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        void RenderCode() => codeHost.Content = model.IsBusy || !model.HasDraft ? plainCode : ScriptCodeView.Render(model.Draft.Replace("\r\n","\n").TrimEnd('\n'));
        var codeGrid = new Grid { RowDefinitions = new("Auto,*") }; codeGrid.Children.Add(codeTop); codeGrid.Children.Add(codeHost.Place(1,0));
        var codePane = new Border { Child = codeGrid, CornerRadius = new(14), BorderThickness = new(1), Margin = new(0,12,0,0), ClipToBounds = true }; Ui.Resource(codePane,Border.BackgroundProperty,"ScCodeBackground"); Ui.Resource(codePane,Border.BorderBrushProperty,"ScBorder");
        var diffTab = new TabItem { Header = "Изменения", Content = diffPane }; Bind(diffTab,IsVisibleProperty,nameof(model.HasProposal));
        var codeTab = new TabItem { Header = "Код", Content = codePane }; Bind(codeTab,IsVisibleProperty,nameof(model.HasDraft));
        tabs.Items.Add(new TabItem { Header = "Диалог", Content = chat }); tabs.Items.Add(diffTab); tabs.Items.Add(codeTab);
        var validationIcon = new ContentControl();
        var validation = Ui.Bound(nameof(model.Validation),"muted"); validation.Name = "AiReviewValidation"; validation.FontSize = 12;
        var back = Ui.Action("К диалогу",() => tabs.SelectedIndex = 0,"ghost"); back.Content = Ui.Row(Ui.Icon("arrowright",14),Ui.Text("К диалогу","muted",12)); ((Control)((StackPanel)back.Content).Children[0]).RenderTransform = new RotateTransform(180);
        var apply = Ui.Button("Сохранить изменения",SaveProposal,"primary"); apply.Name = "AiApply"; apply.Content = Ui.Row(Ui.Icon("save",14),Ui.Text("Сохранить изменения")); Bind(apply,IsEnabledProperty,nameof(model.CanApply));
        var saveFromCode = Ui.Button("Сохранить в библиотеку",SaveScript,"primary"); saveFromCode.Content = Ui.Row(Ui.Icon("save",14),Ui.Text("Сохранить в библиотеку")); Bind(saveFromCode,IsEnabledProperty,nameof(model.CanSaveNewScript));
        var actionSlot = new Grid(); actionSlot.Children.Add(apply); actionSlot.Children.Add(saveFromCode);
        var validationRow = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 }; validationRow.Children.Add(validationIcon); validationRow.Children.Add(validation.Place(0,1));
        var reviewTools = new Grid { RowDefinitions = new("Auto,Auto"), ColumnDefinitions = new("*,Auto"), RowSpacing = 10, ColumnSpacing = 14 };
        Grid.SetColumnSpan(validationRow,2); reviewTools.Children.Add(validationRow); reviewTools.Children.Add(back.Place(1,0)); reviewTools.Children.Add(actionSlot.Place(1,1));
        validationRow.VerticalAlignment = VerticalAlignment.Center;
        var reviewPanel = new Border { Child = reviewTools, Margin = new(0,12,0,0) };
        var workspace = new Grid { RowDefinitions = new("*,Auto") }; workspace.Children.Add(tabs); workspace.Children.Add(reviewPanel.Place(1,0));
        var center = new Grid { RowDefinitions = new("*,Auto"), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch }; center.Children.Add(workspace); center.Children.Add(composerGroup.Place(1,0));
        var left = new Grid { RowDefinitions = new("Auto,*"), Margin = new(30,34,28,20) }; left.Children.Add(title); left.Children.Add(center.Place(1,0)); var root = new Grid { ColumnDefinitions = new("*,340") }; var sidebar = ReferenceUi.Side(Ui.Scroll(ContextSidebar(owner,model,modelButton,query)),26); root.Children.Add(left); root.Children.Add(sidebar.Place(0,1)); Content = root;

        void LayoutMode()
        {
            var index = tabs.SelectedIndex;
            // Пока нет кода и изменений, вкладка «Диалог» одна — полоса вкладок не нужна.
            tabs.Classes.Set("single", !model.HasProposal && !model.HasDraft);
            composerGroup.IsVisible = index == 0;
            reviewPanel.IsVisible = index == 1 && model.HasProposal || index == 2 && (model.HasNewScript || model.HasProposal);
            apply.IsVisible = model.HasProposal && index != 0; saveFromCode.IsVisible = !apply.IsVisible && model.HasNewScript;
            var valid = model.Proposal?.IsValid ?? model.NewScript?.SyntaxValid ?? true;
            var icon = Ui.Icon(valid ? "ok" : "warning",15); Ui.Resource(icon,Lucide.Avalonia.LucideIcon.ForegroundProperty,valid ? "ScAccentText" : "ScDanger"); validationIcon.Content = icon;
            codeTitle.Text = model.NewScript is { } script ? script.Title + ".ps1" : model.Proposal is { } proposal ? "Предложенный фрагмент · " + Path.GetFileName(proposal.Snapshot.Path) : "Код";
        }
        tabs.SelectionChanged += (_,args) => { if (ReferenceEquals(args.Source,tabs)) LayoutMode(); };
        void Update(object? sender,PropertyChangedEventArgs args)
        {
            switch (args.PropertyName)
            {
                case nameof(model.HasTurns): welcome.IsVisible = !model.HasTurns; follow = true; tabs.SelectedIndex = 0; Follow(); break;
                case nameof(model.IsBusy):
                    generate.IsVisible = !model.IsBusy; live.IsVisible = model.IsBusy && model.HasDraft; RenderCode();
                    if (!model.IsBusy && model.HasNewScript && tabs.SelectedIndex == 0) Follow();
                    break;
                case nameof(model.HasModel): case nameof(model.IsModelLoaded):
                    setup.IsVisible = !model.HasModel; Ui.Resource(modelDot,Border.BackgroundProperty,model.HasModel ? "ScAccentText" : "ScMuted"); break;
                case nameof(model.HasScript): case nameof(model.SelectedEntry): case nameof(model.ContextLabel):
                    removeContext.IsVisible = model.HasScript; contextIcon.Kind = model.HasScript ? Lucide.Avalonia.LucideIconKind.FileCode : Lucide.Avalonia.LucideIconKind.Paperclip;
                    contextButton.Classes.Set("active",model.HasScript); Examples(); break;
                case nameof(model.IntentIcon): case nameof(model.AutoDetect): case nameof(model.IntentLabel):
                    intentIcon.Content = Ui.Icon(model.IntentIcon,14); intentPrefix.Text = model.AutoDetect ? "Авто ·" : "Вручную ·"; break;
                case nameof(model.Answer): case nameof(model.HasSuggestedFix): Follow(); break;
                case nameof(model.Draft):
                    if (!model.IsBusy) RenderCode();
                    live.IsVisible = model.IsBusy && model.HasDraft;
                    if (live.IsVisible) { var lines = model.Draft.Split('\n'); liveCode.Text = string.Join('\n',lines.Skip(Math.Max(0,lines.Length - 10))); Follow(); }
                    break;
                case nameof(model.NewScript):
                    if (model.NewScript is { } script)
                    {
                        scriptTitle.Text = "Скрипт готов: " + script.Title; scriptSummary.Text = script.Summary; scriptSummary.IsVisible = script.Summary.Length > 0;
                        scriptCheck.Text = script.SyntaxValid ? "✓ Синтаксис проверен PowerShell Parser" : "⚠ Parser нашёл ошибки — посмотрите код перед сохранением";
                        Follow();
                    }
                    else if (tabs.SelectedIndex == 2 && !model.HasProposal) tabs.SelectedIndex = 0;
                    LayoutMode(); break;
                case nameof(model.Proposal):
                    if (!model.HasProposal && tabs.SelectedIndex == 1) tabs.SelectedIndex = 0;
                    if (model.Proposal is { } proposal)
                    {
                        fileLabel.Text = Path.GetFileName(proposal.Snapshot.Path);
                        proposalTitle.Text = proposal.Code is null ? "Сохранить новые название, описание или теги?" : "Сохранить изменения в скрипт?";
                        var lines = model.Diff.Split('\n'); var stats = $"+{lines.Count(l => l.StartsWith("+ "))}  −{lines.Count(l => l.StartsWith("− "))}"; diffStats.Text = stats;
                        proposalSummary.Text = proposal.IsValid ? $"{Path.GetFileName(proposal.Snapshot.Path)} · {stats} строк. Посмотрите изменения перед сохранением. Выполнение не проверялось."
                            : "Предварительная проверка нашла ошибки — сохранить нельзя. Посмотрите подробности в проверке кода.";
                    }
                    LayoutMode(); Follow(); break;
                case nameof(model.Diff):
                    diff.Inlines!.Clear();
                    foreach (var line in model.Diff.Split('\n')) { var run = new Run(line + "\n"); Ui.Resource(run,TextElement.ForegroundProperty,line.StartsWith("+ ") ? "ScAccentText" : line.StartsWith("− ") ? "ScDanger" : "ScMuted"); diff.Inlines.Add(run); }
                    break;
                case nameof(model.HasDraft): if (!model.HasDraft && tabs.SelectedIndex == 2) tabs.SelectedIndex = 0; LayoutMode(); break;
            }
        }
        void Responsive()
        {
            var compact = Bounds.Height < 680;
            heroTitle.FontSize = 13; hero.Spacing = compact ? 10 : 14;
            heroBadge.IsVisible = false; subtitle.IsVisible = left.Bounds.Width >= 570; keys.IsVisible = false; var wide = Bounds.Width >= 1030; root.ColumnDefinitions = new(wide ? "*,340" : "*,280");
            composer.Padding = compact ? new(12,10) : new(14,12); query.MinHeight = compact ? 40 : 52;
            title.Margin = new(0,0,0,compact ? 10 : 16); titleBadge.IsVisible = false;
            var four = left.Bounds.Width >= 700; cards.ColumnDefinitions = new(four ? "*,*,*,*" : "*,*"); cards.RowDefinitions = new(four ? "Auto" : "Auto,Auto"); var cardIndex = 0; foreach (var item in cards.Children) { Grid.SetColumn(item,four ? cardIndex : cardIndex % 2); Grid.SetRow(item,four ? 0 : cardIndex / 2); cardIndex++; } foreach (var card in cards.Children.OfType<Button>()) { card.MinHeight = compact ? 58 : 72; card.Padding = four ? new(10,10) : new(12,12); }
        }
        SizeChanged += (_,_) => Responsive();
        AttachedToVisualTree += (_,_) =>
        {
            model.PropertyChanged -= Update; model.PropertyChanged += Update;
            foreach (var name in new[] { nameof(model.HasTurns),nameof(model.IsBusy),nameof(model.HasModel),nameof(model.HasScript),nameof(model.IntentIcon),nameof(model.Proposal),nameof(model.NewScript),nameof(model.Diff) }) Update(null,new(name));
            LayoutMode(); Responsive();
            if (model.CanEditContext) Dispatcher.UIThread.Post(() => query.Focus(),DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_,_) => model.PropertyChanged -= Update;
    }

    /// <summary>Ручной выбор задачи — для случаев, когда автоопределение ошиблось.</summary>
    private static ContextMenu IntentMenu(AiAssistantViewModel model,Control anchor)
    {
        MenuItem Item(AiTaskDefinition task)
        {
            var item = new MenuItem { Header = task.Name, Icon = Ui.Icon(task.Icon,15), IsEnabled = !task.NeedsScript || model.HasScript };
            if (!model.AutoDetect && model.SelectedTask.Id == task.Id) item.FontWeight = FontWeight.SemiBold;
            if (task.NeedsScript && !model.HasScript) ToolTip.SetTip(item,"Сначала прикрепите скрипт");
            item.Click += (_,_) => model.SelectedTask = task; return item;
        }
        MenuItem Group(string header,string icon,params string[] ids)
        {
            var group = new MenuItem { Header = header, Icon = Ui.Icon(icon,15), IsEnabled = model.HasScript };
            foreach (var id in ids) group.Items.Add(Item(AiTaskDefinition.Get(id))); return group;
        }
        var auto = new MenuItem { Header = model.AutoDetect ? "Определять автоматически ✓" : "Определять автоматически", Icon = Ui.Icon("wand",15) };
        auto.Click += (_,_) => model.AutoDetect = true;
        return new ContextMenu
        {
            PlacementTarget = anchor,
            Items =
            {
                auto, new Separator(),
                Item(AiTaskDefinition.Get("ask")), Item(AiTaskDefinition.Get("create")), Item(AiTaskDefinition.Get("error")), new Separator(),
                Item(AiTaskDefinition.Get("explain")), Item(AiTaskDefinition.Get("audit")),
                Group("Изменить код","edit","complete","exceptions","logging","optimize","comments"),
                Group("Оформить","document","summary","description","tags"),
                Item(AiTaskDefinition.Get("changes"))
            }
        };
    }
}
