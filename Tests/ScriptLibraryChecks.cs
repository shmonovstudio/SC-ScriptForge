using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Models;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

internal static partial class Checks
{
    private static async Task ScriptLibraryAsync()
    {
        var previousLanguage = LibraryLocalization.Language;
        try
        {
            var root = Fixture("bundled-library");
            var repository = new ScriptRepository(root);
            repository.InstallBundledScripts();
            var entries = repository.LoadAll();
            var builtins = entries.Where(e => e.Translations.ContainsKey("en")).ToArray();
            Check(entries.Count == 22 && builtins.Length == 18 && entries.Select(e => e.Id).Distinct().Count() == 22, "all 18 priority tools are installed with stable metadata");
            Check(builtins.All(e => e.Status == "Ready" && e.Translations["ru"].Name == e.Name && !string.IsNullOrWhiteSpace(e.Translations["en"].Notes)), "every priority tool has complete RU/EN cards and requirements");
            var resources = typeof(ScriptRepository).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("SC.ScriptForge.Library/", StringComparison.Ordinal)).ToArray();
            Check(resources.Count(n => n.EndsWith(".ps1", StringComparison.Ordinal)) == 22 && resources.Length == 44, "release allowlist embeds exactly 22 scripts and their metadata");
            Check(resources.All(n => !n.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase) && !n.Contains("Очистка", StringComparison.OrdinalIgnoreCase)), "AnyDesk and data cleanup scripts are excluded from release resources");
            Check(entries.Any(e => e.FileName == "System-Info.ps1") && ScriptRepository.ReadCode(entries.Single(e => e.FileName == "System-Inventory.ps1").FilePath).Contains("Win32_DesktopMonitor", StringComparison.Ordinal), "legacy system summary and corrected inventory are installed");
            var target = builtins.First(e => e.FileName == "PC-Uptime.ps1");
            LibraryLocalization.Language = "en";
            Check(target.DisplayName == "Computer uptime" && target.DisplayNotes.StartsWith("Read only."), "English cards select translated fields");
            var search = new LibrarySearch();
            Check((await search.FindAsync(entries, new("Время работы после загрузки", null, null, false, LibrarySort.Name), default)).Any(e => e.Id == target.Id), "Russian titles remain searchable in English mode");
            LibraryLocalization.Language = "ru";
            Check((await search.FindAsync(entries, new("Computer uptime", null, null, false, LibrarySort.Name), default)).Any(e => e.Id == target.Id), "English titles remain searchable in Russian mode");
            var preferences = new PreferencesStore(root);
            preferences.Save(new() { LibraryLanguage = "en" });
            Check(preferences.Load().LibraryLanguage == "en", "library language persists across restarts");
            var original = ScriptRepository.ReadCode(target.FilePath);
            repository.WriteCode(target, original + "\n# user edit\n");
            repository.EditMetadata(target, "My custom uptime", target.Category, target.Description, target.Notes, target.Tags, true, "Ready");
            repository.InstallBundledScripts();
            LibraryLocalization.Language = "en";
            var edited = repository.LoadAll().Single(e => e.Id == target.Id);
            Check(edited.DisplayName == "My custom uptime" && edited.Favorite && ScriptRepository.ReadCode(edited.FilePath).EndsWith("# user edit\n"), "installing an update preserves edited code, titles and favorites");
            repository.MoveToTrash(edited); repository.InstallBundledScripts();
            Check(repository.LoadAll().Count == 21 && repository.GetTrash().Count == 1, "deleted bundled scripts are not recreated on startup");
            repository.RestoreFromTrash(repository.GetTrash().Single());

            var report = new RunResultPresentation("en"); report.Reset(root);
            report.Accept(new("row") { Kind = ScriptOutputKind.Data, Fields = [new("ComputerName", "TEST"), new("FreeSpace", "1073741824"), new("IsInherited", "Yes")] });
            report.Complete(RunOutcome.Success);
            LibraryLocalization.Language = "ru";
            Check(report.Report().Contains("Computer\tFree space\tInherited") && report.Report().Contains("1 GiB") && report.Title == "Task completed", "report headers, units and language remain consistent when settings change");
            var empty = new RunResultPresentation("ru"); empty.Reset(root); empty.Complete(RunOutcome.Success);
            Check(empty.ReceivedCount == 0 && empty.Description.Contains("не вернул данные"), "empty successful output is shown honestly");
            var caption = new ScriptParameterViewModel(new() { Name = "StorePath", Description = "RU: Хранилище\nEN: Store" }, "en");
            Check(caption.Role == ParameterRole.Text && !caption.CanBrowse && caption.Label == "Certificate store" && caption.Description == "Store", "certificate provider paths and bilingual parameter help are handled correctly");

            var engine = PowerShellHost.InstalledEngines().First();
            var inspector = new ScriptParameterService();
            foreach (var entry in builtins)
            {
                var definition = await inspector.InspectAsync(entry.FilePath, engine);
                Check(definition.Parameters.Any(p => p.Name == "Language" && p.Choices.SequenceEqual(new[] { "ru", "en" })), entry.FileName + ": parameters parse in Windows PowerShell 5.1");
            }
            var fixtures = Path.Combine(root, "fixture files"); Directory.CreateDirectory(Path.Combine(fixtures, "вложенная папка"));
            File.WriteAllBytes(Path.Combine(fixtures, "большой файл.bin"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(fixtures, "вложенная папка", "small.bin"), new byte[1024]);
            var hostFile = Path.Combine(root, "hosts.txt"); File.WriteAllText(hostFile, "# loopback\n127.0.0.1\n127.0.0.1\n", new UTF8Encoding(true));
            var execution = new ScriptExecutionService(root);
            async Task<Output> Run(string fileName, params BoundScriptParameter[] args)
            {
                var entry = builtins.Single(e => e.FileName == fileName);
                var output = new Output();
                var result = await execution.RunAsync(new(entry.FilePath, ScriptParameterService.Fingerprint(entry.FilePath), engine, args, []) { Language = "en" }, output);
                Check(result.ExitCode == 0 && !result.ExecutionPolicyBlocked && !output.Lines.Any(l => l.IsError), fileName + ": actual local run through the structured-output wrapper");
                var resultReport = new RunResultPresentation("en"); resultReport.Reset(root);
                foreach (var line in output.Lines) resultReport.Accept(line);
                resultReport.Complete(result.ExitCode == 0 ? RunOutcome.Success : RunOutcome.Failed);
                File.WriteAllText(Path.Combine(root, fileName + ".report.txt"), resultReport.Report());
                return output;
            }
            foreach (var name in new[] { "PC-System-Info.ps1", "PC-Disk-Space.ps1", "PC-Uptime.ps1" })
            {
                if (name == "PC-Uptime.ps1") { File.WriteAllText(target.FilePath, original, new UTF8Encoding(true)); }
                var output = await Run(name);
                Check(output.Lines.Any(l => l.Kind == ScriptOutputKind.Data), name + ": returns real computer data");
            }
            var largest = await Run("FS-Largest-Files.ps1", new BoundScriptParameter("FolderPath", "System.String", fixtures), new("Top", "System.Int32", 1));
            Check(largest.Lines.Single(l => l.Kind == ScriptOutputKind.Data).Fields.Any(f => f.Name == "Length" && f.Value == "4096"), "largest-file filtering retains exact byte counts and Unicode paths");
            var folders = await Run("FS-Folder-Sizes.ps1", new BoundScriptParameter("FolderPath", "System.String", fixtures));
            Check(folders.Lines.Single(l => l.Kind == ScriptOutputKind.Data).Fields.Any(f => f.Name == "Size" && f.Value == "1024"), "folder-size report includes nested file content");
            var acl = await Run("FS-Ntfs-Permissions.ps1", new BoundScriptParameter("FolderPath", "System.String", fixtures));
            Check(acl.Lines.Any(l => l.Fields.Any(f => f.Name == "IsInherited" && f.Value is "Yes" or "No")), "NTFS rules preserve inherited flags and translated booleans");
            var ping = await Run("NET-Ping-List.ps1", new BoundScriptParameter("InputFile", "System.String", hostFile));
            Check(ping.Lines.Count(l => l.Kind == ScriptOutputKind.Data) == 1, "ping input skips comments and duplicate targets");
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var ports = await Run("NET-Tcp-Ports.ps1", new("ComputerName", "System.String", "127.0.0.1"), new("Ports", "System.String", port.ToString()));
            Check(ports.Lines.Any(l => l.Fields.Any(f => f.Name == "Status" && f.Value == "Open")), "TCP test detects a real open loopback port");
            listener.Stop();
            var closed = await Run("NET-Tcp-Ports.ps1", new("ComputerName", "System.String", "127.0.0.1"), new("Ports", "System.String", port.ToString()));
            Check(closed.Lines.Any(l => l.Fields.Any(f => f.Name == "Status" && f.Value is "Unavailable" or "Timed out")), "TCP test shows a closed or filtered port without failing the audit");
            await Run("SEC-Certificate-Expiry.ps1", new BoundScriptParameter("StorePath", "System.String", "Cert:\\CurrentUser\\My"));

            using var services = Bootstrap.CreateServices(Fixture("language-ui"), _worker);
            var window = services.GetRequiredService<MainWindow>(); window.Show();
            try
            {
                await WaitUntil(() => window.Library.Entries.Count == 22, TimeSpan.FromSeconds(20));
                window.Navigate("settings");
                await Task.Delay(450);
                Named<ComboBox>(window, "SettingsLibraryLanguage").SelectedIndex = 1;
                await Task.Delay(300);
                window.Navigate("library"); await window.Library.PendingSearch; await Task.Delay(450);
                var english = window.Library.Entries.First(e => e.FileName == "AD-Name-Quality.ps1"); window.Library.Select(english.Id);
                Check(Named<ContentControl>(window, "ScriptDetails").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Check employee names in AD"), "settings picker switches visible library cards to English");
                SaveFrame(window, "priority-library-en");
                window.Navigate("settings"); await Task.Delay(450); Named<ComboBox>(window, "SettingsLibraryLanguage").SelectedIndex = 0;
                await Task.Delay(300); window.Navigate("library"); await window.Library.PendingSearch; await Task.Delay(450); window.Library.Select(english.Id);
                Check(Named<ContentControl>(window, "ScriptDetails").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Проверка ФИО сотрудников в AD"), "settings picker switches visible library cards back to Russian");
                SaveFrame(window, "priority-library-ru");
            }
            finally { window.Close(); }
            Results.Add("OBSERVATION AD, remote hosts, DNS and event-log edge cases use isolated fixture checks in Tools/Test-ScriptLibrary.ps1.");
        }
        finally { LibraryLocalization.Language = previousLanguage; }
    }
}
