using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

// Gathering everything into folder 1 or 2 from the verdict card.
internal static partial class Program
{
    private static void ShowCompared(MainWindow window, TestWorkspace workspace, ComparisonResult result)
    {
        Control<TextBox>(window, "SourceBox").Text = workspace.Source;
        Control<TextBox>(window, "TargetBox").Text = workspace.Target;
        typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, result);
        Invoke(window, "ShowResult", result);
        Invoke(window, "RefreshCopy");
    }

    private static void GatherButtonsFollowTheVerdicts()
    {
        using var workspace = new TestWorkspace();
        var comparison = CopyPreviewFixture(workspace);
        var window = new MainWindow();
        try
        {
            ShowCompared(window, workspace, comparison);
            AssertContainmentVerdicts(window, "Non", "Non");
            var into1 = Control<Button>(window, "GatherInto1Button");
            var into2 = Control<Button>(window, "GatherInto2Button");
            Check(into1.Visibility == Visibility.Visible && into1.IsEnabled, "folder 1 lacks a file: the button to gather it is offered");
            Equal("Tout regrouper dans le 1 · 1 fichier(s) à copier…", into1.Content, "the button says how many files it would copy");
            Equal("Tout regrouper dans le 2 · 1 fichier(s) à copier…", into2.Content, "and the other one too");
            Invoke(window, "SetBusy", true);
            Check(into1.Visibility == Visibility.Collapsed && into2.Visibility == Visibility.Collapsed, "an operation starting hides both buttons");
        }
        finally { window.Close(); }

        var identical = new MainWindow();
        try
        {
            ShowCompared(identical, workspace, VerifiedResult(workspace));
            Check(Control<Button>(identical, "GatherInto1Button").Visibility == Visibility.Collapsed &&
                Control<Button>(identical, "GatherInto2Button").Visibility == Visibility.Collapsed, "nothing to gather, no button");
        }
        finally { identical.Close(); }
    }

    private static void GatherDespiteIncidents()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.Source, "left.txt"), "only left");
        File.WriteAllText(Path.Combine(workspace.Target, "right.txt"), "only right");
        File.WriteAllText(Path.Combine(workspace.Source, "locked.txt"), "same text");
        File.WriteAllText(Path.Combine(workspace.Target, "locked.txt"), "same text");
        ComparisonResult comparison;
        using (new FileStream(Path.Combine(workspace.Source, "locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        Check(comparison.Errors.Count > 0, "the fixture has an incident");
        var window = new MainWindow();
        try
        {
            ShowCompared(window, workspace, comparison);
            AssertContainmentVerdicts(window, "Non", "Non");
            var csv = Path.Combine(workspace.Root, "rapport.csv");
            ReportWriter.Write(csv, comparison);
            var first = ReadCsv(csv)[1];
            Check(first[16] == "Non" && first[18] == "Non", $"REG-P06: despite the incident, the report answers as the screen does: {first[16]} / {first[18]}");
            Check(Control<Button>(window, "TransferButton").IsEnabled, "an incident no longer greys out the copy");
            Check(Control<Button>(window, "GatherInto2Button").Visibility == Visibility.Visible, "gathering is offered despite the incident");
        }
        finally { window.Close(); }
        var dialog = new CopyDialog(comparison, null, CopyDialog.Mode.IntoFolder2);
        try
        {
            AwaitPreview(dialog);
            Equal(CopyDialog.Mode.IntoFolder2, dialog.SelectedMode, "the dialog opens on the chosen gathering");
            Equal("left.txt", dialog.Plan!.Items.Single().RelativePath, "the unreadable file is left out");
            Check(((TextBlock)dialog.FindName("IncidentsText")).Text.StartsWith("1 élément(s) avec un incident"), "the incident is explained before copying");
            Equal("Regrouper dans le dossier 2", ((Button)dialog.FindName("StartCopyButton")).Content, "the action names the destination");
        }
        finally { dialog.Close(); }
    }

    private static void GatherDialogSkipsWhatIsElsewhere()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Source, "photo.jpg"), Body(1));
        Directory.CreateDirectory(Path.Combine(workspace.Target, "sorted"));
        File.WriteAllBytes(Path.Combine(workspace.Target, "sorted", "photo.jpg"), Body(1));
        File.WriteAllText(Path.Combine(workspace.Target, "song.mp3"), "music");
        var comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        var dialog = new CopyDialog(comparison, null, CopyDialog.Mode.IntoFolder1);
        try
        {
            AwaitPreview(dialog);
            Equal(workspace.Target, dialog.Plan!.SourceRoot, "gathering into 1 copies from folder 2");
            Equal("song.mp3", string.Join("|", dialog.Plan.Items.Select(item => item.RelativePath)), "the photo already in folder 1 is not copied back");
            Check(((TextBlock)dialog.FindName("ModeNote")).Text.Contains("n’est pas recopié"), "the dialog says why");
            Check(dialog.FindName("CompareInsteadButton") is null, "one moved file suggests no other folder");
            ((ComboBox)dialog.FindName("DirectionBox")).SelectedIndex = 2;
            AwaitPreview(dialog);
            Equal(CopyDialog.Mode.Backward, dialog.SelectedMode, "third choice completes folder 1 path by path");
            Equal(string.Join("|", new[] { "song.mp3", "sorted", @"sorted\photo.jpg" }), string.Join("|", dialog.Plan!.Items.Select(item => item.RelativePath).Order(StringComparer.Ordinal)),
                "completing folder 1 path by path still rebuilds the tree");
        }
        finally { dialog.Close(); }
        var into2 = new CopyDialog(comparison, null, CopyDialog.Mode.IntoFolder2);
        try
        {
            AwaitPreview(into2);
            Equal(0, into2.Plan!.Items.Count, "folder 2 already has every file");
            Check(((TextBlock)into2.FindName("SummaryText")).Text.Contains("Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier"), "and the comparison proves it, so the dialog says so");
            Check(!((Button)into2.FindName("StartCopyButton")).IsEnabled, "an empty gathering cannot start");
        }
        finally { into2.Close(); }
    }

    private static void GatherSuggestsTheSubfolder()
    {
        using var workspace = new TestWorkspace();
        var seed = 1;
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            var body = Body(seed++);
            File.WriteAllBytes(Path.Combine(workspace.Source, name), body);
            Directory.CreateDirectory(Path.Combine(workspace.Target, "Backup"));
            File.WriteAllBytes(Path.Combine(workspace.Target, "Backup", name), body);
        }
        File.WriteAllText(Path.Combine(workspace.Source, "d.txt"), "new");
        File.WriteAllBytes(Path.Combine(workspace.Target, "photo.jpg"), Body(9));
        var comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        var folder = Path.Combine(workspace.Target, "Backup");
        var window = new MainWindow();
        try
        {
            ShowCompared(window, workspace, comparison);
            var detail = Control<TextBlock>(window, "VerdictDetail").Text;
            Check(detail.Contains($"« {folder} »") && detail.Contains("Pour regrouper dans le dossier 1, gardez cette comparaison : les 1 fichier(s) du dossier 2"),
                "the verdict names the subfolder, and what narrowing would leave out of a gathering into folder 1: " + detail);
        }
        finally { window.Close(); }
        var dialog = new CopyDialog(comparison, null, CopyDialog.Mode.IntoFolder2);
        try
        {
            AwaitPreview(dialog);
            var hint = (TextBlock)dialog.FindName("AlignmentText");
            Check(hint.Text.Contains(folder) && hint.Text.Contains("1 fichier(s) du dossier 2 situés hors de ce sous-dossier"), "the dialog names it, and what leaves the comparison: " + hint.Text);
            var instead = (Button)dialog.FindName("CompareInsteadButton");
            Check(instead.IsVisible || ((FrameworkElement)instead.Parent).Visibility == Visibility.Visible, "and offers to compare with it, for a copy into folder 2");
            Equal("d.txt", dialog.Plan!.Items.Single().RelativePath, "the gathering itself stays available");
            var direction = (ComboBox)dialog.FindName("DirectionBox");
            foreach (var (index, shown) in new[] { (0, false), (1, true), (2, false), (4, false) })
            {
                direction.SelectedIndex = index;
                AwaitPreview(dialog);
                Equal(shown, ((FrameworkElement)instead.Parent).Visibility == Visibility.Visible,
                    $"choice {index}: the subfolder is offered only when it receives the copies, never when it supplies them");
            }
        }
        finally { dialog.Close(); }
    }

    // REG-P02: "every file is there" only on the comparison's proof, never because nothing was left to copy.
    private static void GatherNeverClaimsCompletenessWhileContentsAreMissing()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Source, "rapport.docx"), Body(1));
        File.WriteAllBytes(Path.Combine(workspace.Target, "rapport.docx"), Body(2, 5100));
        File.WriteAllBytes(Path.Combine(workspace.Source, "commun.txt"), Body(3));
        File.WriteAllBytes(Path.Combine(workspace.Target, "commun.txt"), Body(3));
        var comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        foreach (var mode in new[] { CopyDialog.Mode.IntoFolder1, CopyDialog.Mode.IntoFolder2 })
        {
            var dialog = new CopyDialog(comparison, null, mode);
            try
            {
                AwaitPreview(dialog);
                var summary = ((TextBlock)dialog.FindName("SummaryText")).Text;
                Check(!summary.Contains("a déjà un exemplaire de chaque fichier"), $"{mode}: another version at the same path is no copy: " + summary);
                Check(summary.Contains("Rien à copier automatiquement") && summary.Contains("1 élément(s) resteront sans exemplaire prouvé"), $"{mode}: what stays behind is said: " + summary);
            }
            finally { dialog.Close(); }
        }
    }

    // REG-P03: an unfinished copy of the same folders comes first, even from a gathering button.
    private static void GatherButtonKeepsTheSavedCopyFirst()
    {
        using var workspace = new TestWorkspace();
        var comparison = CopyPreviewFixture(workspace);
        var saved = CopyEngine.CreateGatherPlan(comparison, intoTarget: false);
        var dialog = new CopyDialog(comparison, saved, CopyDialog.Mode.IntoFolder2);
        try
        {
            AwaitPreview(dialog);
            Equal(CopyDialog.Mode.Resume, dialog.SelectedMode, "the saved copy of these folders, gathered the other way, is resumed first");
        }
        finally { dialog.Close(); }
        using var elsewhere = new TestWorkspace();
        var other = CopyEngine.CreateBidirectionalPlan(CopyPreviewFixture(elsewhere));
        var unrelated = new CopyDialog(comparison, other, CopyDialog.Mode.IntoFolder2);
        try
        {
            AwaitPreview(unrelated);
            Equal(CopyDialog.Mode.IntoFolder2, unrelated.SelectedMode, "a saved copy of other folders does not override the chosen gathering");
        }
        finally { unrelated.Close(); }
    }

    // REG-P05, REG-P06, REG-P07: what the screen, the dialog, the details and the report say after or before a gathering.
    private static void GatherWordingMatchesWhatHappens()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Source, "photo.jpg"), Body(1));
        Directory.CreateDirectory(Path.Combine(workspace.Target, "sorted"));
        File.WriteAllBytes(Path.Combine(workspace.Target, "sorted", "photo.jpg"), Body(1));
        File.WriteAllText(Path.Combine(workspace.Target, "song.mp3"), "music");
        var comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        var window = new MainWindow();
        try
        {
            ShowCompared(window, workspace, comparison);
            Check(Control<TextBlock>(window, "VerdictDetail").Text.Contains("Le dossier 2 a un exemplaire de chaque fichier du dossier 1, parfois à un autre chemin"),
                "after files were gathered elsewhere, the comparison's proof is said");
            var grid = Control<DataGrid>(window, "ResultsGrid");
            grid.SelectedItem = grid.Items.Cast<RowView>().Single(view => view.Row.RelativePath == @"sorted\photo.jpg");
            Check(Control<TextBlock>(window, "SelectedPaths").Text.StartsWith("1 (même contenu, autre chemin) : "), "a copy at another path is never shown as the file at this path");
            var details = (string)typeof(MainWindow).GetMethod("Details", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [grid.SelectedItem])!;
            Check(details.Contains("Copie dans le dossier 1 : ") && details.Contains("SHA-256 (copie dans 1) : ") && !details.Contains("Non calculé"), "the copy's hash is given, as it was proven: " + details);

            // The containment answers of the screen and of the report agree.
            var csv = Path.Combine(workspace.Root, "rapport.csv");
            ReportWriter.Write(csv, comparison);
            var first = ReadCsv(csv)[1];
            Equal("Non", first[16], "the report says folder 2 lacks part of folder 1 as the screen does");

            // An unfinished copy of these folders is offered first, and the button says so.
            var saved = CopyEngine.CreateGatherPlan(comparison, intoTarget: false);
            typeof(MainWindow).GetField("_savedCopy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, saved);
            Invoke(window, "ShowResult", comparison);
            Equal("Reprendre la copie inachevée, puis regrouper dans le 1…", Control<Button>(window, "GatherInto1Button").Content, "resuming comes first");

            // After "Compare with this folder", the next verdict recalls what the narrowing leaves out, until a box is edited.
            var narrowing = typeof(MainWindow).GetField("_narrowedFrom", BindingFlags.Instance | BindingFlags.NonPublic)!;
            narrowing.SetValue(window, new FolderAlignment.Suggestion(true, workspace.Target, 3, 7));
            Invoke(window, "ShowResult", comparison);
            Check(Control<TextBlock>(window, "VerdictDetail").Text.Contains("Comparaison limitée à") && Control<TextBlock>(window, "VerdictDetail").Text.Contains("7 fichier(s)"),
                "the narrowed comparison is recalled");
            Control<TextBox>(window, "TargetBox").Text = workspace.Source;
            Check(narrowing.GetValue(window) is null, "editing a box forgets it");
        }
        finally { window.Close(); }

        var dialog = new CopyDialog(comparison, null, CopyDialog.Mode.IntoFolder1);
        try
        {
            AwaitPreview(dialog);
            Equal("Tout regrouper dans un dossier", ((TextBlock)dialog.FindName("DialogTitle")).Text, "the dialog names what it does");
            Equal("Seuls les contenus absents de ce dossier seront copiés.", ((TextBlock)dialog.FindName("ExclusionsText")).Text, "and what it copies");
        }
        finally { dialog.Close(); }

        var unsearched = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions(FindMovedFiles: false)).GetAwaiter().GetResult();
        var plain = new CopyDialog(unsearched, null, CopyDialog.Mode.IntoFolder1);
        try
        {
            AwaitPreview(plain);
            Check(((ComboBox)plain.FindName("DirectionBox")).Items.Cast<object>().All(item => !item.ToString()!.Contains("sans recopier")),
                "without the search for moved files, no choice promises not to copy what is already there");
        }
        finally { plain.Close(); }

        // The temporaries a replaced journal would leave behind are counted for the confirmation.
        var started = CopyEngine.CreateGatherPlan(comparison, intoTarget: false);
        var item = started.Items.Single(entry => entry.Kind == EntryKind.File);
        item.PartialIdentity = "started";
        var partial = Path.Combine(Path.GetDirectoryName(Path.Combine(started.TargetRoot, item.RelativePath))!, item.PartialName);
        File.WriteAllBytes(partial, new byte[1234]);
        var orphans = ((int Count, long Bytes))typeof(CopyDialog).GetMethod("OrphanTemporaries", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [started])!;
        Equal((1, 1234L), orphans, "one temporary of 1,234 bytes would be left behind");
    }

    private static byte[] Body(int seed, int length = 5000)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
