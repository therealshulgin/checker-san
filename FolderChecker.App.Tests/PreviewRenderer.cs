using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    // Render this application's own WPF layout, using synthetic data and no desktop input.
    private static void RenderPreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        void Render(Window window, string name, int width, int height)
        {
            try
            {
                var content = (FrameworkElement)window.Content;
                content.SetValue(TextElement.ForegroundProperty, window.Foreground);
                content.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
                content.SetValue(TextElement.FontSizeProperty, window.FontSize);
                window.Content = null;
                var canvas = new Border { Background = window.Background, Child = content };
                canvas.Measure(new Size(width, height));
                canvas.Arrange(new Rect(0, 0, width, height));
                canvas.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, name + ".png")); png.Save(output);
                Console.WriteLine(Path.Combine(directory, name + ".png"));
            }
            finally { window.Close(); }
        }
        var main = new MainWindow();
        Render(main, "main", 1220, 900);
        using (var workspace = new TestWorkspace())
        {
            // Folder 2 holds folder 1 in a subfolder, with files missing on each side and one unreadable file.
            var seed = 1;
            foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            {
                var body = Body(seed++);
                File.WriteAllBytes(Path.Combine(workspace.Source, name), body);
                Directory.CreateDirectory(Path.Combine(workspace.Target, "Sauvegarde"));
                File.WriteAllBytes(Path.Combine(workspace.Target, "Sauvegarde", name), body);
            }
            File.WriteAllText(Path.Combine(workspace.Source, "nouveau.txt"), "only in 1");
            File.WriteAllText(Path.Combine(workspace.Target, "musique.mp3"), "only in 2");
            File.WriteAllText(Path.Combine(workspace.Source, "verrou.txt"), "same");
            File.WriteAllText(Path.Combine(workspace.Target, "verrou.txt"), "same");
            ComparisonResult compared;
            using (new FileStream(Path.Combine(workspace.Source, "verrou.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                compared = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
            var gather = new MainWindow();
            ShowCompared(gather, workspace, compared);
            Render(gather, "main-gather", 1220, 900);
            var dialog = new CopyDialog(compared, null, CopyDialog.Mode.IntoFolder2);
            AwaitPreview(dialog);
            Render(dialog, "gather-dialog", 880, 760);
        }
        var duplicates = DuplicateUiWindow();
        DupInvoke(duplicates, "SelectExtras_Click", duplicates, new RoutedEventArgs());
        Render(duplicates, "duplicates", 1100, 850);
        var small = DuplicateUiWindow();
        Render(small, "duplicates-small", 850, 620);
        Render(new SnapshotWindow(@"C:\Users\Photos"), "snapshots", 860, 600);
        var store = Path.Combine(Path.GetTempPath(), "checkersan-preview-" + Guid.NewGuid().ToString("N"), "pairs.json");
        try
        {
            var photos = PairStore.Save(new SavedPair { Name = "Photos ⇄ Sauvegarde", Folder1 = @"C:\Users\Photos", Folder2 = @"G:\Sauvegarde\Photos",
                Schedule = new PairSchedule(ScheduleFrequency.Weekly, DayOfWeek.Sunday, new TimeOnly(12, 0)) }, store);
            PairStore.RecordRun(photos.Id, new PairRun(new DateTimeOffset(2026, 10, 19, 12, 4, 0, TimeSpan.FromHours(2)), ExitCodes.Differences, 3, 0, 0, @"C:\reports\r.json"), store);
            PairStore.Save(new SavedPair { Name = "Documents ⇄ NAS", Folder1 = @"C:\Users\Documents", Folder2 = @"\\NAS\Documents" }, store);
            Render(new PairsWindow(@"C:\Users\Musique", @"G:\Sauvegarde\Musique", true, store, @"C:\Apps\CheckerSan.exe"), "pairs", 900, 760);
            Render(new ScheduledResultWindow(PairStore.Load(store)[0], PairStore.Load(store)[0].LastRun!), "scheduled-result", 660, 330);
        }
        finally { Directory.Delete(Path.GetDirectoryName(store)!, recursive: true); }
        var filters = DuplicateUiWindow();
        DupControl<Expander>(filters, "FiltersExpander").IsExpanded = true;
        Render(filters, "duplicates-filters", 1100, 980);
        Render(new DuplicateRemovalDialog(DuplicateUiFixture(), [@"C:\fixture\b.bin", @"C:\fixture\c.mp4", @"C:\fixture\e.zip"], DuplicateRemovalMode.Permanent), "permanent-preview", 830, 670);
    }
}
