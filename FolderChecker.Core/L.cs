using System.Globalization;

namespace FolderChecker.Core;

/// <summary>Shared display language. File paths, hashes and persisted identifiers are never translated.</summary>
public static class L
{
    private static string _language = "fr";
    public static string Language => Volatile.Read(ref _language);
    public static bool IsEnglish => Language == "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "fr-FR");
    public static event EventHandler? Changed;

    public static void SetLanguage(string language)
    {
        if (language is not ("en" or "fr")) throw new ArgumentException("Supported languages: en, fr.", nameof(language));
        if (Interlocked.Exchange(ref _language, language) != language) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string french, string english) => IsEnglish ? english : french;
    public static string F(FormattableString french, FormattableString english) => (IsEnglish ? english : french).ToString(Culture);

    /// <summary>Binary units: B/KiB/MiB… in English, o/Kio/Mio… in French.</summary>
    public static string Size(long bytes)
    {
        double n = bytes;
        string[] units = IsEnglish ? ["B", "KiB", "MiB", "GiB", "TiB", "PiB"] : ["o", "Kio", "Mio", "Gio", "Tio", "Pio"];
        var i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return n.ToString(i == 0 ? "N0" : "N1", Culture) + " " + units[i];
    }
}
