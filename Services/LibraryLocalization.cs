using System.Globalization;

namespace SC.ScriptForge.Services;

/// <summary>Language of library cards, parameter captions and script reports. Storage keys stay stable.</summary>
public static class LibraryLocalization
{
    private static string _language = "ru";
    public static string Language { get => _language; set => _language = Normalize(value); }
    public static string Normalize(string? language) => language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "ru";
    public static bool IsEnglish => Language == "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "ru-RU");
    public static string Text(string russian, string english, string? language = null) => Normalize(language ?? Language) == "en" ? english : russian;
}
