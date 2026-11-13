using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge;
using SC.ScriptForge.Core;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using ShmonovStudio.AI;

internal static partial class Checks
{
    // Real HTTP streams with controlled disconnects, Range support and incorrect servers.
    private sealed class DownloadServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private readonly Task _serve;
        public string Url { get; }
        public string? RequestedRange { get; private set; }
        public DownloadServer(byte[] bytes, string mode = "range")
        {
            _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/model";
            _serve = Serve(bytes, mode);
        }
        private async Task Serve(byte[] bytes, string mode)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    if (line.StartsWith("Range:",StringComparison.OrdinalIgnoreCase)) RequestedRange = line[6..].Trim();
                var requested = RequestedRange is null ? 0 : int.Parse(RequestedRange[6..].TrimEnd('-'));
                var offset = mode is "ignore" or "wrong" ? 0 : requested;
                var partial = RequestedRange is not null && mode != "ignore";
                var headers = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nContent-Length: {bytes.Length - offset}\r\nConnection: close\r\n"
                    + (partial ? $"Content-Range: bytes {offset}-{bytes.Length - 1}/{bytes.Length}\r\n" : "") + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _stop.Token);
                if (mode == "corrupt") { bytes = bytes.ToArray(); bytes[0] ^= 0xFF; }
                await stream.WriteAsync(bytes.AsMemory(offset, mode is "drop" or "stall" ? bytes.Length / 2 : bytes.Length - offset), _stop.Token);
                if (mode == "stall") await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _serve; _stop.Dispose(); }
    }

    private static async Task AiDownloadAsync()
    {
        var root = Fixture("download");
        var bytes = RandomNumberGenerator.GetBytes(1_048_576); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        string FilePath(string name) => Path.Combine(root,name + ".gguf");
        Task Download(string url,string path,CancellationToken token = default) => AiInstaller.DownloadAsync(url,path,bytes.Length,hash,"fixture",null,token);
        var resumed = FilePath("resumed");
        await using (var broken = new DownloadServer(bytes,"drop"))
        {
            var failed = false; try { await Download(broken.Url,resumed); } catch (Exception e) when (e is IOException or HttpRequestException) { failed = true; }
            Check(failed && !File.Exists(resumed) && new FileInfo(AiInstaller.PartialPath(resumed,hash)).Length == bytes.Length / 2,
                "a dropped real HTTP stream retains partial bytes without installing a model");
        }
        await using (var server = new DownloadServer(bytes))
        {
            await Download(server.Url,resumed);
            Check(server.RequestedRange == "bytes=524288-" && File.ReadAllBytes(resumed).SequenceEqual(bytes) && !File.Exists(AiInstaller.PartialPath(resumed,hash)),
                "a new downloader sends Range and atomically installs a hash-verified complete file");
        }
        var cancelled = FilePath("cancelled");
        await using (var server = new DownloadServer(bytes,"stall"))
        {
            using var cancellation = new CancellationTokenSource(); var task = Download(server.Url,cancelled,cancellation.Token);
            await WaitUntil(() => File.Exists(AiInstaller.PartialPath(cancelled,hash)) && new FileInfo(AiInstaller.PartialPath(cancelled,hash)).Length > 0,TimeSpan.FromSeconds(5));
            cancellation.Cancel(); var stopped = false; try { await task; } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !File.Exists(cancelled) && new FileInfo(AiInstaller.PartialPath(cancelled,hash)).Length > 0,
                "user cancellation retains bytes for a later retry and never exposes a partial model");
        }
        await using (var server = new DownloadServer(bytes))
        {
            await Download(server.Url,cancelled);
            Check(server.RequestedRange is not null && File.ReadAllBytes(cancelled).SequenceEqual(bytes),"cancelled downloads resume after reopening");
        }
        var ignored = FilePath("ignored"); await File.WriteAllBytesAsync(AiInstaller.PartialPath(ignored,hash),bytes[..12345]);
        await using (var server = new DownloadServer(bytes,"ignore"))
        {
            await Download(server.Url,ignored);
            Check(server.RequestedRange == "bytes=12345-" && File.ReadAllBytes(ignored).SequenceEqual(bytes),"servers ignoring Range restart safely instead of appending duplicate bytes");
        }
        var wrong = FilePath("wrong"); await File.WriteAllBytesAsync(AiInstaller.PartialPath(wrong,hash),bytes[..12345]);
        await using (var server = new DownloadServer(bytes,"wrong"))
        {
            var refused = false; try { await Download(server.Url,wrong); } catch (InvalidDataException) { refused = true; }
            Check(refused && !File.Exists(wrong),"an incorrect Content-Range never becomes an installed model");
        }
        var corrupt = FilePath("corrupt");
        await using (var server = new DownloadServer(bytes,"corrupt"))
        {
            var refused = false; try { await Download(server.Url,corrupt); } catch (InvalidDataException) { refused = true; }
            Check(refused && !File.Exists(corrupt) && !File.Exists(AiInstaller.PartialPath(corrupt,hash)),"SHA256 mismatch removes corrupt partial bytes and blocks installation");
        }
        var complete = FilePath("complete"); await File.WriteAllBytesAsync(AiInstaller.PartialPath(complete,hash),bytes);
        await Download("http://127.0.0.1:1/unreachable",complete);
        await Download("http://127.0.0.1:1/unreachable",complete);
        Check(File.ReadAllBytes(complete).SequenceEqual(bytes),"complete partial and previously installed files are verified and reused without network access");
        var protectedFile = FilePath("protected"); await File.WriteAllTextAsync(protectedFile,"user file");
        var protectedError = false; try { await Download("http://127.0.0.1:1/",protectedFile); } catch (IOException) { protectedError = true; }
        Check(protectedError && File.ReadAllText(protectedFile) == "user file","existing unrelated files remain untouched");
        var locked = FilePath("locked");
        using (var held = new FileStream(AiInstaller.PartialPath(locked,hash) + ".lock",FileMode.Create,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose))
        {
            var protectedError2 = false; try { await Download("http://127.0.0.1:1/",locked); } catch (IOException) { protectedError2 = true; }
            Check(protectedError2 && !File.Exists(locked),"concurrent downloaders cannot append to the same partial file");
        }
        var store = new AiSettingsStore(root); var customFolder = Path.Combine(root,"другой диск и папка"); store.RememberInstallationFolder(customFolder);
        Check(new AiSettingsStore(root).InstallationFolder == customFolder,"chosen installation directory survives restart before a download finishes");
        Check(AiCatalog.Models.Count == 3 && AiCatalog.Models.All(m => m.RecommendedRamGb > 0 && m.RecommendedVramGb > 0 && m.Description.Length > 0),"all three model choices describe purpose, RAM and VRAM requirements");
        await AiDownloadSettingsAsync(root,store);
    }

    private static async Task AiDownloadSettingsAsync(string root,AiSettingsStore store)
    {
        using var services = Bootstrap.CreateServices(root,_worker,installBundledScripts:false);
        var owner = services.GetRequiredService<MainWindow>(); owner.Show();
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var vm = services.GetRequiredService<AiAssistantViewModel>();
        var showing = AiSettingsWindow.ShowAsync(owner,vm,store); await Task.Delay(200);
        var window = owner.OwnedWindows.Single();
        Check(Named<ComboBox>(window,"AiInstallModel").ItemCount == 3 && Named<TextBox>(window,"AiInstallFolder").Text == store.InstallationFolder,
            "settings expose a small model catalog and the remembered destination");
        Named<ComboBox>(window,"AiInstallModel").SelectedIndex = 2; Named<ComboBox>(window,"AiComputeBackend").SelectedIndex = 1;
        await Task.Delay(120); SaveFrame(window,"ai-download-gpu-dark");
        window.Width = 600; window.Height = 640; await Task.Delay(120); SaveFrame(window,"ai-download-600");
        var save = Named<Button>(window,"AiSettingsSave"); var status = Named<TextBlock>(window,"AiInstallStatus");
        var install = Named<Button>(window,"AiDownloadInstall");
        Check(save.TranslatePoint(new Avalonia.Point(save.Bounds.Width,save.Bounds.Height),window) is { } bottom && bottom.X <= window.Width && bottom.Y <= window.Height
            && status.IsEffectivelyVisible && install.IsEffectivelyVisible && install.TranslatePoint(new Point(0,0),window) is { Y: > 0 } buttonTop && buttonTop.Y < window.Height,
            "settings keep download action, feedback and save controls visible at minimum width");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light; window.Width = 840; window.Height = 800;
        await Task.Delay(120); SaveFrame(window,"ai-download-gpu-light");
        Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close(); await showing;
        await WaitUntil(() => !services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(90)); owner.Close();
    }

    private static async Task AiDownloadLiveAsync()
    {
        var workspace = Path.GetFullPath(Path.Combine(_root,"..",".."));
        var folder = Path.Combine(_root,"installation"); Directory.CreateDirectory(folder);
        // Prepared by the validation command: model hard link and the official pinned archives.
        var options = await new AiInstaller().InstallAsync(folder,AiCatalog.Qwen4B,AiComputeBackend.Vulkan);
        var root = Fixture("activation"); var store = new AiSettingsStore(root);
        var previous = new AiRuntimeOptions { ModelPath = Path.Combine(workspace,"AI","Models",AiCatalog.Qwen4B.FileName),RuntimePath = Path.Combine(workspace,"AI","Runtime","llama-server.exe") };
        store.Save(previous);
        using var services = Bootstrap.CreateServices(root,_worker,installBundledScripts:false);
        var vm = services.GetRequiredService<AiAssistantViewModel>();
        options = options with { ContextTokens = 4096,Threads = AiHardwareProfile.Current.RecommendedThreads,MaximumOutputTokens = 1024 };
        await vm.ActivateInstalledAsync(options);
        Check(vm.IsModelLoaded && store.Load() == options && vm.ModelStateLabel.Contains("GPU"),"installation activates the real Vulkan model and persists its selected paths automatically");
        var runtime = services.GetRequiredService<IAiRuntime>(); var answer = new StringBuilder();
        await foreach (var delta in runtime.GenerateAsync(new([new("user","Ответь коротко по-русски: сколько будет два плюс два?")],32))) answer.Append(delta.Text);
        Check(answer.Length > 0 && runtime.State.Message.Contains("Vulkan"),"the downloaded Vulkan engine performs real generation on the local GPU");
        Results.Add("OBSERVATION GPU answer: " + answer.ToString().Replace('\n',' '));
        var invalidModel = Path.Combine(root,"broken.gguf"); await File.WriteAllTextAsync(invalidModel,"not GGUF");
        var rejected = false;
        try { await vm.ActivateInstalledAsync(options with { ModelPath = invalidModel }); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && store.Load() == options && vm.Options.ModelPath == options.ModelPath && !runtime.State.Loaded,
            "a failed model startup rolls back configuration and leaves no failed native process running");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var failed = false;
            try { await vm.ActivateInstalledAsync(options with { ComputeBackend = AiComputeBackend.Cpu },cancelled.Token); } catch (OperationCanceledException) { failed = true; }
            Check(failed && store.Load() == options,"cancelled activation retains the previous persisted configuration");
        }
        await vm.UnloadAsync();
        var cpu = await new AiInstaller().InstallAsync(folder,AiCatalog.Qwen4B,AiComputeBackend.Cpu);
        Check(cpu.RuntimePath != options.RuntimePath && cpu.ModelPath == options.ModelPath,"CPU and Vulkan builds have isolated DLL directories and share a verified model");
        await vm.ActivateInstalledAsync(cpu);
        answer.Clear(); await foreach (var delta in runtime.GenerateAsync(new([new("user","Ответь одним словом: привет")],16))) answer.Append(delta.Text);
        Check(vm.IsModelLoaded && answer.Length > 0 && store.Load().ComputeBackend == AiComputeBackend.Cpu,"switching back to CPU activates and generates without redownloading the model");
        await vm.UnloadAsync();
        var owner = services.GetRequiredService<MainWindow>(); owner.Show();
        var showing = AiSettingsWindow.ShowAsync(owner,vm,store); await Task.Delay(160); var window = owner.OwnedWindows.Single();
        Named<TextBox>(window,"AiInstallFolder").Text = folder;
        Named<ComboBox>(window,"AiInstallModel").SelectedIndex = 1; Named<ComboBox>(window,"AiComputeBackend").SelectedIndex = 1;
        Click(Named<Button>(window,"AiDownloadInstall"));
        var status = Named<TextBlock>(window,"AiInstallStatus");
        await WaitUntil(() => (status.Text ?? "").StartsWith("Готово") || Named<Button>(window,"AiDownloadInstall").IsEnabled,TimeSpan.FromMinutes(2));
        Check((status.Text ?? "").StartsWith("Готово") && vm.IsModelLoaded && store.Load().ComputeBackend == AiComputeBackend.Vulkan && store.InstallationFolder == folder,
            "the actual settings download button completes install, model startup and saving without a second Save click");
        SaveFrame(window,"ai-download-ready");
        Click(Named<Button>(window,"AiSettingsSave")); await showing;
        Check(vm.IsModelLoaded,"closing saved settings does not unload the just-activated model");
        await vm.UnloadAsync();
        await WaitUntil(() => !services.GetRequiredService<ComputerViewModel>().IsBusy,TimeSpan.FromSeconds(90)); owner.Close();
    }
}
