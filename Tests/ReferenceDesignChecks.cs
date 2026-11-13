using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

internal static partial class Checks
{
    private static async Task ReferenceDesignAsync(bool native)
    {
        var data=Fixture("reference-fixture");var repository=new ScriptRepository(data);
        var samples=new[]{("Сведения о системе","Windows","Характеристики компьютера и аппаратная конфигурация","Инвентаризация"),
            ("Аудит пользователей AD","Active Directory","Проверка учётных записей и корректности атрибутов","Безопасность"),
            ("Диагностика сети","Network","DNS, шлюзы, порты и доступность узлов","Диагностика"),
            ("Анализ дискового пространства","Disks","Свободное место, тома и состояние накопителей","Отчёты"),
            ("Проверка служб Windows","Services","Поиск остановленных служб и проблем запуска","Диагностика"),
            ("Проверка локальной безопасности","Security","Состояние защиты, брандмауэра и настроек","Аудит"),
            ("Очистка временных файлов","Windows","Удаление ненужных файлов и освобождение места","Обслуживание")};
        foreach(var category in samples.Select(s => s.Item2).Distinct().Where(c => !repository.Categories.Contains(c))) repository.CreateCategory(category);
        foreach(var (name,category,description,tag) in samples.Reverse())
        {
            var entry=repository.Create(name,category,description,"Скрипт собирает подробную информацию о системе. Проверьте исходный код и параметры перед выполнением.",[tag],name==samples[0].Item1,"Ready");
            repository.WriteCode(entry,"# "+name+"\n# Пример для проверки интерфейса: не меняет систему\n$computer = $env:COMPUTERNAME\nWrite-Output ('Компьютер: ' + $computer)\nGet-Date -Format 'dd.MM.yyyy HH:mm'\nWrite-Output 'Сбор данных завершён успешно.'");
        }
        var scripts=repository.LoadAll();var system=scripts.First(e=>e.Name==samples[0].Item1);
        var scenarios=new[]{new MainWindow.AutomationScenario{Name="Ежедневная диагностика рабочих станций",Description="Проверка системы, дисков и служб",Steps=scripts.Take(3).Select(e=>e.Id).ToList(),Enabled=true,LastReminder=DateTime.Now},
            new MainWindow.AutomationScenario{Name="Аудит пользователей AD",Description="Проверка учётных записей и отчёт по активности",Frequency="Еженедельно",Time="09:00",Steps=[scripts[1].Id],Enabled=true,LastReminder=DateTime.Now},
            new MainWindow.AutomationScenario{Name="Проверка безопасности",Description="Анализ уязвимостей и конфигурации",Frequency="Еженедельно",Time="10:00",Steps=[scripts[2].Id]},
            new MainWindow.AutomationScenario{Name="Очистка дискового пространства",Description="Удаление временных файлов и старых логов",Time="19:00",Steps=[scripts[3].Id]},
            new MainWindow.AutomationScenario{Name="Проверка служб Windows",Description="Контроль состояния критических служб",Time="12:00",Steps=[scripts[4].Id]}};
        File.WriteAllText(Path.Combine(data,"automation.json"),JsonSerializer.Serialize(scenarios));
        var history=new RunHistoryStore(data);for(var i=0;i<24;i++)await history.RecordAsync(new(scripts[i%scripts.Count].Id,DateTimeOffset.Now-TimeSpan.FromMinutes(i*45),TimeSpan.FromSeconds(12+i*7),i%7==3?RunOutcome.Failed:i%9==2?RunOutcome.Stopped:RunOutcome.Success,i%7==3?1:0));
        using var services=Bootstrap.CreateServices(data,_worker,installBundledScripts:false);var window=services.GetRequiredService<MainWindow>();window.Show();window.Width=1370;window.Height=990;
        await WaitUntil(()=>window.Library.Entries.Count==samples.Length,TimeSpan.FromSeconds(20));await window.Library.PendingSearch;
        var ai=services.GetRequiredService<AiAssistantViewModel>();
        void Capture(string name)
        {
            window.UpdateLayout();
            if(name=="ai-1370-light")
            {
                var fonts=window.GetVisualDescendants().OfType<TextBlock>().Where(t=>!string.IsNullOrWhiteSpace(t.Text)).Select(t=>
                {
                    var face=new Typeface(t.FontFamily,t.FontStyle,t.FontWeight,t.FontStretch).GlyphTypeface;
                    return new {t.Text,Classes=string.Join(" ",t.Classes),Family=t.FontFamily.ToString(),RequestedWeight=t.FontWeight.ToString(),ActualFamily=face.FamilyName,ActualWeight=face.Weight.ToString(),Simulations=face.FontSimulations.ToString()};
                });
                File.WriteAllText(Path.Combine(_root,"rendered-fonts.json"),JsonSerializer.Serialize(fonts,new JsonSerializerOptions{WriteIndented=true}));
            }
            if(!native){SaveFrame(window,name);return;}
            Visual top=window;while(top.GetVisualParent() is {} parent)top=parent;var size=top.Bounds.Size;var scale=window.RenderScaling;
            using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale)),new Vector(96*scale,96*scale));bitmap.Render(top);bitmap.Save(Path.Combine(_root,name+".png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        try
        {
            foreach(var section in new[]{"home","library","history","automation","settings","ai"})
            {
                window.Navigate(section);await window.Library.PendingSearch;if(section=="library")window.Library.Select(system.Id);await Task.Delay(450);Capture(section+"-1370-light");
                Check(window.GetVisualDescendants().OfType<Control>().Any(c=>c.Name==(section switch{"home"=>"HomePage","library"=>"LibraryPage","history"=>"HistoryPage","automation"=>"AutomationPage","settings"=>"SettingsPage",_=>"AiAssistantPage"})),section+" renders its separate page");
            }
            window.Navigate("library");await window.Library.PendingSearch;window.Library.Select(system.Id);await Task.Delay(100);
            Check(Named<ContentControl>(window,"ScriptDetails").GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==system.Name),"selected script populates the permanent detail panel");
            Check(Named<ListBox>(window,"ScriptCatalog").SelectedItem is ScriptEntry selected&&selected.Id==system.Id,"catalog and detail selection stay synchronized");
            var contextMenu=Named<ListBox>(window,"ScriptCatalog").GetVisualDescendants().OfType<Grid>().First(g=>g.ContextMenu is not null&&g.DataContext is ScriptEntry e&&e.Id==system.Id).ContextMenu!;
            Check(contextMenu.Items.OfType<MenuItem>().Count()==14,"catalog retains the complete script action menu");
            contextMenu.Items.OfType<MenuItem>().First(m=>m.Header as string=="Просмотреть код").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(()=>window.OwnedWindows.Any(w=>w.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t=>t.Name=="PreviewSourceText")),TimeSpan.FromSeconds(5));
            var viewer=window.OwnedWindows.Single();var source=Named<SelectableTextBlock>(viewer,"PreviewSourceText");
            Check(source.Inlines?.Text==ScriptRepository.ReadCode(system.FilePath).Replace("\r\n","\n").Replace('\r','\n'),"source viewer preserves actual script text");
            source.SelectAll();Check(source.SelectedText==source.Inlines?.Text,"syntax-highlighted source remains selectable");
            Check(!window.OwnedWindows.OfType<ScriptRunWindow>().Any(),"viewing source does not open an execution window");viewer.Close();await Task.Delay(100);
            var search=Named<TextBox>(window,"PageSearch");search.Text="нет-совпадений-щъ";await window.Library.PendingSearch;await Task.Delay(300);
            Check(window.Library.FilteredEntries.Count==0&&!Named<ContentControl>(window,"ScriptDetails").GetVisualDescendants().OfType<Button>().Any(b=>b.Name=="DetailsRun"),"empty search removes the previous launch target");
            search.Text="";await window.Library.PendingSearch;await Task.Delay(250);window.Library.Select(system.Id);await Task.Delay(100);
            Click(Named<Button>(window,"DetailsRun"));await Task.Delay(250);var run=window.OwnedWindows.OfType<ScriptRunWindow>().Single();Check(!run.ViewModel.IsRunning,"detail launch opens parameter review without executing");run.Close();
            window.Navigate("favorites");await window.Library.PendingSearch;Check(window.Library.FilteredEntries.All(e=>e.Favorite)&&window.Library.FilteredEntries.Count==1,"favorites is a separate functional navigation destination");
            window.Navigate("history");await Task.Delay(100);Check(Named<ListBox>(window,"RunCatalog").ItemCount==8,"history paginates records in groups of eight");
            Named<TextBox>(window,"HistorySearch").Text="нет-запуска";await Task.Delay(100);Check(Named<ListBox>(window,"RunCatalog").ItemCount==0,"history search clears unmatched records");Named<TextBox>(window,"HistorySearch").Text="";
            window.Navigate("automation");await Task.Delay(100);var picker=Named<ComboBox>(window,"ScenarioScriptPicker");picker.SelectedIndex=0;
            var before=JsonSerializer.Deserialize<List<MainWindow.AutomationScenario>>(File.ReadAllText(Path.Combine(data,"automation.json")))!.Sum(s=>s.Steps.Count);
            Click(window.GetVisualDescendants().OfType<Button>().First(b=>Avalonia.Automation.AutomationProperties.GetName(b)=="Добавить шаг"));await Task.Delay(100);
            var after=JsonSerializer.Deserialize<List<MainWindow.AutomationScenario>>(File.ReadAllText(Path.Combine(data,"automation.json")))!.Sum(s=>s.Steps.Count);Check(after==before+1,"automation steps persist in the scenario store");
            window.Navigate("settings");await Task.Delay(150);Named<ComboBox>(window,"SettingsTheme").SelectedIndex=0;await Task.Delay(180);Check(window.ActualThemeVariant==ThemeVariant.Dark,"theme picker applies the dark theme");
            foreach(var section in new[]{"home","library","history","automation","settings","ai"})
            {
                window.Navigate(section);await Task.Delay(180);Capture(section+"-1370-dark");
            }
            Application.Current!.RequestedThemeVariant=ThemeVariant.Light;window.Width=960;window.Height=640;await Task.Delay(200);
            foreach(var section in new[]{"home","library","history","automation","settings","ai"})
            {
                window.Navigate(section);await Task.Delay(220);Capture(section+"-960-light");
                var action=section switch{"library"=>window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>b.Name=="DetailsRun"),"ai"=>Named<Button>(window,"AiGenerate"),"automation"=>Named<Button>(window,"ScenarioRun"),_=>null};
                if(action is not null)Check(action.TranslatePoint(new Point(action.Bounds.Width,action.Bounds.Height),window) is {} end&&end.X<=window.Bounds.Width&&end.Y<=window.Bounds.Height,section+" primary action fits the minimum window size");
            }
            window.Navigate("ai");await Task.Delay(200);Check(!ai.IsModelLoaded,"viewing the redesigned AI screen does not load a model");
            Check(!window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any(b=>b.IsEffectivelyVisible),"AI environment sidebar has no visible scrollbar chrome");
            Click(Named<Button>(window,"AiExample_0"));Check(ai.Instruction.Length>10,"AI example fills the functional composer");
            Check(window.GetVisualDescendants().OfType<Button>().Any(b=>b.Name=="AiModelSettings"&&b.IsEffectivelyVisible),"AI model settings remain accessible in the environment panel");
            Check(!window.GetVisualDescendants().Any(v=>!double.IsFinite(v.Bounds.Width)||!double.IsFinite(v.Bounds.Height)),"redesigned UI bounds remain finite");
            if(native)
            {
                Visual top=window;while(top.GetVisualParent() is {} parent)top=parent;
                var captions=top.GetVisualDescendants().OfType<Button>().Where(b=>b.Name is "PART_MinimizeButton" or "PART_MaximizeButton" or "PART_CloseButton").ToArray();
                Check(captions.Length==3&&captions.All(b=>b.IsEffectivelyVisible),"all native window caption actions remain available");
                var close=captions.Single(b=>b.Name=="PART_CloseButton");Check(close.TranslatePoint(new Point(close.Bounds.Width,0),top) is {} edge&&top.Bounds.Width-edge.X<=12,"caption actions sit on the right edge of the title bar");
                Click(captions.Single(b=>b.Name=="PART_MinimizeButton"));await Task.Delay(100);Check(window.WindowState==WindowState.Minimized,"native minimize action works");window.WindowState=WindowState.Normal;
                Click(captions.Single(b=>b.Name=="PART_MaximizeButton"));await Task.Delay(150);Check(window.WindowState==WindowState.Maximized,"native maximize action works");
                Click(captions.Single(b=>b.Name=="PART_MaximizeButton"));await Task.Delay(100);Check(window.WindowState==WindowState.Normal,"native restore action works");
            }
        }
        finally{await WaitUntil(()=>!services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(60));window.Close();}
    }
}
