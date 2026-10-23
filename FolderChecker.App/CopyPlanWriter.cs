using System.Globalization;
using System.IO;
using System.Text;
using FolderChecker.Core;

namespace FolderChecker.App;

/// <summary>Exports a copy preview — what Complete folders would copy — before anything is written, outside both folders.</summary>
public static class CopyPlanWriter
{
    public static void Write(string path, CopyPlan plan)
    {
        using var output = ReportWriter.CreateOutside(path, [plan.SourceRoot, plan.TargetRoot]);
        using var writer = new StreamWriter(output, new UTF8Encoding(true));
        writer.WriteLine(Csv.Line([L.T("Type", "Type"), L.T("Chemin relatif", "Relative path"), L.T("Taille (octets)", "Size (bytes)"),
            L.T("Modifié (UTC)", "Modified (UTC)"), L.T("Depuis", "From"), L.T("Vers", "To"), L.T("Déjà copié", "Already copied")]));
        foreach (var item in plan.Items)
        {
            var (from, to) = item.ReverseDirection ? (plan.TargetRoot, plan.SourceRoot) : (plan.SourceRoot, plan.TargetRoot);
            writer.WriteLine(Csv.Line([item.Kind == EntryKind.Directory ? L.T("Dossier", "Folder") : L.T("Fichier", "File"), item.RelativePath,
                item.Kind == EntryKind.Directory ? "" : item.Size.ToString(CultureInfo.InvariantCulture),
                item.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Path.Combine(from, item.RelativePath), Path.Combine(to, item.RelativePath), item.Completed ? L.T("Oui", "Yes") : L.T("Non", "No")]));
        }
    }
}
