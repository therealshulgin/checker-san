using System.IO;
using System.Reflection;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static void LinksAndOnlineFilesExplainVerdict()
    {
        using var workspace = new TestWorkspace();
        var window = new MainWindow();
        try
        {
            var verified = VerifiedResult(workspace);
            var link = new ComparisonRow
            {
                Kind = EntryKind.Directory, Status = ComparisonStatus.Skipped, RelativePath = "My Music",
                SourcePath = Path.Combine(workspace.Source, "My Music"), Detail = "link"
            };
            var online = new ComparisonRow
            {
                Kind = EntryKind.File, Status = ComparisonStatus.Unverified, OnlineOnly = true, RelativePath = "cloud.mp4",
                SourcePath = Path.Combine(workspace.Source, "cloud.mp4"), TargetPath = Path.Combine(workspace.Target, "cloud.mp4"),
                SourceSize = 9, TargetSize = 9, Detail = "online"
            };
            var result = verified with
            {
                Rows = [.. verified.Rows, link, online],
                SourceFullyContained = false, TargetFullyContained = false, TreesIdentical = false
            };
            typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, result);
            Invoke(window, "ShowResult", result);
            Invoke(window, "RefreshCopy");
            var detail = Control<TextBlock>(window, "VerdictDetail").Text;
            Check(detail.StartsWith("1 fichier(s) disponible(s) uniquement en ligne", StringComparison.Ordinal),
                "an unread online-only file, not an error, explains the incomplete verdict: " + detail);
            Check(detail.Contains("liens ou jonctions non suivis", StringComparison.Ordinal), "skipped links are counted in the verdict");
            AssertContainmentVerdicts(window, "Non vérifié", "Non vérifié");
            Check(Control<Button>(window, "TransferButton").IsEnabled, "online-only files present on both sides do not block completing folders");
            Equal("Lien non suivi", new RowView(link).StatusLabel, "skipped link label");
            Equal("En ligne · non téléchargé", new RowView(online).StatusLabel, "online-only label");
        }
        finally { window.Close(); }
    }
}
