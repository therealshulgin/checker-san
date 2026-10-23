using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FolderChecker.Core;

namespace FolderChecker.App;

public static class ReportWriter
{
    public static void Write(string path, ComparisonResult result)
    {
        using var output = CreateOutside(path, [result.SourceRoot, result.TargetRoot]);
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            using var jsonWriter = new StreamWriter(output, new UTF8Encoding(false));
            jsonWriter.Write(JsonSerializer.Serialize(new
            {
                Application = "Checker San 1.0", Algorithm = L.T("SHA-256 (contenu complet)", "SHA-256 (complete contents)"),
                Scope = L.T("Flux principal des fichiers et arborescence. Hors droits NTFS, flux alternatifs et métadonnées du système de fichiers.",
                    "Main file data stream and folder structure. Excludes NTFS permissions, alternate data streams and filesystem metadata."),
                result
            }, options));
            return;
        }
        using var writer = new StreamWriter(output, new UTF8Encoding(true));
        // A single rectangular table is easier to filter in Excel. Verdicts and roots are repeated intentionally.
        WriteLine(writer, [L.T("État", "Status"), L.T("Type", "Type"), L.T("Chemin relatif", "Relative path"),
            L.T("Chemin dossier 1", "Folder 1 path"), L.T("Chemin dossier 2", "Folder 2 path"),
            L.T("Taille 1 (octets)", "Size 1 (bytes)"), L.T("Taille 2 (octets)", "Size 2 (bytes)"),
            L.T("SHA-256 dossier 1", "Folder 1 SHA-256"), L.T("SHA-256 dossier 2", "Folder 2 SHA-256"),
            L.T("Détail", "Detail"), L.T("Racine 1", "Root 1"), L.T("Racine 2", "Root 2"),
            L.T("Début", "Started"), L.T("Fin", "Completed"), L.T("Annulé", "Cancelled"),
            L.T("Instantané vérifié", "Snapshot verified"), L.T("Dossier 2 contient tout le dossier 1", "Folder 2 contains all of folder 1"),
            L.T("Arborescences identiques", "Identical folder trees"), L.T("Dossier 1 contient tout le dossier 2", "Folder 1 contains all of folder 2")]);
        var verified = !result.Cancelled && result.SnapshotVerified && result.Errors.Count == 0 &&
            result.Rows.All(row => row.Status != ComparisonStatus.Unverified);
        var meta = new[] { result.SourceRoot, result.TargetRoot, result.StartedAt.ToString("O"), result.CompletedAt.ToString("O"), YesNo(result.Cancelled), YesNo(result.SnapshotVerified), verified ? YesNo(result.SourceFullyContained) : L.T("Non vérifié", "Not verified"), YesNo(result.TreesIdentical), verified ? YesNo(result.TargetFullyContained) : L.T("Non vérifié", "Not verified") };
        WriteLine(writer, new[] { L.T("Résumé", "Summary"), "", "", "", "", "", "", "", "", L.F(
            $"Comparaison SHA-256 du contenu principal et de l’arborescence ; hors droits et flux NTFS alternatifs. Empreintes reprises après contrôle : {result.FilesReused}. Point de reprise sauvegardé : {YesNo(result.CheckpointSaved)}.",
            $"SHA-256 comparison of main file contents and folder structure; excludes permissions and alternate NTFS streams. Hashes reused after validation: {result.FilesReused}. Checkpoint saved: {YesNo(result.CheckpointSaved)}.") }.Concat(meta));
        foreach (var row in result.Rows)
        {
            var view = new RowView(row);
            WriteLine(writer, new[] { view.StatusLabel, view.KindLabel, row.RelativePath, row.SourcePath ?? "", row.TargetPath ?? "", row.SourceSize?.ToString(CultureInfo.InvariantCulture) ?? "", row.TargetSize?.ToString(CultureInfo.InvariantCulture) ?? "", row.SourceHash ?? "", row.TargetHash ?? "", row.Detail }.Concat(meta));
        }
        foreach (var error in result.Errors)
            WriteLine(writer, new[] { L.T("Erreur", "Error"), "", "", "", "", "", "", "", "", error }.Concat(meta));
        foreach (var warning in result.Warnings)
            WriteLine(writer, new[] { L.T("Avertissement", "Warning"), "", "", "", "", "", "", "", "", warning }.Concat(meta));
    }
    /// <summary>Creates a new report outside every scanned root, through the engine's single check.</summary>
    internal static FileStream CreateOutside(string path, IEnumerable<string> roots) => OutputFiles.CreateOutside(path, roots);
    private static string YesNo(bool value) => value ? L.T("Oui", "Yes") : L.T("Non", "No");
    private static void WriteLine(TextWriter writer, IEnumerable<string> fields) => writer.WriteLine(Csv.Line(fields));
}
