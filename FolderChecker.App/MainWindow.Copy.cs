using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FolderChecker.Core;

namespace FolderChecker.App;

public partial class MainWindow
{
    private readonly string _copyCheckpointPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Sessions", "copy.json");
    private CopyPlan? _savedCopy;
    internal static void ApplyDarkTitleBar(Window window)
    { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int)); }
    private void RefreshCopy()
    {
        _savedCopy = CopyEngine.ReadPlan(_copyCheckpointPath);
        TransferButton.IsEnabled = _cancellation == null && ((_result is { Cancelled: false, SnapshotVerified: true } && _result.Errors.Count == 0 &&
            _result.Rows.All(row => row.Status != ComparisonStatus.Unverified)) || (_savedCopy?.Items.Any(i => !i.Completed) == true));
        TransferButton.Content = _savedCopy?.Items.Any(i => !i.Completed) == true ? "Compléter / reprendre…" : "Compléter les dossiers…";
    }
    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        RefreshCopy();
        var dialog = new CopyDialog(_result, _savedCopy) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Plan is not {} plan) return;
        // A copy invalidates every previous comparison verdict as soon as writes may begin.
        _result = null; _rows = []; _view = null; ResultsGrid.ItemsSource = null;
        ExportButton.IsEnabled = false;
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        SourceBox.Text = plan.SourceRoot; TargetBox.Text = plan.TargetRoot;
        _cancellation = new CancellationTokenSource(); SetBusy(true);
        TransferButton.IsEnabled = false;
        VerdictTitle.Text = plan.Bidirectional ? "Complétion des deux dossiers" : "Copie des éléments manquants";
        VerdictCard.Background = RowView.Brush("#242424");
        EmptyState.Visibility = Visibility.Visible; EmptyTitle.Text = "Copie avec reprise et vérification SHA-256";
        EmptyDetail.Text = "Les fichiers existants ne seront pas écrasés.";
        ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Visible; ScanProgress.IsIndeterminate = true;
        _clock.Restart(); long lastUpdate = 0;
        var progress = new Progress<CopyProgress>(p =>
        {
            if (_cancellation == null) return;
            if (_clock.ElapsedMilliseconds - lastUpdate < 100 && p.RetryAttempt == 0) return;
            lastUpdate = _clock.ElapsedMilliseconds;
            VerdictDetail.Text = $"{p.Phase} · {p.CompletedFiles:N0} / {p.TotalFiles:N0} fichiers terminés";
            ProgressDetail.Text = $"{RowView.FormatSize(p.BytesWritten)} copiés · {RowView.FormatSize(p.BytesResumed)} repris · {p.CurrentPath}";
            FooterText.Text = "Copie en cours • Arrêter et garder permet de reprendre plus tard";
        });
        try
        {
            var copy = await new CopyEngine().CopyAsync(plan, _copyCheckpointPath, progress, _cancellation.Token);
            VerdictTitle.Text = copy.Complete ? "Copie terminée et vérifiée" : copy.Cancelled ? "Copie interrompue — reprise disponible" : "Copie incomplète — des éléments restent à traiter";
            VerdictDetail.Text = copy.Complete ? "Les chemins manquants ont été copiés et vérifiés par SHA-256. Les contenus différents n’ont pas été remplacés. Relancez une comparaison pour contrôler les écarts restants." : "Utilisez « Compléter / reprendre… » pour poursuivre. " + string.Join(" ", copy.Errors.Take(3));
            VerdictCard.ToolTip = copy.Errors.Count > 0 ? string.Join("\n", copy.Errors) : null;
            VerdictCard.Background = RowView.Brush("#242424");
            FooterText.Text = $"{copy.CompletedFiles:N0} / {copy.TotalFiles:N0} fichiers terminés · {RowView.FormatSize(copy.BytesWritten)} copiés · {RowView.FormatSize(copy.BytesResumed)} repris · {copy.Errors.Count:N0} incident(s)";
            EmptyTitle.Text = copy.Complete ? "Prêt pour une nouvelle comparaison" : "Le journal de copie conserve l’avancement";
        }
        catch (Exception ex) { VerdictTitle.Text = "Copie non aboutie"; VerdictDetail.Text = ex.Message; }
        finally
        {
            _clock.Stop(); _cancellation.Dispose(); _cancellation = null;
            SetBusy(false); RefreshCheckpoint(); RefreshCopy();
            ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Collapsed;
            if (_closeAfterCancel) Close();
        }
    }
}
