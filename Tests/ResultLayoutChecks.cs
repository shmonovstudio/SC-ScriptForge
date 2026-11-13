using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SC.ScriptForge;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;

internal static partial class Checks
{
    private static async Task ResultLayoutAsync()
    {
        var oldTheme = Application.Current!.RequestedThemeVariant;
        var oldLanguage = LibraryLocalization.Language;
        var oldMotion = Motion.Reduced;
        Motion.Reduced = true;
        LibraryLocalization.Language = "ru";
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        var longPath = @"C:\Отчёты\Проверка оборудования\" + new string('я', 160) + ".csv";
        ScriptResultField[] fields =
        [
            new("ComputerName", "PC-DEMO"),
            new("OperatingSystem", "Майкрософт Windows 11 Pro"),
            new("Version", "10.0.26200"),
            new("Manufacturer", "Gigabyte Technology Co., Ltd."),
            new("Model", "B760M AORUS ELITE X AX"),
            new("Processor", "Intel(R) Core(TM) — пример длинного названия процессора"),
            new("Cores", "12"),
            new("LogicalProcessors", "20"),
            new("TotalPhysicalMemory", "34359738368"),
            new("LastBootUpTime", "25.10.2026 08:40"),
            new("FullName", longPath)
        ];
        var result = new RunResultPresentation("ru"); result.Reset(_root);
        result.Accept(new("fixture") { Kind = ScriptOutputKind.Data, Fields = fields }); result.Complete(RunOutcome.Success);
        var report = result.Report();
        var view = new ResultDataView(result.Tables.Single());
        var holder = new ContentControl { Content = view, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(20) };
        var window = new Window { Width = 800, Height = 1000, Content = Ui.Scroll(holder) };
        window.Show(); await Task.Delay(100);
        try
        {
            Check(view.UsesCards, "eleven-field computer reports use vertical cards");
            var values = view.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.Name == "ResultFieldValue").ToArray();
            Check(values.Length == fields.Length && values.Any(t => t.Text == longPath) && values.Any(t => t.Text == "32 ГиБ"), "all fields, long Unicode paths and formatted units remain intact");
            bool Fits(ResultDataView panel) => panel.GetVisualDescendants().OfType<SelectableTextBlock>().All(t =>
                t.TranslatePoint(default, panel) is { } origin && origin.X >= 0 && origin.X + t.Bounds.Width <= panel.Bounds.Width + 1 && t.TextWrapping == TextWrapping.Wrap);
            Check(Fits(view), "wide record values stay within the available width");
            SaveFrame(window, "computer-record-light");
            window.Width = 380; await Task.Delay(100);
            Check(view.UsesCards && Fits(view), "vertical fields and unbroken values fit a narrow result surface");
            Check(values.Single(t => t.Text == longPath).Bounds.Height > 30, "long unbroken paths wrap onto multiple lines");
            SaveFrame(window, "computer-record-narrow");
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark; await Task.Delay(100);
            SaveFrame(window, "computer-record-dark");

            var four = new ResultTable(fields.Take(4).ToArray(), 1, "en");
            four.Add(fields.Take(4).ToArray());
            var compact = new ResultDataView(four); holder.Content = compact;
            window.Width = 800; await Task.Delay(100);
            Check(!compact.UsesCards && Fits(compact), "four-field records remain fully visible in a compact table");
            SaveFrame(window, "four-fields-table");
            window.Width = 380; await Task.Delay(100);
            Check(compact.UsesCards && Fits(compact), "short tables switch to cards when the viewport becomes narrow");
            window.Width = 800; await Task.Delay(100);
            Check(!compact.UsesCards, "widening the viewport restores the compact table");

            var five = new ResultTable(fields.Take(5).ToArray(), 1, "en");
            for (var row = 0; row < 3; row++)
            {
                var record = fields.Take(5).ToArray(); record[0] = new("ComputerName", "PC-" + (row + 1)); five.Add(record);
            }
            var multiple = new ResultDataView(five); holder.Content = multiple; await Task.Delay(100);
            var records = multiple.GetVisualDescendants().OfType<Expander>().Where(e => e.Name == "ResultRecord").ToArray();
            Check(multiple.UsesCards && records.Length == 3 && records[0].IsExpanded && records.Skip(1).All(e => e.Content is null), "five fields cross the card threshold; additional records load their fields on demand");
            records[2].IsExpanded = true; await Task.Delay(100);
            Check(records[2].GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text == "PC-3") && Fits(multiple), "expanding another record shows its own complete field values");
            SaveFrame(window, "multiple-records-dark");
            Check(result.Report() == report, "switching result layouts leaves the copied report and original values unchanged");
        }
        finally { window.Close(); Application.Current.RequestedThemeVariant = oldTheme; LibraryLocalization.Language = oldLanguage; Motion.Reduced = oldMotion; }

        // Exercise the actual run window as well, so the new view is covered through script output.
        var root = Fixture("result-window");
        var repository = new ScriptRepository(root);
        var entry = repository.Create("Пример оформления результата", "Windows", "", "", [], false, "Ready");
        var code = "[pscustomobject]@{\n" + string.Join("\n", fields.Select(f => f.Name + " = '" + f.Value.Replace("'", "''") + "'")) + "\n}";
        repository.WriteCode(entry, code);
        var log = new DiagnosticLog(root);
        var run = new ScriptRunWindow(entry, root, new RunHistoryStore(root), log);
        run.Show();
        try
        {
            await run.ViewModel.InitializeAsync(); await run.ViewModel.RunAsync(); await Task.Delay(100);
            var panel = run.GetVisualDescendants().OfType<ResultDataView>().Single();
            Check(run.ViewModel.Result.State == RunState.Success && panel.UsesCards, "the actual script run window renders wide PowerShell output as cards");
            run.Width = 900; run.Height = 800; await Task.Delay(100);
            Check(panel.GetVisualDescendants().OfType<SelectableTextBlock>().Count(t => t.Name == "ResultFieldValue") == fields.Length, "minimum run-window width retains every result field");
            SaveFrame(run, "run-window-900");
        }
        finally { run.Close(); }
    }
}
