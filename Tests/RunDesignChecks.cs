using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SC.ScriptForge;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

internal static partial class Checks
{
    private static async Task RunDesignAsync(bool native)
    {
        var oldTheme = Application.Current!.RequestedThemeVariant;
        var oldLanguage = LibraryLocalization.Language;
        var oldMotion = Motion.Reduced;
        LibraryLocalization.Language = "ru"; Motion.Reduced = true;
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        var root = Fixture("run-form"); var repository = new ScriptRepository(root);
        var entry = repository.Create("Характеристики компьютера", "Windows", "", "", [], false, "Ready");
        repository.WriteCode(entry, """
            [CmdletBinding()]
            param(
                [string]$ComputerName = $env:COMPUTERNAME,
                [ValidateSet('ru','en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
            )
            [pscustomobject]@{
                ComputerName=$ComputerName; OperatingSystem='Windows 11 Pro'; Version='10.0.26200'
                Manufacturer='Gigabyte Technology Co., Ltd.'; Model='B760M AORUS ELITE X AX'
                SerialNumber='DEMO-0001'; Processor='Intel(R) Core(TM) — пример подробного названия процессора'
                Cores=12; LogicalProcessors=20; TotalPhysicalMemory=34359738368
                LastBootUpTime=[datetime]'2026-10-25T08:40:00'
            }
            """);
        var window = new ScriptRunWindow(entry, root, new RunHistoryStore(root), new DiagnosticLog(root));
        window.Show();
        void Capture(string name)
        {
            window.UpdateLayout();
            if (!native) { SaveFrame(window, name); return; }
            Visual top = window; while (top.GetVisualParent() is { } parent) top = parent;
            var size = top.Bounds.Size; var scale = window.RenderScaling;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(top); bitmap.Save(Path.Combine(_root, name + ".png"), PngBitmapEncoderOptions.Default);
        }
        try
        {
            await window.ViewModel.InitializeAsync(); await Task.Delay(100);
            var optional = Named<Expander>(window, "RunOptionalParameters");
            optional.IsExpanded = true; await Task.Delay(80);
            var fields = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "RunParameter").ToArray();
            Check(fields.Length == 2 && fields.All(f => !f.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "ParameterCustomEditors").IsEffectivelyVisible),
                "default parameters show summaries instead of disabled empty editors");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Environment.MachineName + " · этот компьютер")
                && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Русский"),
                "computer and report language defaults are readable without evaluating script expressions");
            Check(fields.All(f => f.TranslatePoint(default, optional) is { X: >= 13 }), "expanded parameter content has consistent horizontal padding");
            Check(!Named<Button>(window, "RunStop").IsVisible, "idle footer contains no disabled stop action");
            Check(Named<Border>(window, "RunParameters").TranslatePoint(default, window) is { } left
                && Named<ScrollViewer>(window, "RunResultScroll").TranslatePoint(default, window) is { } right
                && Math.Abs(left.Y - right.Y) < 1, "parameter and result columns start on the same baseline");
            Capture("parameters-light-1180");

            Button Customize(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ParameterCustomize" && b.Tag as string == name);
            var computer = window.ViewModel.Parameters.Single(p => p.Name == "ComputerName");
            Click(Customize("ComputerName")); await Task.Delay(60);
            var computerInput = fields.Single(f => f.Tag as string == "ComputerName").GetVisualDescendants().OfType<TextBox>().Single();
            Check(computerInput.IsEffectivelyVisible && computerInput.Text == Environment.MachineName && !computer.UseDefault, "customizing a known default opens an editable field populated with its value");
            computerInput.Text = "SERVER-01"; await Task.Delay(40);
            Check(window.ViewModel.PrepareRequest()!.Parameters.Any(p => p.Name == "ComputerName" && p.Value as string == "SERVER-01"), "custom input remains bound to the execution request");
            Click(Customize("ComputerName")); await Task.Delay(60);
            Check(computer.UseDefault && !computerInput.IsEffectivelyVisible && !window.ViewModel.PrepareRequest()!.Parameters.Any(p => p.Name == "ComputerName"), "resetting a parameter hides custom controls and restores the script default");

            Click(Customize("Language")); await Task.Delay(60);
            var language = window.ViewModel.Parameters.Single(p => p.Name == "Language");
            Check(language.Value == "ru" && !language.UseDefault, "language customization starts from the displayed default");
            language.Value = "en";
            Check(window.ViewModel.PrepareRequest()!.Language == "en", "report language override remains functional");
            Click(Customize("Language")); await Task.Delay(40);
            Capture("parameters-custom-light");

            var originalCode = ScriptRepository.ReadCode(entry.FilePath);
            var marker = Path.Combine(root, "inspection-must-not-run.txt");
            repository.WriteCode(entry, "param([string]$Example)\nSet-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value 'executed'\n$($if($true){'да'}{'нет'})\n");
            await window.ViewModel.ReloadAsync(); await Task.Delay(80);
            Check(!window.ViewModel.CanRun && window.ViewModel.Parameters.Count == 0 && !window.ViewModel.CanBypassOnce,
                "a parser failure disables execution and clears stale parameter fields");
            Check(window.ViewModel.Error.Contains("Строка 3, столбец") && window.ViewModel.Error.Contains("$if"),
                "preparation failures retain the parser location, message and offending code");
            Check(!window.ViewModel.Error.Contains("CLIXML") && !window.ViewModel.Error.Contains("<Objs"), "inspection diagnostics contain no serialized PowerShell progress records");
            Check(Named<Border>(window, "RunError").IsEffectivelyVisible && Named<Button>(window, "RunCopyError").IsEffectivelyVisible
                && Named<SelectableTextBlock>(window, "RunErrorText").Text == window.ViewModel.Error && !Named<ContentControl>(window, "RunResults").IsVisible,
                "the blocked run shows a selectable error and copy action without a ready-to-run banner");
            Check(!File.Exists(marker), "parameter inspection never executes the invalid script");
            Capture("blocked-syntax-light-1180");
            window.Width = 900; window.Height = 640; Application.Current.RequestedThemeVariant = ThemeVariant.Dark; await Task.Delay(80);
            Check(Named<Border>(window, "RunError").Bounds.Width <= Named<ScrollViewer>(window, "RunResultScroll").Bounds.Width
                && Named<Button>(window, "RunCopyError").TranslatePoint(default, Named<ScrollViewer>(window, "RunResultScroll")) is { Y: >= 0 } copyPoint
                && copyPoint.Y + Named<Button>(window, "RunCopyError").Bounds.Height <= Named<ScrollViewer>(window, "RunResultScroll").Bounds.Height,
                "error details fit the minimum window width and copying is accessible without scrolling");
            Capture("blocked-syntax-dark-900");
            repository.WriteCode(entry, originalCode); await window.ViewModel.ReloadAsync(); await Task.Delay(80);
            Check(window.ViewModel.CanRun && !window.ViewModel.HasError && !Named<Border>(window, "RunError").IsVisible && window.ViewModel.Parameters.Count == 2,
                "reloading corrected code clears the error and restores execution and parameters");
            using (var missing = new ScriptRunViewModel(new ScriptEntry { Name = "Missing script", FilePath = entry.FilePath + ".missing" }, root))
            {
                await missing.InitializeAsync();
                Check(!missing.CanRun && missing.Error.Contains("Файл скрипта не найден"), "a missing file retains its concrete preparation failure");
            }
            window.Width = 1180; window.Height = 840; Application.Current.RequestedThemeVariant = ThemeVariant.Light;

            File.AppendAllText(entry.FilePath, "\n# Changed after inspection\n");
            await window.ViewModel.RunAsync(); await Task.Delay(80);
            Check(window.ViewModel.Error.Contains("Скрипт изменился. Обновите параметры") && Named<Border>(window, "RunError").IsEffectivelyVisible,
                "a rejected launch retains the specific changed-file failure instead of generic advice");
            await window.ViewModel.ReloadAsync();
            await window.ViewModel.RunAsync(); await Task.Delay(100);
            Check(window.ViewModel.Result.State == SC.ScriptForge.Presentation.RunState.Success && Named<Button>(window, "RunCopyReport").IsVisible, "successful execution still exposes the report action");
            Capture("result-light-1180");
            bool Fits(Control control) => control.TranslatePoint(default, window) is { } p && p.X >= 0 && p.Y >= 0
                && p.X + control.Bounds.Width <= window.Bounds.Width + 1 && p.Y + control.Bounds.Height <= window.Bounds.Height + 1;
            window.Width = 900; window.Height = 640; await Task.Delay(100);
            Check(Fits(Named<Button>(window, "RunStart")) && Fits(Named<Grid>(window, "RunFooter")) && Fits(Named<TextBlock>(window, "RunTitle")), "title and primary actions fit the minimum run-window dimensions");
            Check(window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.Name == "ResultFieldValue").All(t =>
                t.TranslatePoint(default, window) is { } p && p.X + t.Bounds.Width <= window.Bounds.Width + 1), "result values remain within the right edge at minimum width");
            Capture("result-light-900");
            var engine = Named<Expander>(window, "RunEngineSettings"); engine.IsExpanded = true; await Task.Delay(80);
            Check(engine.Content is Border { Padding.Left: 14, Padding.Top: 12 }, "engine settings use the same padded section layout");
            Capture("engine-settings-light-900");
            engine.IsExpanded = false;
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark; await Task.Delay(80);
            Capture("result-dark-900");
            window.Width = 1180; window.Height = 840; await Task.Delay(80); Capture("result-dark-1180");
            var secret = new ScriptParameterViewModel(new() { Name = "Password", HasDefault = true, LiteralDefault = "must-not-display" });
            Check(!secret.DefaultSummary.Contains("must-not-display"), "default summaries do not expose secret values");
            repository.WriteCode(entry, "throw 'Подробная причина отказа запуска'\n");
            await window.ViewModel.ReloadAsync(); await window.ViewModel.RunAsync(); await Task.Delay(80);
            Check(window.ViewModel.Error.Contains("Подробная причина отказа запуска") && window.ViewModel.Result.State == SC.ScriptForge.Presentation.RunState.Failed
                && Named<Border>(window, "RunError").IsEffectivelyVisible, "runtime failures expose the actual error above the result and diagnostic log");
            Capture("blocked-runtime-dark-1180");
        }
        finally { window.Close(); Application.Current.RequestedThemeVariant = oldTheme; LibraryLocalization.Language = oldLanguage; Motion.Reduced = oldMotion; }
    }
}
