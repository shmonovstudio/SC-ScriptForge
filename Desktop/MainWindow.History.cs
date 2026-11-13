using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SC.ScriptForge.Models;

namespace SC.ScriptForge;

public sealed partial class MainWindow
{
    private string _historyQuery = "", _historyFilter = "all";
    private int _historyPage;
    private ScriptRunRecord? _selectedRun;
    private Control HistoryView()
    {
        var root = new Grid { Name="HistoryPage", RowDefinitions=new("Auto,Auto,*,Auto,230"), RowSpacing=18, Margin=new(24,28,24,20) };
        root.Children.Add(ReferenceUi.Heading("Контроль и результаты выполнения","Runs & Logs","История запусков скриптов, результаты выполнения и журналы",
            Ui.WithIcon(Command("Экспорт", ExportHistoryAsync),"export")));
        var search=Ui.Input(_historyQuery,"Поиск по названию скрипта или результату…"); search.Name="HistorySearch"; search.MinHeight=40; search.Padding=new(38,10,12,10);
        var searchGrid=new Grid(); searchGrid.Children.Add(search); var searchIcon=Ui.Icon("search",15); searchIcon.HorizontalAlignment=HorizontalAlignment.Left;searchIcon.Margin=new(13,0);searchGrid.Children.Add(searchIcon);
        var filters=Ui.Row(); filters.Spacing=6;
        var content=new Grid { RowDefinitions=new("36,*") };
        var footer=new Grid { ColumnDefinitions=new("*,Auto") };
        var details=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch };
        var list=new ListBox {Name="RunCatalog",Classes={"referenceList","runList"},ItemTemplate=new FuncDataTemplate<ScriptRunRecord>((r,_)=>r is null?new Border():RunRow(r))};
        list.SelectionChanged+=(_,_)=> { _selectedRun=list.SelectedItem as ScriptRunRecord; details.Content=RunDetails(_selectedRun); };
        var rowsHeader=RunColumns(); string[] labels=["", "Скрипт / Задача", "Статус", "Время запуска", "Длительность", "Компьютер", "Результат"];
        for(var i=0;i<labels.Length;i++) rowsHeader.Children.Add(Ui.Text(labels[i],"muted",10.5).Place(0,i));
        var headBorder=new Border {Child=rowsHeader,Padding=new(12,0),BorderThickness=new(0,1,0,1)};Ui.Resource(headBorder,Border.BackgroundProperty,"ScQuiet");Ui.Resource(headBorder,Border.BorderBrushProperty,"ScLine");
        content.Children.Add(headBorder);content.Children.Add(list.Place(1,0));
        void Update()
        {
            var records=_history.Recent.Where(r=>(_historyFilter switch {"success"=>r.Outcome==RunOutcome.Success,"failed"=>r.Outcome==RunOutcome.Failed,"stopped"=>r.Outcome==RunOutcome.Stopped,_=>true})
                && (RunName(r)+" "+r.Label).Contains(_historyQuery,StringComparison.CurrentCultureIgnoreCase)).ToArray();
            var pages=Math.Max(1,(records.Length+7)/8);_historyPage=Math.Clamp(_historyPage,0,pages-1);
            var items=records.Skip(_historyPage*8).Take(8).ToArray();list.ItemsSource=items;list.SelectedItem=items.FirstOrDefault(r=>r==_selectedRun)??items.FirstOrDefault();
            if(items.Length==0) details.Content=RunDetails(null);
            footer.Children.Clear();footer.Children.Add(Ui.Text($"Показано {items.Length} из {records.Length} запусков","muted",11.5));
            var pagination=Ui.Row();
            pagination.Children.Add(ReferenceUi.IconButton("Предыдущая страница","chevron",()=>{_historyPage--;Update();}));pagination.Children[0].RenderTransform=new RotateTransform(180);pagination.Children[0].IsEnabled=_historyPage>0;
            for(var p=0;p<pages;p++){var index=p;var button=Ui.Action((p+1).ToString(),()=>{_historyPage=index;Update();},"tab");button.Classes.Set("active",p==_historyPage);pagination.Children.Add(button);}
            var next=ReferenceUi.IconButton("Следующая страница","chevron",()=>{_historyPage++;Update();});next.IsEnabled=_historyPage<pages-1;pagination.Children.Add(next);footer.Children.Add(pagination.Place(0,1));
            foreach(var b in filters.Children.OfType<Button>())b.Classes.Set("active",(string?)b.Tag==_historyFilter);
        }
        foreach(var (id,label,count) in new[]{("all","Все",_history.Recent.Count),("success","Успешные",_history.Recent.Count(r=>r.Outcome==RunOutcome.Success)),("failed","Ошибки",_history.Recent.Count(r=>r.Outcome==RunOutcome.Failed)),("stopped","Остановлены",_history.Recent.Count(r=>r.Outcome==RunOutcome.Stopped))})
        {var button=Ui.Action(label,()=>{_historyFilter=id;_historyPage=0;Update();},"tab");button.Tag=id;button.Content=Ui.Row(Ui.Text(label,size:11),Ui.Text(count.ToString(),"muted",10));filters.Children.Add(button);}
        search.TextChanged+=(_,_)=>{_historyQuery=search.Text??"";_historyPage=0;Update();};
        var toolbar=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=10};toolbar.Children.Add(searchGrid);toolbar.Children.Add(filters.Place(0,1));
        root.Children.Add(toolbar.Place(1,0));root.Children.Add(content.Place(2,0));root.Children.Add(footer.Place(3,0));root.Children.Add(details.Place(4,0));Update();return root;
    }
    private Grid RunColumns()=>new(){ColumnDefinitions=new(_compact?"24,2*,100,115,76,0,1*":"24,2*,110,130,90,115,1.5*"),ColumnSpacing=10};
    private string RunName(ScriptRunRecord record)=>_library.Entries.FirstOrDefault(e=>e.Id==record.ScriptId)?.DisplayName??"Скрипт удалён";
    private Control RunRow(ScriptRunRecord record)
    {
        var row=RunColumns();row.MinHeight=27;
        row.Children.Add(Ui.Icon("terminal",17));
        var copy=Ui.Stack(ReferenceUi.Ellipsis(RunName(record),"section",12),ReferenceUi.Ellipsis(_library.Entries.FirstOrDefault(e=>e.Id==record.ScriptId)?.DisplayDescription??record.ScriptId.ToString(),"muted",10.5));copy.Spacing=3;row.Children.Add(copy.Place(0,1));
        row.Children.Add(Feedback.Chip(record.Label,record.Outcome==RunOutcome.Success?Tone.Success:record.Outcome==RunOutcome.Failed?Tone.Danger:Tone.Neutral).Place(0,2));
        row.Children.Add(Ui.Text((record.FinishedAt-record.Duration).LocalDateTime.ToString("dd.MM.yyyy HH:mm"),"muted",10.5).Place(0,3));
        row.Children.Add(Ui.Text(record.Duration.ToString(@"hh\:mm\:ss"),"muted",11).Place(0,4));
        var machine=ReferenceUi.Ellipsis("Локально","muted",10.5);machine.IsVisible=!_compact;row.Children.Add(machine.Place(0,5));
        row.Children.Add(ReferenceUi.Ellipsis(record.ExitCode is {} code?"Код завершения: "+code:"Остановлен пользователем","muted",10.5).Place(0,6));return row;
    }
    private Control RunDetails(ScriptRunRecord? record)
    {
        var root=new Grid {RowDefinitions=new("36,*"),RowSpacing=6};
        var header=new Grid{ColumnDefinitions=new("*,Auto")};header.Children.Add(Ui.Row(Ui.Icon("terminal",16),Ui.Text("Информация о запуске","section",11.5)));
        if(record is not null)
        {
            var copy=Ui.Button("Копировать сведения",async()=>{if(Clipboard is {} clipboard)await clipboard.SetTextAsync(RunSummary(record));},"ghost iconAction");copy.Content=Ui.Icon("copy",15);header.Children.Add(Ui.Row(Feedback.Chip(record.Label,record.Outcome==RunOutcome.Success?Tone.Success:Tone.Neutral),Ui.Text(record.DurationLabel,"muted",11),copy).Place(0,1));
        }
        root.Children.Add(header);
        var text=new SelectableTextBlock {Text=record is null?"История пока пуста.\nЗапустите скрипт из каталога — здесь появится результат.":RunSummary(record),FontFamily=new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas"),FontSize=11,LineHeight=21,Foreground=Brush.Parse("#B7CADB"),Margin=new(18,12),TextWrapping=TextWrapping.Wrap};
        var console=new Border{Child=Ui.Scroll(text),Background=Brush.Parse("#172531"),CornerRadius=new(9)};root.Children.Add(console.Place(1,0));
        var panel=new Border{Child=root,Padding=new(8,2,8,8),BorderThickness=new(1),CornerRadius=new(10)};Ui.Resource(panel,Border.BorderBrushProperty,"ScBorder");return panel;
    }
    private string RunSummary(ScriptRunRecord r)=>$"01  Скрипт       : {RunName(r)}\n02  Начало       : {(r.FinishedAt-r.Duration).LocalDateTime:dd.MM.yyyy HH:mm:ss}\n03  Завершение   : {r.FinishedAt.LocalDateTime:dd.MM.yyyy HH:mm:ss}\n04  Длительность : {r.DurationLabel}\n05  Результат    : {r.Label} · код {r.ExitCode?.ToString()??"—"}\n06  Вывод скрипта доступен в окне выполнения и не сохраняется в истории.";
    private async Task ExportHistoryAsync()
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){SuggestedFileName="ScriptForge-runs.json",DefaultExtension="json"});
        if(file is not null){await using var stream=await file.OpenWriteAsync();await System.Text.Json.JsonSerializer.SerializeAsync(stream,_history.Recent);Notify("История экспортирована.");}
    }
}
