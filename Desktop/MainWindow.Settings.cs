using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SC.ScriptForge.Services;

namespace SC.ScriptForge;

public sealed partial class MainWindow
{
    private Control SettingsView()
    {
        Control Setting(string title,string detail,Control input)
        {
            var copy=Ui.Stack(Ui.Text(title,"section",12),Ui.Text(detail,"muted",11));copy.Spacing=3;
            var row=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=16};row.Children.Add(copy);row.Children.Add(input.Place(0,1));return row;
        }
        Control Section(string icon,string title,string detail,params Control[] fields)
        {
            var body=Ui.Stack(Ui.Text(title,"section",13.5),Ui.Text(detail,"muted",11.5));body.Spacing=4;
            foreach(var field in fields){field.Margin=new(0,10,0,0);body.Children.Add(field);}
            var layout=new Grid{ColumnDefinitions=new("26,*"),ColumnSpacing=16};var glyph=Ui.Icon(icon,21);glyph.VerticalAlignment=VerticalAlignment.Top;glyph.Margin=new(0,2,0,0);layout.Children.Add(glyph);layout.Children.Add(body.Place(0,1));var card=Ui.Card(layout);card.Padding=new(16,13);return card;
        }
        var root=new Grid{Name="SettingsPage",ColumnDefinitions=new(_compact?"*,300":"*,380")};
        var page=Ui.Stack(ReferenceUi.Heading("Настройки приложения","Настройки и безопасность","Настройте внешний вид, выполнение скриптов и другие параметры"));page.Margin=new(32,30,28,28);page.Spacing=13;
        page.Children[0].Margin=new(0,0,0,10);
        var theme=new ComboBox{Name="SettingsTheme",ItemsSource=new[]{"Тёмная","Светлая (по умолчанию)"},SelectedIndex=ActualThemeVariant==ThemeVariant.Light?1:0,MinWidth=_compact?125:190};AutomationProperties.SetName(theme,"Тема интерфейса");
        theme.SelectionChanged+=(_,_)=>{_preferences.Theme=theme.SelectedIndex==1?"Light":"Dark";ApplyTheme();SavePreferences();};
        var language=new ComboBox{Name="SettingsLibraryLanguage",ItemsSource=new[]{"Русский", "English"},SelectedIndex=LibraryLocalization.IsEnglish?1:0,MinWidth=_compact?125:190};
        AutomationProperties.SetName(language,"Язык библиотеки и отчётов / Library and report language");
        language.SelectionChanged+=async (_,_)=>
        {
            _preferences.LibraryLanguage=language.SelectedIndex==1?"en":"ru";
            LibraryLocalization.Language=_preferences.LibraryLanguage;SavePreferences();
            await _library.RefreshAsync();
        };
        theme.HorizontalAlignment=HorizontalAlignment.Stretch;language.HorizontalAlignment=HorizontalAlignment.Stretch;var appearance=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=24};appearance.Children.Add(Ui.Field("Тема интерфейса",theme));appearance.Children.Add(Ui.Field("Язык библиотеки и отчётов / Library language",language).Place(0,1));
        page.Children.Add(Section("theme","Внешний вид","Настройки интерфейса приложения",appearance));
        page.Children.Add(Ui.Text("Язык меняет названия, описания и новые отчёты встроенных проверок. Окна запуска используют язык, выбранный при открытии. / Applies to built-in cards and new reports. Run windows keep the language selected when opened.","muted",11));
        var motion=new ToggleSwitch{IsChecked=_preferences.ReduceMotion};AutomationProperties.SetName(motion,"Уменьшить анимацию");motion.IsCheckedChanged+=(_,_)=>{_preferences.ReduceMotion=motion.IsChecked==true;Ui.ReduceMotion=_preferences.ReduceMotion;SavePreferences();};
        page.Children.Add(Section("shield","Политика выполнения","Правила запуска PowerShell-скриптов",
            PolicyFields()));
        Control PolicyFields()
        {
            var grid=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=24};
            grid.Children.Add(Setting("Проверка перед запуском","Параметры и политика выполнения",Feedback.Chip("Включена",Tone.Success)));
            grid.Children.Add(Setting("Уменьшить анимацию","Спокойные переходы",motion).Place(0,1));
            if(_compact){grid.ColumnDefinitions=new("*");grid.RowDefinitions=new("Auto,Auto");grid.RowSpacing=12;grid.Children[1].Place(1,0);}
            return grid;
        }
        page.Children.Add(Section("users","Разрешения","Управление системными разрешениями",
            Setting("Уровень привилегий по умолчанию","Запуск с правами текущего процесса",Ui.Pill("Текущий пользователь"))));
        page.Children.Add(Section("key","Безопасное выполнение","Контроль команд перед запуском",
            Setting("Рабочее окружение","Системная информация и характеристики компьютера",Ui.Action("Посмотреть",()=>Navigate("computer")))));
        page.Children.Add(Section("disk","Репозитории скриптов","Источники скриптов и обновлений",
            Setting("Основной репозиторий","Ваша локальная библиотека ScriptForge",Command("Открыть папку",()=>OpenFolderAsync(_repository.DataRoot))),
            Setting("Добавить скрипты","Импорт файлов PowerShell в библиотеку",Command("Импортировать…",ImportAsync))));
        page.Children.Add(Section("history","Данные и резервные копии","Версии, история запусков и восстановление",
            Setting("Резервная копия библиотеки","Скрипты и метаданные в ZIP-архиве",Command("Создать копию…",BackupAsync)),
            Setting("Удалённые скрипты","Восстановление из локальной корзины",Ui.Action("Открыть корзину",()=>Navigate("trash")))));
        page.Children.Add(Section("ai","Настройки ИИ-помощника","Параметры интеллектуального помощника",
            Setting("Модель помощника",_ai.ModelLabel,Command("Модель и настройки…",ShowAiSettingsAsync)),
            Ui.Text(_ai.IsExternal?"Запросы отправляются на настроенный вами сервер модели.":"Встроенная модель обрабатывает запросы локально на компьютере.","muted",11.5)));
        root.Children.Add(Ui.Scroll(page));
        var side=Ui.Stack();side.Spacing=26;
        side.Children.Add(Ui.Text("Рабочее пространство","section",15));
        var identity=new Grid{ColumnDefinitions=new("48,*"),ColumnSpacing=16};identity.Children.Add(new BrandMark{Width=48,Height=48,CornerRadius=new(13)});
        var identityCopy=Ui.Stack(Ui.Text("Shmonov Studio","section",13.5),Ui.Text("ScriptForge Desktop","muted",12));identityCopy.Spacing=4;identity.Children.Add(identityCopy.Place(0,1));
        var account=Ui.Stack(identity,ReferenceUi.Metadata("computer","Платформа","Windows"),ReferenceUi.Metadata("folder","Библиотека","Локальное хранение"),ReferenceUi.Metadata("refresh","Версия","1.5.1"),Command("Открыть данные приложения",()=>OpenFolderAsync(_repository.DataRoot)));account.Spacing=20;
        var accountCard=Ui.Card(account);Ui.Resource(accountCard,Border.BackgroundProperty,"ScSelected");side.Children.Add(accountCard);
        side.Children.Add(Ui.Text("Безопасность","section",15));
        var security=Ui.Card(Ui.Stack(Ui.Row(Ui.Icon("shield",21),Ui.Text("Ваши данные под контролем","section",12.5)),Ui.Text(_ai.IsExternal?"Скрипты хранятся на компьютере. Для ИИ используется сервер, указанный в настройках модели.":"Скрипты и история хранятся на вашем компьютере. Встроенный ИИ работает локально.","muted",12)));Ui.Resource(security,Border.BackgroundProperty,"ScSelected");side.Children.Add(security);
        var checks=Ui.Stack();foreach(var line in new[]{"Локальное выполнение скриптов","Проверка синтаксиса по запросу","История версий при изменении","Подтверждение применения правок ИИ"}){var icon=Ui.Icon("ok",16);Ui.Resource(icon,Lucide.Avalonia.LucideIcon.ForegroundProperty,"ScSuccess");checks.Children.Add(Ui.Row(icon,Ui.Text(line,"soft",12)));}checks.Spacing=16;side.Children.Add(checks);
        side.Children.Add(ReferenceUi.Line());side.Children.Add(Ui.Text("Системная информация","section",15));
        side.Children.Add(Ui.Stack(ReferenceUi.Metadata("computer","Версия приложения","v1.5.1"),ReferenceUi.Metadata("disk","Скриптов в каталоге",_library.Entries.Count.ToString()),ReferenceUi.Metadata("window","Платформа",Environment.OSVersion.VersionString),ReferenceUi.Metadata("cpu","Архитектура",System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())));
        side.Children.Add(Ui.WithIcon(Command("Журнал приложения",()=>OpenFolderAsync(Path.GetDirectoryName(_log.Path)!)),"document"));
        root.Children.Add(ReferenceUi.Side(Ui.Scroll(side),24).Place(0,1));return root;
    }
}
