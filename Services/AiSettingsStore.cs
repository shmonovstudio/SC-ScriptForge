using System.Text.Json;
using ShmonovStudio.AI;

namespace SC.ScriptForge.Services;

public sealed class AiSettingsStore(string dataRoot)
{
    private readonly string _path = Path.Combine(dataRoot, "ai-settings.json");
    private readonly string _folderPath = Path.Combine(dataRoot, "ai-installation-folder.json");
    public string InstallationFolder
    {
        get
        {
            try
            {
                if (File.Exists(_folderPath) && JsonSerializer.Deserialize<string>(File.ReadAllText(_folderPath)) is { Length: > 0 } saved) return Path.GetFullPath(saved);
                if (File.Exists(_path) && JsonSerializer.Deserialize<AiRuntimeOptions>(File.ReadAllText(_path)) is { InstallationDirectory.Length: > 0 } options) return options.InstallationDirectory;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; directory is not null && i < 6; i++, directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AI", "Runtime", "llama-server.exe"))) return Path.Combine(directory.FullName, "AI");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataRoot)) ?? AppContext.BaseDirectory, "AI");
        }
    }
    public void RememberInstallationFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Выберите папку установки.");
        var temporary = _folderPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(Path.GetFullPath(folder))); File.Move(temporary, _folderPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public AiRuntimeOptions Load()
    {
        try { if (File.Exists(_path) && JsonSerializer.Deserialize<AiRuntimeOptions>(File.ReadAllText(_path)) is { } value) return value; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        var folder = InstallationFolder;
        string FindModel()
        {
            var directory = new DirectoryInfo(folder);
            for (var i = 0; directory is not null && i < 4; i++, directory = directory.Parent)
                foreach (var relative in new[] { AiCatalog.Qwen4B, AiCatalog.Qwen2B, AiCatalog.Qwen9B }.SelectMany(m => new[] { Path.Combine("Models",m.FileName), Path.Combine("AI","Models",m.FileName) }))
                    if (File.Exists(Path.Combine(directory.FullName, relative))) return Path.Combine(directory.FullName, relative);
            return "";
        }
        var cpu = Path.Combine(folder, "Runtime", AiCatalog.RuntimeVersion + "-cpu", "llama-server.exe");
        return new() { RuntimePath = File.Exists(cpu) ? cpu : Path.Combine(folder, "Runtime", "llama-server.exe"), ModelPath = FindModel() };
    }
    public void Save(AiRuntimeOptions options)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
