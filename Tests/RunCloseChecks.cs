using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

internal static partial class Checks
{
    private static async Task RunCloseAsync(bool native)
    {
        var root = Fixture("close-run");
        var repository = new ScriptRepository(root);
        for (var index = 0; index < 36; index++)
        {
            var entry = repository.Create($"Проверка {index:00}", index % 2 == 0 ? "Windows" : "Network", "Проверка сохранения списка после закрытия скрипта", "", [], index % 3 == 0, "Ready");
            repository.WriteCode(entry, "param()\n[pscustomobject]@{Name='fixture'; Status='OK'}\n");
        }
        new PreferencesStore(root).Save(new() { ReduceMotion = false, Theme = "Light", DesignVersion = 2 });
        using var services = Bootstrap.CreateServices(root, _worker, installBundledScripts: false);
        var window = services.GetRequiredService<MainWindow>();
        window.Show(); window.Width = 1370; window.Height = 990;
        try
        {
            await WaitUntil(() => window.Library.Entries.Count == 36, TimeSpan.FromSeconds(20));
            var workspace = Named<TransitioningContentControl>(window, "Workspace");
            bool VisibleContent() => workspace.Content is Control page && page.IsAttachedToVisualTree() && page.Bounds.Width > 0
                && page.GetVisualAncestors().Prepend(page).All(v => v.IsVisible && v.Opacity > .99);
            void Capture(string name)
            {
                window.UpdateLayout();
                if (!native) { SaveFrame(window, name); return; }
                Visual top = window; while (top.GetVisualParent() is { } parent) top = parent;
                var size = top.Bounds.Size; var scale = window.RenderScaling;
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
                bitmap.Render(top); bitmap.Save(Path.Combine(_root, name + ".png"), PngBitmapEncoderOptions.Default);
            }
            foreach (var section in new[] { "library", "favorites", "myscripts" })
            {
                window.Navigate(section);
                window.Library.Query = "Проверка"; window.Library.Category = "Windows"; window.Library.Status = "Ready"; window.Library.Sort = LibrarySort.Name;
                await window.Library.PendingSearch; await Task.Delay(450);
                var id = window.Library.FilteredEntries.First().Id; window.Library.Select(id);
                await Task.Delay(100);
                var page = workspace.Content;
                var list = Named<ListBox>(window, "ScriptCatalog");
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, Math.Min(160, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
                await Task.Delay(50);
                var offset = scroll.Offset;
                var count = list.ItemCount;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    Click(Named<Button>(window, "DetailsRun"));
                    await Task.Delay(80);
                    var run = window.OwnedWindows.OfType<ScriptRunWindow>().Single();
                    Check(!run.ViewModel.IsRunning, section + ": opening the script does not execute it");
                    run.Close();
                    await Task.Delay(450);
                }
                Check(ReferenceEquals(workspace.Content, page) && ReferenceEquals(Named<ListBox>(window, "ScriptCatalog"), list), section + ": closing a script preserves the existing catalog and scroll container");
                Check(VisibleContent() && list.ItemCount == count && count > 0, section + ": catalog remains visible after repeated open/close");
                Check(window.Library.SelectedEntry?.Id == id && window.Library.Query == "Проверка" && window.Library.Category == "Windows"
                    && window.Library.Status == "Ready" && window.Library.Sort == LibrarySort.Name && scroll.Offset == offset, section + ": selection, search, filters, sorting and scroll position survive closing a script");
                File.WriteAllLines(Path.Combine(_root, section + "-presenters.txt"), workspace.GetVisualDescendants().OfType<ContentPresenter>().Select(p => p.Name + " visible=" + p.IsVisible + " opacity=" + p.Opacity + " content=" + p.Content?.GetType().Name));
                Capture(section + "-after-close");
            }
            window.Navigate("library"); await window.Library.PendingSearch; await Task.Delay(450);
            var savedPage = workspace.Content;
            Click(Named<Button>(window, "DetailsRun")); await Task.Delay(100);
            var executed = window.OwnedWindows.OfType<ScriptRunWindow>().Single();
            await executed.ViewModel.InitializeAsync(); await executed.ViewModel.RunAsync(); executed.Close(); await Task.Delay(450);
            Check(ReferenceEquals(workspace.Content, savedPage) && VisibleContent(), "closing a completed run keeps the catalog attached and visible");
            window.Navigate("history"); await Task.Delay(450);
            var previous = Named<ListBox>(window, "RunCatalog").ItemCount;
            // Open via the real Ctrl+Enter shortcut while the history page is behind the run window.
            window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
            await Task.Delay(100);
            var historyRun = window.OwnedWindows.OfType<ScriptRunWindow>().Single();
            await historyRun.ViewModel.InitializeAsync(); await historyRun.ViewModel.RunAsync(); historyRun.Close(); await Task.Delay(450);
            Check(VisibleContent() && Named<ListBox>(window, "RunCatalog").ItemCount == previous + 1, "closing a completed run refreshes visible history without a blank workspace");
            Capture("history-after-close");

            // A background refresh can replace content before a navigation animation completes.
            window.Navigate("settings"); await Task.Delay(40);
            Named<ComboBox>(window, "SettingsTheme").SelectedIndex = 0; await Task.Delay(450);
            Check(VisibleContent(), "a non-navigation refresh during a page transition keeps the workspace visible");
        }
        finally
        {
            foreach (var run in window.OwnedWindows.OfType<ScriptRunWindow>().ToArray()) { run.ViewModel.Stop(); run.Close(); }
            await WaitUntil(() => !services.GetRequiredService<ComputerViewModel>().IsBusy, TimeSpan.FromSeconds(30));
            window.Close();
        }
    }
}
