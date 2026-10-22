using System.Windows.Media;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class RowView(ComparisonRow row)
{
    public ComparisonRow Row { get; } = row;
    public string RelativePath => Row.RelativePath;
    public string KindLabel => Row.Kind == EntryKind.Directory ? L.T("Dossier", "Folder") : L.T("Fichier", "File");
    public string SourceSizeLabel => FormatSize(Row.SourceSize);
    public string TargetSizeLabel => FormatSize(Row.TargetSize);
    public string StatusLabel => Row.Status switch
    {
        ComparisonStatus.Identical => L.T("Identique · SHA-256", "Identical · SHA-256"),
        ComparisonStatus.CommonDirectory => L.T("Dossier commun", "Common folder"),
        ComparisonStatus.OnlySource => L.T("Absent du dossier 2", "Missing from folder 2"),
        ComparisonStatus.OnlyTarget => L.T("Absent du dossier 1", "Missing from folder 1"),
        ComparisonStatus.Moved => L.T("Retrouvé ailleurs", "Found elsewhere"),
        ComparisonStatus.Different => L.T("Contenu différent", "Different content"),
        ComparisonStatus.Conflict => L.T("Conflit fichier / dossier", "File / folder conflict"),
        _ => L.T("Non vérifié", "Not verified")
    };
    public Brush StatusBackground => Brush("#2B2B2B");
    public Brush StatusForeground => Brush(Row.Status switch
    {
        ComparisonStatus.Identical or ComparisonStatus.CommonDirectory => "#A5D9BB",
        ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget or ComparisonStatus.Unverified => "#E8C395",
        ComparisonStatus.Different or ComparisonStatus.Conflict => "#F1A9B7",
        ComparisonStatus.Moved => "#C7CDD6",
        _ => "#BDC6E6"
    });
    public static Brush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
    public static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue) return "—";
        double n = bytes.Value;
        string[] units = L.IsEnglish ? ["B", "KiB", "MiB", "GiB", "TiB", "PiB"] : ["o", "Kio", "Mio", "Gio", "Tio", "Pio"];
        int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return n.ToString(i == 0 ? "N0" : "N1", L.Culture) + " " + units[i];
    }
}
