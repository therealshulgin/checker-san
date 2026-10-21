using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static DuplicateResult DuplicateUiFixture() => new()
    {
        Root = @"C:\fixture", CanonicalRoot = @"C:\fixture", SnapshotVerified = true, FilesScanned = 5,
        Groups =
        [
            new() { Hash = new string('A', 64), Size = 1024, Files =
            [
                new() { FullPath = @"C:\fixture\a.txt", RelativePath = "a.txt", Size = 1024 },
                new() { FullPath = @"C:\fixture\b.bin", RelativePath = "b.bin", Size = 1024 },
                new() { FullPath = @"C:\fixture\c.mp4", RelativePath = "c.mp4", Size = 1024 }
            ] },
            new() { Hash = new string('B', 64), Size = 2057, Files =
            [
                new() { FullPath = @"C:\fixture\d.wav", RelativePath = "d.wav", Size = 2057 },
                new() { FullPath = @"C:\fixture\e.zip", RelativePath = "e.zip", Size = 2057 }
            ] }
        ]
    };
    private static T DupControl<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void DupInvoke(DuplicatesWindow window, string method, params object[] args) =>
        typeof(DuplicatesWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static DuplicatesWindow DuplicateUiWindow(DuplicateResult? result = null)
    {
        var window = new DuplicatesWindow(@"C:\fixture");
        DupInvoke(window, "ApplyResult", result ?? DuplicateUiFixture());
        return window;
    }
    private static DuplicateRowView[] DuplicateUiRows(Window window) =>
        DupControl<DataGrid>(window, "DuplicateGrid").ItemsSource.Cast<DuplicateRowView>().ToArray();

    private static void DuplicateUiStartsUnselected()
    {
        var window = DuplicateUiWindow();
        try
        {
            Check(DuplicateUiRows(window).All(row => !row.IsSelected), "nothing is selected automatically");
            Check(!DupControl<Button>(window, "RemoveButton").IsEnabled, "cannot remove with no selection");
            Equal(0, DupControl<ComboBox>(window, "RemovalModeBox").SelectedIndex, "reversible quarantine is default");
            Check(DupControl<Button>(window, "SelectExtrasButton").IsEnabled, "explicit selection is available");
        }
        finally { window.Close(); }
    }
    private static void DuplicateUiKeepsOne()
    {
        var window = DuplicateUiWindow();
        try
        {
            DupInvoke(window, "SelectExtras_Click", window, new RoutedEventArgs());
            var rows = DuplicateUiRows(window);
            Equal(3, rows.Count(row => row.IsSelected), "selects only extras");
            Check(rows.GroupBy(row => row.GroupNumber).All(group => group.Count(row => !row.IsSelected) == 1), "one kept per group");
            Check(DupControl<Button>(window, "RemoveButton").IsEnabled, "valid selection can preview");
            rows[0].IsSelected = true;
            Check(!DupControl<Button>(window, "RemoveButton").IsEnabled, "all-selected group blocked even with another group keeper");
            rows[0].IsSelected = false;
            Check(DupControl<Button>(window, "RemoveButton").IsEnabled, "restoring keeper enables preview");
            DupInvoke(window, "ClearSelection_Click", window, new RoutedEventArgs());
            Check(rows.All(row => !row.IsSelected), "clear resets every checkbox");
        }
        finally { window.Close(); }
    }
    private static void DuplicateUiChangedPath()
    {
        var window = DuplicateUiWindow();
        try
        {
            DupInvoke(window, "SelectExtras_Click", window, new RoutedEventArgs());
            DupControl<TextBox>(window, "FolderBox").Text = @"C:\another-folder";
            Check(DupControl<DataGrid>(window, "DuplicateGrid").ItemsSource == null, "old rows removed");
            Check(!DupControl<Button>(window, "RemoveButton").IsEnabled, "old selections cannot delete");
        }
        finally { window.Close(); }
    }
    private static void DuplicateUiIncomplete()
    {
        foreach (var result in new[] { DuplicateUiFixture() with { SnapshotVerified = false }, DuplicateUiFixture() with { Cancelled = true }, DuplicateUiFixture() with { Errors = ["network lost"] } })
        {
            var window = DuplicateUiWindow(result);
            try
            {
                DuplicateUiRows(window)[1].IsSelected = true;
                Check(!DupControl<Button>(window, "RemoveButton").IsEnabled, "incomplete scans cannot remove");
                Check(!DupControl<Button>(window, "SelectExtrasButton").IsEnabled, "cannot bulk-select incomplete result");
            }
            finally { window.Close(); }
        }
    }
    private static void DuplicateUiPermanentPreview()
    {
        var result = DuplicateUiFixture();
        var window = new DuplicateRemovalDialog(result, [@"C:\fixture\b.bin"], DuplicateRemovalMode.Permanent);
        try
        {
            var button = DupControl<Button>(window, "ConfirmRemovalButton");
            Check(!button.IsEnabled, "permanent action not acknowledged by default");
            var text = DupControl<TextBox>(window, "RemovalPreview").Text;
            Check(text.Contains(@"RETIRER   C:\fixture\b.bin") && text.Contains(@"CONSERVER   C:\fixture\a.txt"), "exact kept and deleted paths displayed");
            DupControl<CheckBox>(window, "PermanentConsent").IsChecked = true;
            Check(button.IsEnabled, "acknowledgement enables valid selection");
            DupControl<CheckBox>(window, "PermanentConsent").IsChecked = false;
            Check(!button.IsEnabled, "revoking acknowledgement disables action");
        }
        finally { window.Close(); }
    }
    private static void DuplicateUiRejectsWholeGroup()
    {
        var scan = DuplicateUiFixture();
        var window = new DuplicateRemovalDialog(scan, scan.Groups[0].Files.Select(file => file.FullPath).ToArray(), DuplicateRemovalMode.Quarantine);
        try { Check(!DupControl<Button>(window, "ConfirmRemovalButton").IsEnabled, "even quarantine must keep a copy"); }
        finally { window.Close(); }
    }
}
