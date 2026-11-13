using System.Text.Json;

namespace SC.ScriptForge.Services;

public sealed class AppPreferences
{
    public LibrarySort Sort { get; set; } = LibrarySort.Recent;
    public string Status { get; set; } = "All";
    public string Category { get; set; } = "Все категории";
    public bool ReduceMotion { get; set; }
    public double Width { get; set; } = 1370;
    public double Height { get; set; } = 990;
    public bool Maximized { get; set; }
    public string Theme { get; set; } = "Light";
    public string LibraryLanguage { get; set; } = "ru";
    public int DesignVersion { get; set; }
}

public sealed class PreferencesStore(string dataRoot)
{
    private readonly string _path = Path.Combine(dataRoot, "preferences.json");
    public AppPreferences Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(AppPreferences preferences)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
