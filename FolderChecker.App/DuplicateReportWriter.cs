using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FolderChecker.Core;

namespace FolderChecker.App;

/// <summary>Exports duplicate groups with the current selection, outside the scanned folder and never over an existing file.</summary>
public static class DuplicateReportWriter
{
    public static void Write(string path, DuplicateResult result, IReadOnlySet<string> selected)
    {
        using var output = ReportWriter.CreateOutside(path, [result.Root]);
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var json = new StreamWriter(output, new UTF8Encoding(false));
            json.Write(JsonSerializer.Serialize(new
            {
                Application = "Checker San", result.Root, ExportedAt = DateTimeOffset.Now,
                Complete = result.SnapshotVerified && !result.Cancelled && result.Errors.Count == 0,
                Groups = result.Groups.Select((group, index) => new
                {
                    Group = index + 1, group.Hash, group.Size,
                    Files = group.Files.Select(file => new { file.RelativePath, file.FullPath, file.Size, file.LastWriteUtc, Selected = selected.Contains(file.FullPath) })
                }),
                result.Errors, result.Warnings
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        using var writer = new StreamWriter(output, new UTF8Encoding(true));
        writer.WriteLine(Csv.Line([L.T("Groupe", "Group"), "SHA-256", L.T("Taille (octets)", "Size (bytes)"), L.T("Chemin relatif", "Relative path"),
            L.T("Chemin complet", "Full path"), L.T("Modifié (UTC)", "Modified (UTC)"), L.T("Choix", "Choice")]));
        foreach (var (group, number) in result.Groups.Select((group, index) => (group, index + 1)))
            foreach (var file in group.Files)
                writer.WriteLine(Csv.Line([number.ToString(CultureInfo.InvariantCulture), group.Hash, file.Size.ToString(CultureInfo.InvariantCulture),
                    file.RelativePath, file.FullPath, file.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    selected.Contains(file.FullPath) ? L.T("À retirer", "To remove") : L.T("Conservé", "Kept")]));
    }
}
