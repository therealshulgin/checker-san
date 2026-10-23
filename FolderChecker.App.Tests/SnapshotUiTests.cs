using System.Windows.Controls;
using FolderChecker.App;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static void SnapshotWindowStartsFromFolderOne()
    {
        var main = new MainWindow();
        var window = new SnapshotWindow(@"C:\fixture");
        try
        {
            Equal("Instantanés…", Control<Button>(main, "SnapshotsButton").Content, "the header offers snapshots");
            Equal(@"C:\fixture", ((TextBox)window.FindName("SnapshotFolderBox")).Text, "the snapshot window starts from folder 1");
            Check(((ListBox)window.FindName("SnapshotResults")).ItemsSource is null, "nothing is read before an explicit action");
        }
        finally { window.Close(); main.Close(); }
    }
}
