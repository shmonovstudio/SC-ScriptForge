using System.Diagnostics;
using System.Reflection;
using System.Text;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public static class PowerShellHost
{
    public static IReadOnlyList<PowerShellEngine> InstalledEngines()
    {
        var engines = new List<PowerShellEngine>();
        var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windows)) engines.Add(new("windows", "Windows PowerShell 5.1", windows));
        var seven = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(seven)) engines.Add(new("core", "PowerShell 7", seven));
        return engines;
    }

    public static string ReadRuntimeScript(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SC.ScriptForge.Runtime." + name)
            ?? throw new InvalidOperationException("Встроенный сценарий не найден: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static ProcessStartInfo StartInfo(PowerShellEngine engine, string command, bool bypassOnce = false)
    {
        var info = new ProcessStartInfo
        {
            FileName = engine.ExecutablePath, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        // A GUI launched from pwsh can inherit its module paths. Always resolve engine-native
        // modules first, otherwise Windows PowerShell may try loading PowerShell 7 assemblies.
        var nativeModules = Path.Combine(Path.GetDirectoryName(engine.ExecutablePath)!, "Modules");
        var inheritedModules = Environment.GetEnvironmentVariable("PSModulePath");
        info.Environment["PSModulePath"] = nativeModules + (string.IsNullOrWhiteSpace(inheritedModules) ? "" : Path.PathSeparator + inheritedModules);
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text" })
            info.ArgumentList.Add(argument);
        if (bypassOnce) { info.ArgumentList.Add("-ExecutionPolicy"); info.ArgumentList.Add("Bypass"); }
        info.ArgumentList.Add("-EncodedCommand"); info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        return info;
    }
}
