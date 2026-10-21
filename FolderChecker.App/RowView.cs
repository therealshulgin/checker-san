using System.Globalization;
using System.Windows.Media;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class RowView(ComparisonRow row)
{
    public ComparisonRow Row { get; } = row;
    public string RelativePath => Row.RelativePath;
    public string KindLabel => Row.Kind == EntryKind.Directory ? "Dossier" : "Fichier";
    public string SourceSizeLabel => FormatSize(Row.SourceSize);
    public string TargetSizeLabel => FormatSize(Row.TargetSize);
    public string StatusLabel => Row.Status switch
    {
        ComparisonStatus.Identical => "Identique · SHA-256",
        ComparisonStatus.CommonDirectory => "Dossier commun",
        ComparisonStatus.OnlySource => "Absent du dossier 2",
        ComparisonStatus.OnlyTarget => "Absent du dossier 1",
        ComparisonStatus.Moved => "Retrouvé ailleurs",
        ComparisonStatus.Different => "Contenu différent",
        ComparisonStatus.Conflict => "Conflit fichier / dossier",
        _ => "Non vérifié"
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
        string[] units = ["o", "Kio", "Mio", "Gio", "Tio", "Pio"];
        int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return n.ToString(i == 0 ? "N0" : "N1", CultureInfo.GetCultureInfo("fr-FR")) + " " + units[i];
    }
}
