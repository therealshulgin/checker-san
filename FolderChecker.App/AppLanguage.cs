using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Windows;
using FolderChecker.Core;

namespace FolderChecker.App;

/// <summary>WPF resources and an independent language preference, outside scan journals.</summary>
public static class AppLanguage
{
    private static string _settingsPath = DefaultSettingsPath;

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "settings.json");

    /// <summary>A preference write failure never prevents switching the display language.</summary>
    public static string? LastSaveError { get; private set; }

    /// <summary>Call at application startup only; tests can inject both path and fallback culture.</summary>
    public static void Initialize(string? settingsPath = null, CultureInfo? systemCulture = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath;
        var culture = systemCulture ?? CultureInfo.CurrentUICulture;
        var fallback = culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
        var language = ReadLanguage(_settingsPath) ?? fallback;
        Apply(language, persist: false);
    }

    /// <summary>Populate resource strings without reading or writing user preferences.</summary>
    public static void InitializeResources(ResourceDictionary? resources = null)
    {
        resources ??= Application.Current?.Resources;
        if (resources is not null) Populate(resources, L.Language);
    }

    /// <summary>Apply fr/en immediately. Optional paths isolate persistence in tests.</summary>
    public static void Apply(string language, bool persist = true, string? settingsPath = null)
    {
        if (language is not ("fr" or "en"))
            throw new ArgumentException("Supported languages: en, fr.", nameof(language));
        var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "fr-FR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        // Changed subscribers must see the new resource strings and formatting culture.
        if (Application.Current?.Resources is { } resources) Populate(resources, language);
        L.SetLanguage(language);
        LastSaveError = null;
        if (persist) SaveLanguage(settingsPath ?? _settingsPath, language);
    }

    private static void Populate(ResourceDictionary resources, string language)
    {
        foreach (var (key, text) in MainStrings.Values)
            resources[key] = language == "en" ? text.En : text.Fr;
        foreach (var (key, text) in DuplicateStrings.Values)
            resources[key] = language == "en" ? text.En : text.Fr;
    }

    private static string? ReadLanguage(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 4096) return null;
            var settings = JsonSerializer.Deserialize<LanguageSettings>(file);
            return settings is { Version: 1, Language: "fr" or "en" } ? settings.Language : null;
        }
        catch (Exception exception) when (IsPreferenceError(exception) || exception is JsonException)
        {
            return null;
        }
    }

    private static void SaveLanguage(string path, string language)
    {
        string? temporary = null;
        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new LanguageSettings { Language = language });
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception exception) when (IsPreferenceError(exception))
        {
            LastSaveError = exception.Message;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (IsPreferenceError(exception)) { }
            }
        }
    }

    private static bool IsPreferenceError(Exception exception) => exception is IOException
        or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;

    private sealed record LanguageSettings
    {
        public int Version { get; init; } = 1;
        public string? Language { get; init; }
    }
}
