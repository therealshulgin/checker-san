using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

/// <summary>Shown after a scheduled check that found differences or could not prove everything. It never takes the focus.</summary>
public sealed class ScheduledResultWindow : Window
{
    /// <param name="launch">Starts Checker San with these arguments; a separate normal process unless a test replaces it.</param>
    public ScheduledResultWindow(SavedPair? pair, PairRun run, string? store = null, Action<IReadOnlyList<string>>? launch = null)
    {
        Title = "Checker San"; Width = 660; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowActivated = false;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        NameScope.SetNameScope(this, new NameScope());
        var panel = new StackPanel { Margin = new Thickness(24) };
        var title = new TextBlock
        {
            Text = run.ExitCode switch
            {
                ExitCodes.Differences => L.T("Vérification planifiée : des écarts sont prouvés", "Scheduled check: differences are proven"),
                ExitCodes.Incomplete => L.T("Vérification planifiée incomplète", "Scheduled check incomplete"),
                _ => L.T("Vérification planifiée impossible", "Scheduled check failed")
            },
            FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = RowView.Brush(run.ExitCode == ExitCodes.Differences ? "#F1A9B7" : "#E8C395")
        };
        RegisterName("ScheduledTitle", title); panel.Children.Add(title);
        var detail = new TextBlock
        {
            Text = (pair is null ? "" : $"{pair.Name}\n{pair.Folder1}  ⇄  {pair.Folder2}\n") +
                ((pair ?? new SavedPair()) with { LastRun = run }).LastRunText +
                (run.Failure is not null && run.ExitCode != ExitCodes.Failure ? "\n" + run.Failure : "") +
                (run.ReportPath is null ? "" : "\n" + L.T("Rapport : ", "Report: ") + run.ReportPath),
            TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#C8C8C8"), Margin = new Thickness(0, 12, 0, 8), LineHeight = 20
        };
        RegisterName("ScheduledDetail", detail); panel.Children.Add(detail);
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Rien n’a été copié ni supprimé. Ouvrez la paire dans Checker San pour voir chaque écart et décider quoi faire.",
                "Nothing was copied or deleted. Open the pair in Checker San to see each difference and decide what to do."),
            TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#ACACAC"), Margin = new Thickness(0, 0, 0, 18)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var report = new Button { Content = L.T("Ouvrir le rapport", "Open the report"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = run.ReportPath is not null };
        var open = new Button { Content = L.T("Ouvrir dans Checker San", "Open in Checker San"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = pair is not null, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var close = new Button { Content = L.T("Fermer", "Close"), IsCancel = true };
        RegisterName("ScheduledReportButton", report); RegisterName("ScheduledOpenButton", open);
        buttons.Children.Add(report); buttons.Children.Add(open); buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
        report.Click += (_, _) =>
        {
            if (run.ReportPath is not null && File.Exists(run.ReportPath))
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + run.ReportPath + "\"", UseShellExecute = true });
        };
        open.Click += (_, _) =>
        {
            if (pair is null) return;
            // A normal app of its own, not part of this result window's process.
            (launch ?? App.StartSelf)(store is null ? ["--pair", pair.Id] : ["--pair", pair.Id, "--pairs", store]);
            Close();
        };
        close.Click += (_, _) => Close();
    }
}
