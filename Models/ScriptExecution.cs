namespace SC.ScriptForge.Models;

public enum ParameterRole { Text, InputFile, OutputFile, InputDirectory, OutputDirectory, Number, Boolean, Choice, Lines, Secret }
public sealed record PowerShellEngine(string Id, string Name, string ExecutablePath);
public sealed class ScriptParameterDefinition
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "System.String";
    public string Description { get; set; } = "";
    public bool HasDefault { get; set; }
    public string DefaultDisplay { get; set; } = "";
    public string? LiteralDefault { get; set; }
    public string[] Choices { get; set; } = Array.Empty<string>();
    public string[] Sets { get; set; } = new[] { "*" };
    public string[] RequiredIn { get; set; } = Array.Empty<string>();
}
public sealed class ScriptDefinition
{
    public string ScriptPath { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string DefaultSet { get; set; } = "";
    public string[] ParameterSets { get; set; } = Array.Empty<string>();
    public List<ScriptParameterDefinition> Parameters { get; set; } = new();
}
public sealed record BoundScriptParameter(string Name, string TypeName, object? Value);
public sealed record ScriptExecutionRequest(string ScriptPath, string Fingerprint, PowerShellEngine Engine,
    IReadOnlyList<BoundScriptParameter> Parameters, IReadOnlyList<string> OutputDirectories)
{
    public bool BypassOnce { get; init; }
    public string Language { get; init; } = SC.ScriptForge.Services.LibraryLocalization.Language;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}
public enum ScriptOutputKind { Message, Data, Warning, Information, Diagnostic }
public sealed record ScriptOutput(string Text, bool IsError = false)
{
    public ScriptOutputKind Kind { get; init; }
    public IReadOnlyList<ScriptResultField> Fields { get; init; } = Array.Empty<ScriptResultField>();
    public string? FilePath { get; init; }
    public bool PreviewLimited { get; init; }
}
public sealed record ScriptResultField(string Name, string Value);
public sealed record ScriptRunResult(int ExitCode, bool Cancelled, TimeSpan Duration, string LogPath)
{
    public bool ExecutionPolicyBlocked { get; init; }
    public bool TimedOut { get; init; }
    public SC.ScriptForge.Core.OperationError? Error { get; init; }
}

public sealed class ParameterPreset
{
    public string TypeName { get; set; } = "";
    public string Value { get; set; } = "";
    public bool BooleanValue { get; set; }
    public bool UseDefault { get; set; }
    public ParameterRole Role { get; set; }
}
public sealed class ScriptRunProfile
{
    public string EngineId { get; set; } = "windows";
    public string ParameterSet { get; set; } = "";
    public Dictionary<string, ParameterPreset> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
