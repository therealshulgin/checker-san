using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static void DuplicateUiKeepRuleChoosesKeeper()
    {
        var fixture = DuplicateUiFixture();
        DateTime Year(int year) => new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dated = fixture with
        {
            Groups =
            [
                fixture.Groups[0] with { Files = [fixture.Groups[0].Files[0] with { LastWriteUtc = Year(2020) }, fixture.Groups[0].Files[1] with { LastWriteUtc = Year(2015) }, fixture.Groups[0].Files[2] with { LastWriteUtc = Year(2022) }] },
                fixture.Groups[1]
            ]
        };
        var window = DuplicateUiWindow(dated);
        try
        {
            DupControl<ComboBox>(window, "KeepRuleBox").SelectedIndex = 1;
            DupInvoke(window, "SelectExtras_Click", window, new RoutedEventArgs());
            var kept = DuplicateUiRows(window).Where(row => !row.IsSelected).Select(row => row.RelativePath).ToArray();
            Equal("b.bin|d.wav", string.Join("|", kept), "the oldest copy of each group is kept");
            Check(DupControl<Button>(window, "RemoveButton").IsEnabled, "a rule-based selection can be reviewed");
        }
        finally { window.Close(); }
    }

    private static void DuplicateUiNoRecycleBinOnShares()
    {
        var window = DuplicateUiWindow(DuplicateUiFixture() with { Root = @"\\server\share\photos", CanonicalRoot = @"\\server\share\photos" });
        try
        {
            Check(!DupControl<ComboBoxItem>(window, "RecycleChoice").IsEnabled, "a network share has no Recycle Bin");
            Equal("Quarantine", (DupControl<ComboBox>(window, "RemovalModeBox").SelectedItem as ComboBoxItem)?.Tag, "the reversible quarantine is chosen instead");
        }
        finally { window.Close(); }
    }

    private static void DuplicateExportListsSelection()
    {
        using var workspace = new TestWorkspace();
        var fixture = DuplicateUiFixture();
        string Under(string relative) => Path.Combine(workspace.Source, relative);
        var result = fixture with
        {
            Root = workspace.Source, CanonicalRoot = workspace.Source,
            Groups = fixture.Groups.Select(group => group with { Files = group.Files.Select(file => file with { FullPath = Under(file.RelativePath) }).ToArray() }).ToArray()
        };
        var selected = new HashSet<string>([Under("b.bin"), Under("c.mp4")], StringComparer.OrdinalIgnoreCase);
        var csv = Path.Combine(workspace.Root, "duplicates.csv");
        DuplicateReportWriter.Write(csv, result, selected);
        var rows = ReadCsv(csv);
        Equal(6, rows.Count, "a header and one line per file");
        Equal("Choix", rows[0][6], "the choice column is labelled");
        Equal("À retirer", rows.Single(row => row[3] == "b.bin")[6], "selected copies are marked for removal");
        Equal("Conservé", rows.Single(row => row[3] == "a.txt")[6], "unselected copies are marked as kept");
        Check(rows.Skip(1).All(row => row[1].Length == 64), "every line carries the group's SHA-256");
        var inside = Path.Combine(workspace.Source, "duplicates.csv");
        Throws<IOException>(() => DuplicateReportWriter.Write(inside, result, selected));
        Check(!File.Exists(inside), "the list is never written inside the scanned folder");
    }

    private static void DuplicateRecyclePreviewNeedsNoConsent()
    {
        var window = new DuplicateRemovalDialog(DuplicateUiFixture(), [@"C:\fixture\b.bin"], DuplicateRemovalMode.RecycleBin);
        try
        {
            Check(DupControl<Button>(window, "ConfirmRemovalButton").IsEnabled, "the reversible Recycle Bin needs no extra acknowledgement");
            Equal(Visibility.Collapsed, DupControl<CheckBox>(window, "PermanentConsent").Visibility, "the permanent-deletion consent is hidden");
            Equal("Mettre à la Corbeille", DupControl<Button>(window, "ConfirmRemovalButton").Content, "the action names the Recycle Bin");
        }
        finally { window.Close(); }
    }
}
