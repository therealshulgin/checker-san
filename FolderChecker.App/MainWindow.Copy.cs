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
        TransferButton.IsEnabled = _cancellation == null && ((_result is not null && CopyEngine.CanCreatePlan(_result)) || (_savedCopy?.Items.Any(i => !i.Completed) == true));
        TransferButton.Content = _savedCopy?.Items.Any(i => !i.Completed) == true ? L.T("Compléter / reprendre…", "Complete / resume…") : L.T("Compléter les dossiers…", "Complete folders…");
    }
    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        RefreshCopy();
        var dialog = new CopyDialog(_result, _savedCopy) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Plan is not {} plan) return;
        // A copy invalidates every previous comparison verdict as soon as writes may begin.
        _refreshStatus = null;
        _result = null; _rows = []; _view = null; ResultsGrid.ItemsSource = null;
        ExportButton.IsEnabled = false;
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        SourceBox.Text = plan.SourceRoot; TargetBox.Text = plan.TargetRoot;
        _cancellation = new CancellationTokenSource(); SetBusy(true);
        TransferButton.IsEnabled = false;
        VerdictTitle.Text = plan.Bidirectional ? L.T("Complétion des deux dossiers", "Completing both folders") : L.T("Copie des éléments manquants", "Copying missing items");
        VerdictCard.Background = RowView.Brush("#242424");
        EmptyState.Visibility = Visibility.Visible; EmptyTitle.Text = L.T("Copie avec reprise et vérification SHA-256", "Resumable copy with SHA-256 verification");
        EmptyDetail.Text = L.T("Les fichiers existants ne seront pas écrasés.", "Existing files will not be overwritten.");
        ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Visible; ScanProgress.IsIndeterminate = true;
        _clock.Restart(); long lastUpdate = 0;
        var progress = new Progress<CopyProgress>(p =>
        {
            if (_cancellation == null) return;
            if (_clock.ElapsedMilliseconds - lastUpdate < 100 && p.RetryAttempt == 0) return;
            lastUpdate = _clock.ElapsedMilliseconds;
            VerdictDetail.Text = L.F($"{p.Phase} · {p.CompletedFiles:N0} / {p.TotalFiles:N0} fichiers terminés", $"{p.Phase} · {p.CompletedFiles:N0} / {p.TotalFiles:N0} files completed");
            ProgressDetail.Text = L.F($"{RowView.FormatSize(p.BytesWritten)} copiés · {RowView.FormatSize(p.BytesResumed)} repris · {p.CurrentPath}", $"{RowView.FormatSize(p.BytesWritten)} copied · {RowView.FormatSize(p.BytesResumed)} resumed · {p.CurrentPath}");
            FooterText.Text = L.T("Copie en cours • Arrêter et garder permet de reprendre plus tard", "Copy in progress • Stop and save to resume later");
        });
        try
        {
            var copy = await new CopyEngine().CopyAsync(plan, _copyCheckpointPath, progress, _cancellation.Token);
            SetStatus(() =>
            {
                VerdictTitle.Text = copy.Complete ? L.T("Copie terminée et vérifiée", "Copy complete and verified") : copy.Cancelled ? L.T("Copie interrompue — reprise disponible", "Copy stopped — resume available") : L.T("Copie incomplète — des éléments restent à traiter", "Copy incomplete — some items still need processing");
                VerdictDetail.Text = copy.Complete ? L.T("Les chemins manquants ont été copiés et vérifiés par SHA-256. Les contenus différents n’ont pas été remplacés. Relancez une comparaison pour contrôler les écarts restants.", "Missing paths have been copied and verified with SHA-256. Differing contents were not replaced. Run another comparison to check remaining differences.") : L.T("Utilisez « Compléter / reprendre… » pour poursuivre. ", "Use Complete / resume to continue. ") + string.Join(" ", copy.Errors.Take(3));
                VerdictCard.ToolTip = copy.Errors.Count > 0 ? string.Join("\n", copy.Errors) : null;
                VerdictCard.Background = RowView.Brush("#242424");
                FooterText.Text = L.F($"{copy.CompletedFiles:N0} / {copy.TotalFiles:N0} fichiers terminés · {RowView.FormatSize(copy.BytesWritten)} copiés · {RowView.FormatSize(copy.BytesResumed)} repris · {copy.Errors.Count:N0} incident(s)", $"{copy.CompletedFiles:N0} / {copy.TotalFiles:N0} files completed · {RowView.FormatSize(copy.BytesWritten)} copied · {RowView.FormatSize(copy.BytesResumed)} resumed · {copy.Errors.Count:N0} issue(s)");
                EmptyTitle.Text = copy.Complete ? L.T("Prêt pour une nouvelle comparaison", "Ready for another comparison") : L.T("Le journal de copie conserve l’avancement", "Copy progress is saved in the journal");
            });
        }
        catch (Exception ex) { SetStatus(() => { VerdictTitle.Text = L.T("Copie non aboutie", "Copy did not complete"); VerdictDetail.Text = ex.Message; }); }
        finally
        {
            _clock.Stop(); _cancellation.Dispose(); _cancellation = null;
            SetBusy(false); RefreshCheckpoint(); RefreshCopy();
            ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Collapsed;
            if (_closeAfterCancel) Close();
        }
    }
}
