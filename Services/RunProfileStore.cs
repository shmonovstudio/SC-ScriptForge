using System.Text.Json;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public sealed class RunProfileStore(string dataRoot)
{
    private string PathFor(Guid scriptId) => Path.Combine(dataRoot, "RunProfiles", scriptId.ToString("N") + ".json");
    public ScriptRunProfile Load(Guid scriptId)
    {
        try
        {
            var profile = File.Exists(PathFor(scriptId)) ? JsonSerializer.Deserialize<ScriptRunProfile>(File.ReadAllText(PathFor(scriptId))) ?? new() : new();
            profile.Parameters = new Dictionary<string, ParameterPreset>(profile.Parameters ?? new(), StringComparer.OrdinalIgnoreCase);
            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(Guid scriptId, ScriptRunProfile profile)
    {
        var path = PathFor(scriptId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
