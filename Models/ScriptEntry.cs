using System.Text.Json.Serialization;
using SC.ScriptForge.Services;

namespace SC.ScriptForge.Models;

public sealed class ScriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Status { get; set; } = "Draft";
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, ScriptTranslation> Translations { get; set; } = new();
    public bool Favorite { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;

    // Non-persisted fields.
    [JsonIgnore]
    public string FilePath { get; set; } = "";
    [JsonIgnore]
    public DateTime FileUpdated { get; set; }
    [JsonIgnore]
    public DateTime LastModified => Updated > FileUpdated ? Updated : FileUpdated;
    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);
    [JsonIgnore]
    public string DisplayName => Translate(Name, t => t.Name);
    [JsonIgnore]
    public string DisplayDescription => Translate(Description, t => t.Description);
    [JsonIgnore]
    public string DisplayNotes => Translate(Notes, t => t.Notes);
    private string Translate(string original, Func<ScriptTranslation, string> select)
    {
        // A manually edited field always takes precedence over its bundled translations.
        return LibraryLocalization.IsEnglish && Translations.TryGetValue("ru", out var source) && select(source) == original
            && Translations.TryGetValue("en", out var translated) && !string.IsNullOrWhiteSpace(select(translated)) ? select(translated) : original;
    }
    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        "Ready" => LibraryLocalization.Text("Готов", "Ready"),
        "Archived" => LibraryLocalization.Text("Архив", "Archived"),
        _ => LibraryLocalization.Text("Черновик", "Draft")
    };
}

public sealed class ScriptTranslation
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class ScriptVersion
{
    public string Path { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string Reason { get; init; } = "";
    public string Description => $"{Timestamp:dd.MM.yyyy · HH:mm}  —  {Reason}";
}

public sealed class TrashEntry
{
    public string Folder { get; set; } = "";
    public string OriginalRelativePath { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime DeletedAt { get; set; }
    public string Display => $"{Name}    ·    {DeletedAt:dd.MM.yyyy HH:mm}";
}
