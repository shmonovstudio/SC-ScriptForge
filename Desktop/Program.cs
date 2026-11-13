using System.Text;
using System.Text.Json;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SC.ScriptForge.Core;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;
using SC.ScriptForge.Windows;
using ShmonovStudio.AI;

namespace SC.ScriptForge;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--inventory-worker") return RunWorker(args[1]).GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    private static async Task<int> RunWorker(string category)
    {
        if (!InventoryCategory.Names.ContainsKey(category)) return 64;
        try
        {
            InventoryCategory result;
            try { result = await NativeInventory.CollectAsync(category); }
            catch (Exception e) { result = InventoryCategory.Failed(category, OperationError.From(e)); }
            await using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            await writer.WriteAsync(JsonSerializer.Serialize(result, InventoryExport.JsonOptions));
            return 0;
        }
        catch { return 1; }
    }
}

public static class Bootstrap
{
    public static ServiceProvider CreateServices(string? dataRoot = null, string? workerExecutable = null, bool installBundledScripts = true)
    {
        var services = new ServiceCollection();
        var repository = new ScriptRepository(dataRoot);
        if (installBundledScripts) repository.InstallBundledScripts();
        var preferences = new PreferencesStore(repository.DataRoot);
        var settings = preferences.Load();
        LibraryLocalization.Language = settings.LibraryLanguage;
        services.AddSingleton(repository).AddSingleton(preferences).AddSingleton(settings);
        services.AddSingleton(new DiagnosticLog(repository.DataRoot));
        services.AddSingleton<LibrarySearch>().AddSingleton<LibraryViewModel>();
        services.AddSingleton(new RunHistoryStore(repository.DataRoot));
        var aiSettings = new AiSettingsStore(repository.DataRoot);
        services.AddSingleton(aiSettings).AddSingleton<IAiRuntime>(_ => new AiRuntimeHost(aiSettings.Load()));
        services.AddSingleton<PowerShellContextService>().AddSingleton<ScriptAiService>().AddSingleton<AiAssistantViewModel>();
        services.AddSingleton<IPrivilegeService, WindowsPrivilegeService>();
        services.AddSingleton<IPrivilegedOperationBroker, PrivilegedOperationBroker>();
        services.AddSingleton(provider =>
        {
            var executable = workerExecutable ?? Environment.ProcessPath!;
            IReadOnlyList<string>? prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? [Path.Combine(AppContext.BaseDirectory, "SC.ScriptForge.dll")] : null;
            return new ComputerInventory(InventoryCategory.Names.Keys.Select(id => (IInventoryCategoryService)new IsolatedInventoryService(id, executable, prefix, log: provider.GetRequiredService<DiagnosticLog>())).ToArray());
        });
        services.AddSingleton<ComputerViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
