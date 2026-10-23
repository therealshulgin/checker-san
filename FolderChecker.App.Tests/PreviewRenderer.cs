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
        var duplicates = DuplicateUiWindow();
        DupInvoke(duplicates, "SelectExtras_Click", duplicates, new RoutedEventArgs());
        Render(duplicates, "duplicates", 1100, 850);
        var small = DuplicateUiWindow();
        Render(small, "duplicates-small", 850, 620);
        var filters = DuplicateUiWindow();
        DupControl<Expander>(filters, "FiltersExpander").IsExpanded = true;
        Render(filters, "duplicates-filters", 1100, 980);
        Render(new DuplicateRemovalDialog(DuplicateUiFixture(), [@"C:\fixture\b.bin", @"C:\fixture\c.mp4", @"C:\fixture\e.zip"], DuplicateRemovalMode.Permanent), "permanent-preview", 830, 670);
    }
}
