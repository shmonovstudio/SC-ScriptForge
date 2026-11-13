using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SC.ScriptForge.Models;

namespace SC.ScriptForge;

public sealed partial class MainWindow
{
    public sealed class AutomationScenario
    {
        public Guid Id {get;set;}=Guid.NewGuid();
        public string Name {get;set;}="Новый сценарий";
        public string Description {get;set;}="";
        public string Frequency {get;set;}="Ежедневно";
        public string Time {get;set;}="08:00";
        public bool Enabled {get;set;}
        public List<Guid> Steps {get;set;}=[];
        public DateTime? LastReminder {get;set;}
    }
    private List<AutomationScenario>? _scenarios;
    private Guid? _selectedScenario;
    private string _scenarioQuery="",_scenarioFilter="Все";
    private DispatcherTimer? _scenarioTimer;
    private List<AutomationScenario> Scenarios
    {
        get
        {
            if(_scenarios is not null)return _scenarios;
            try{_scenarios=File.Exists(ScenarioPath)?JsonSerializer.Deserialize<List<AutomationScenario>>(File.ReadAllText(ScenarioPath))??[]:[];}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){_log.Failure("automation.load",e);_scenarios=[];}
            _scenarios=_scenarios.Where(s=>s is not null&&s.Steps is not null).ToList();
            return _scenarios;
        }
    }
    private string ScenarioPath=>Path.Combine(_repository.DataRoot,"automation.json");
    private void SaveScenarios()
    {
        var temporary=ScenarioPath+".tmp";try{File.WriteAllText(temporary,JsonSerializer.Serialize(Scenarios,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,ScenarioPath,true);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){ShowError("automation.save",e);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void InitializeAutomation()
    {
        _=Scenarios;
        _scenarioTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(20)};
        _scenarioTimer.Tick+=(_,_)=>
        {
            var now=DateTime.Now;
            foreach(var scenario in Scenarios.Where(s=>s.Enabled&&s.Steps.Count>0))
            {
                if(!TimeSpan.TryParseExact(scenario.Time,@"hh\:mm",null,out var time)||now.TimeOfDay<time||now.TimeOfDay-time>TimeSpan.FromMinutes(2)||scenario.LastReminder?.Date==now.Date)continue;
                if(scenario.Frequency=="Еженедельно"&&now.DayOfWeek!=DayOfWeek.Monday||scenario.Frequency=="Ежемесячно"&&now.Day!=1)continue;
                scenario.LastReminder=now;SaveScenarios();_selectedScenario=scenario.Id;Notify("Пора запустить сценарий «"+scenario.Name+"». Откройте «Автоматизацию» и нажмите «Запустить сейчас».");
            }
        };
        _scenarioTimer.Start();Closed+=(_,_)=>_scenarioTimer.Stop();
    }
    private Control AutomationView()
    {
        var root=new Grid{Name="AutomationPage",ColumnDefinitions=new(_compact?"*,330":"*,460")};
        var left=new Grid{RowDefinitions=new("Auto,Auto,Auto,*"),RowSpacing=24,Margin=new(32,32,24,20)};
        left.Children.Add(ReferenceUi.Heading("Автоматизация и планирование задач","Автоматизация","Собирайте скрипты в сценарии и планируйте запуск"));
        var search=Ui.Input(_scenarioQuery,"Найти задачу или сценарий…");search.Name="AutomationSearch";search.MinHeight=42;
        var create=Ui.WithIcon(Ui.Action("Новый сценарий",()=>{var scenario=new AutomationScenario();Scenarios.Add(scenario);_selectedScenario=scenario.Id;SaveScenarios();Render();},"ink"),"plus");
        var toolbar=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=10};toolbar.Children.Add(search);toolbar.Children.Add(create.Place(0,1));left.Children.Add(toolbar.Place(1,0));
        var filters=Ui.Row();foreach(var label in new[]{"Все","Запланированные","Отключенные"}){var button=Ui.Action(label,()=>{_scenarioFilter=label;Render();},"tab");button.Classes.Set("active",label==_scenarioFilter);filters.Children.Add(button);}left.Children.Add(filters.Place(2,0));
        var rows=Ui.Stack();rows.Spacing=0;
        var details=new ContentControl{Name="ScenarioDetails",HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        void Update()
        {
            rows.Children.Clear();var scenarios=Scenarios.Where(s=>s.Name.Contains(_scenarioQuery,StringComparison.CurrentCultureIgnoreCase)&&(_scenarioFilter=="Все"||s.Enabled==(_scenarioFilter=="Запланированные"))).ToArray();
            var selected=scenarios.FirstOrDefault(s=>s.Id==_selectedScenario)??scenarios.FirstOrDefault();_selectedScenario=selected?.Id;
            foreach(var scenario in scenarios)
            {
                var row=new Grid{ColumnDefinitions=new("52,*,82,42"),ColumnSpacing=14};var badge=Ui.IconBadge("computer",22);badge.Width=badge.Height=52;row.Children.Add(badge);
                var labels=Ui.Stack(ReferenceUi.Ellipsis(scenario.Name,"section",13),ReferenceUi.Ellipsis(scenario.Description,"muted",11),Ui.Pill(scenario.Steps.Count+" шагов"));labels.Spacing=4;row.Children.Add(labels.Place(0,1));
                row.Children.Add(Ui.Text(scenario.Frequency+"\n"+scenario.Time,"muted",11).Place(0,2));
                var toggle=new ToggleSwitch{IsChecked=scenario.Enabled,IsEnabled=scenario.Steps.Count>0};AutomationProperties.SetName(toggle,"Включить расписание «"+scenario.Name+"»");toggle.IsCheckedChanged+=(_,_)=>{scenario.Enabled=toggle.IsChecked==true;SaveScenarios();};row.Children.Add(toggle.Place(0,3));
                var button=Ui.Action(scenario.Name,()=>{_selectedScenario=scenario.Id;Update();});button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new(14,19);button.BorderThickness=new(0,0,0,1);button.CornerRadius=new(0);button.Content=row;
                Ui.Resource(button,Button.BorderBrushProperty,"ScLine");Ui.Resource(button,Button.BackgroundProperty,selected==scenario?"ScSelected":"ScSurface");rows.Children.Add(button);
            }
            if(scenarios.Length==0){var empty=Ui.Stack(Ui.IconBadge("clock"),Ui.Text("Создайте первый сценарий","section",18),Ui.Text("Добавьте скрипты из каталога, выберите время и сохраните расписание.","muted",12));empty.Margin=new(20,40);rows.Children.Add(empty);}
            details.Content=ScenarioEditor(selected,Update);
        }
        search.TextChanged+=(_,_)=>{_scenarioQuery=search.Text??"";Update();};
        left.Children.Add(Ui.Scroll(rows).Place(3,0));root.Children.Add(left);root.Children.Add(ReferenceUi.Side(details,24).Place(0,1));Update();return root;
    }
    private Control ScenarioEditor(AutomationScenario? scenario,Action refresh)
    {
        if(scenario is null)return Ui.Stack(ReferenceUi.SmallTitle("Настройка сценария"),Ui.Text("Выберите сценарий","section",20),Ui.Text("Создайте сценарий, чтобы настроить расписание и последовательность шагов.","muted",12));
        var root=Ui.Stack();root.Spacing=18;root.Children.Add(ReferenceUi.SmallTitle("Настройка сценария"));
        var title=Ui.Text(scenario.Name,"section",20);title.FontWeight=FontWeight.Bold;var titleRow=new Grid{ColumnDefinitions=new("54,*,42"),ColumnSpacing=14};titleRow.Children.Add(Ui.IconBadge("computer",24));titleRow.Children.Add(title.Place(0,1)); var enabled=new ToggleSwitch{IsChecked=scenario.Enabled,IsEnabled=scenario.Steps.Count>0};AutomationProperties.SetName(enabled,"Включить сценарий");enabled.IsCheckedChanged+=(_,_)=>{scenario.Enabled=enabled.IsChecked==true;SaveScenarios();};titleRow.Children.Add(enabled.Place(0,2));root.Children.Add(titleRow); var sectionTabs=Ui.Row();sectionTabs.Spacing=2;root.Children.Add(sectionTabs);
        var name=Ui.Input(scenario.Name);name.Name="ScenarioName";name.LostFocus+=(_,_)=>{var value=string.IsNullOrWhiteSpace(name.Text)?"Новый сценарий":name.Text.Trim();if(scenario.Name!=value){scenario.Name=value;title.Text=value;SaveScenarios();}};
        var description=Ui.Input(scenario.Description,multiline:true);description.MinHeight=65;description.LostFocus+=(_,_)=>{scenario.Description=description.Text??"";SaveScenarios();};
        var nameField=Ui.Field("Название сценария",name);root.Children.Add(nameField);root.Children.Add(Ui.Field("Описание",description));
        root.Children.Add(ReferenceUi.Line());root.Children.Add(Ui.Text("Триггер запуска","section",12));root.Children.Add(Ui.Pill("По расписанию · с подтверждением"));
        var frequency=new ComboBox{ItemsSource=new[]{"Ежедневно","Еженедельно","Ежемесячно"},SelectedItem=scenario.Frequency,HorizontalAlignment=HorizontalAlignment.Stretch};frequency.SelectionChanged+=(_,_)=>{scenario.Frequency=frequency.SelectedItem as string??"Ежедневно";SaveScenarios();};
        var time=Ui.Input(scenario.Time,"08:00");time.Name="ScenarioTime";time.MaxLength=5;
        var validation=Ui.Text("","muted",10.5);validation.IsVisible=false;
        time.LostFocus+=(_,_)=>{if(TimeSpan.TryParseExact(time.Text,@"hh\:mm",null,out var parsed)&&parsed.TotalHours<24){scenario.Time=time.Text!;SaveScenarios();validation.IsVisible=false;}else{validation.Text="Введите время в формате ЧЧ:ММ, например 08:00.";validation.IsVisible=true;time.Text=scenario.Time;}};
        var schedule=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=12};schedule.Children.Add(Ui.Field("Расписание",frequency));schedule.Children.Add(Ui.Field("Время запуска",time).Place(0,1));root.Children.Add(schedule);root.Children.Add(validation);
        var hint=Ui.Card(Ui.Text("Приложение напомнит о запуске в выбранное время. Для напоминаний ScriptForge должен быть открыт. Еженедельно — по понедельникам, ежемесячно — 1-го числа. Скрипты запускаются после проверки параметров.","muted",11));Ui.Resource(hint,Border.BackgroundProperty,"ScSelected");root.Children.Add(hint);
        var stepsTitle=Ui.Text("Шаги сценария","section",12.5);root.Children.Add(stepsTitle); foreach(var (label,anchor) in new[]{("Общие",nameField),("Расписание",schedule),("Шаги",(Control)stepsTitle),("Уведомления",hint)}){var tab=Ui.Action(label,()=>{anchor.BringIntoView();foreach(var item in sectionTabs.Children.OfType<Button>())item.Classes.Set("active",(string?)item.Content==label);},"tab");tab.Classes.Set("active",label=="Общие");tab.Padding=new(9,7);sectionTabs.Children.Add(tab);}
        for(var i=0;i<scenario.Steps.Count;i++)
        {
            var index=i;var id=scenario.Steps[i];var entry=_library.Entries.FirstOrDefault(e=>e.Id==id);
            var row=new Grid{ColumnDefinitions=new("24,*,30"),ColumnSpacing=8};row.Children.Add(Ui.Text((i+1).ToString(),"muted",11));row.Children.Add(ReferenceUi.Ellipsis(entry?.DisplayName??"Скрипт удалён","section",12).Place(0,1));
            var remove=ReferenceUi.IconButton("Убрать шаг","close",()=>{scenario.Steps.RemoveAt(index);if(scenario.Steps.Count==0)scenario.Enabled=false;SaveScenarios();refresh();});remove.Width=remove.Height=28;row.Children.Add(remove.Place(0,2));
            var card=Ui.Card(row);card.Padding=new(10,7);root.Children.Add(card);
        }
        var picker=new ComboBox{Name="ScenarioScriptPicker",ItemsSource=_library.Entries,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<ScriptEntry>((entry,_)=>ReferenceUi.Ellipsis(entry?.DisplayName??"")),PlaceholderText="Выберите скрипт…",HorizontalAlignment=HorizontalAlignment.Stretch};
        var add=Ui.WithIcon(Ui.Action("Добавить шаг",()=>{if(picker.SelectedItem is ScriptEntry entry){scenario.Steps.Add(entry.Id);SaveScenarios();refresh();}}),"plus");
        root.Children.Add(picker);root.Children.Add(add);root.Children.Add(ReferenceUi.Line());
        var run=Ui.WithIcon(Ui.Action("Запустить сейчас",()=>RunScenario(scenario),"primary"),"play");run.Name="ScenarioRun";run.IsEnabled=scenario.Steps.Count>0;
        var duplicate=Ui.WithIcon(Ui.Action("Дублировать",()=>{var copy=new AutomationScenario{Name=scenario.Name+" — копия",Description=scenario.Description,Frequency=scenario.Frequency,Time=scenario.Time,Steps=[..scenario.Steps]};Scenarios.Add(copy);_selectedScenario=copy.Id;SaveScenarios();Render();}),"copy");
        var delete=Ui.WithIcon(Command("Удалить",async()=>{if(await Dialogs.ConfirmAsync(this,"Удалить сценарий?",scenario.Name)){Scenarios.Remove(scenario);SaveScenarios();Render();}},"danger"),"trash");
        var actions=new WrapPanel{Orientation=Orientation.Horizontal};foreach(var b in new[]{run,duplicate,delete}){b.Margin=new(0,0,8,8);actions.Children.Add(b);}var editor=new Grid{RowDefinitions=new("*,Auto"),RowSpacing=16};editor.Children.Add(Ui.Scroll(root));editor.Children.Add(actions.Place(1,0));return editor;
    }
    private void RunScenario(AutomationScenario scenario)
    {
        var entries=scenario.Steps.Select(id=>_library.Entries.FirstOrDefault(e=>e.Id==id)).ToArray();
        if(entries.Any(e=>e is null)){Notify("В сценарии есть удалённые скрипты. Обновите список шагов.");return;}
        if(entries.Length==0)return;
        void Next(int index)
        {
            if(index>=entries.Length||_closed)return;
            var entry=entries[index]!;
            if(Running(entry)){Notify("Скрипт «"+entry.Name+"» уже выполняется.");return;}
            var started=DateTimeOffset.Now;OpenRun(entry);
            if(!_runs.TryGetValue(entry.Id,out var window))return;
            window.Closed+=(_,_)=>{var latest=_history.LatestFor(entry.Id);if(latest?.FinishedAt>=started&&latest.Outcome==RunOutcome.Success)Next(index+1);else if(index<entries.Length-1)Notify("Сценарий остановлен. Следующий шаг открывается после успешного выполнения предыдущего.");};
        }
        Next(0);
    }
}
