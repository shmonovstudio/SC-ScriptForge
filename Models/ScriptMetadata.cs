namespace SC.ScriptForge.Models;

public sealed record ScriptMetadata(string Name, string Category, string Description, string Notes,
    IReadOnlyList<string> Tags, bool Favorite, string Status);
