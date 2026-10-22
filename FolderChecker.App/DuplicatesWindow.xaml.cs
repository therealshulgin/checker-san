using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;
using Microsoft.Win32;

namespace FolderChecker.App;

public partial class DuplicatesWindow : Window
{
    private readonly string _checkpointPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Sessions", "duplicates.json");
    private CancellationTokenSource? _cancellation;
    private DuplicateResult? _result;
    private List<DuplicateRowView> _rows = [];
    private bool _closeAfterCancel, _updatingSelection;
    private string? _quarantinePath;
    private long _lastProgress;
    public bool FilesChanged { get; private set; }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer { Content: Grid content }) content.Height = Math.Max(710, e.NewSize.Height - 44);
    }

    public DuplicatesWindow(string initialFolder = "")
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 30);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        FolderBox.Text = initialFolder;
        Loaded += async (_, _) =>
        {
            var savedRoot = await Task.Run(() => DuplicateEngine.ReadCheckpointRoot(_checkpointPath));
            if (_cancellation != null || savedRoot == null) return;
            if (string.IsNullOrWhiteSpace(FolderBox.Text)) FolderBox.Text = savedRoot;
            ResumeText.Text = L.T("Une recherche est enregistrée. Analyser / reprendre réutilise les fichiers encore valides.", "A scan is saved. Scan / resume reuses files that are still valid.");
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = L.T("Dossier à analyser", "Folder to scan"), Multiselect = false };
        if (!string.IsNullOrWhiteSpace(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text.Trim().Trim('"');
        if (picker.ShowDialog(this) == true) FolderBox.Text = picker.FolderName;
    }

    private void Folder_Changed(object sender, TextChangedEventArgs e)
    {
        if (_result == null || _cancellation != null) return;
        ClearResult();
        StatusText.Text = L.T("Le dossier a changé — relancez l’analyse", "Folder changed — run the scan again");
        StatsText.Text = L.T("Les résultats précédents ne s’appliquent plus à cette sélection.", "Previous results no longer apply to this selection.");
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private async Task ScanAsync()
    {
        if (_cancellation != null) return;
        ClearResult();
        ShowIssues([]);
        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        var timer = Stopwatch.StartNew();
        _lastProgress = 0;
        StatusText.Text = L.T("Préparation de l’analyse…", "Preparing scan…");
        var active = _cancellation;
        var progress = new Progress<DuplicateProgress>(p =>
        {
            if (_cancellation != active || (Environment.TickCount64 - _lastProgress < 100 && p.RetryAttempt == 0)) return;
            _lastProgress = Environment.TickCount64;
            StatusText.Text = p.Phase;
            StatsText.Text = L.F($"{p.FilesScanned:N0} fichiers · {RowView.FormatSize(p.BytesRead)} lus · {p.FilesReused:N0} repris · {timer.Elapsed:hh\\:mm\\:ss}", $"{p.FilesScanned:N0} files · {RowView.FormatSize(p.BytesRead)} read · {p.FilesReused:N0} reused · {timer.Elapsed:hh\\:mm\\:ss}");
            CurrentPathText.Text = p.CurrentPath;
        });
        try
        {
            var result = await new DuplicateEngine().ScanAsync(FolderBox.Text, new DuplicateScanOptions
            { CheckpointPath = _checkpointPath, ResumeFromCheckpoint = ReuseCheck.IsChecked == true }, progress, active.Token);
            ApplyResult(result);
        }
        catch (Exception ex) { StatusText.Text = L.T("Analyse impossible", "Unable to scan"); ShowIssues([ex.Message]); }
        finally { FinishOperation(); }
    }

    private void ApplyResult(DuplicateResult result)
    {
        ClearResult();
        _result = result;
        _rows = result.Groups.SelectMany((group, index) => group.Files.Select(file => new DuplicateRowView(index + 1, file))).ToList();
        foreach (var row in _rows) row.PropertyChanged += Selection_Changed;
        DuplicateGrid.ItemsSource = _rows;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool complete = CanRemove;
        StatusText.Text = !complete ? result.Cancelled ? L.T("Analyse arrêtée — reprenez pour terminer", "Scan stopped — resume to finish") : L.T("Analyse incomplète — aucun retrait autorisé", "Scan incomplete — removal is disabled")
            : result.Groups.Count == 0 ? L.T("Aucun doublon trouvé", "No duplicates found") : L.F($"{result.Groups.Count:N0} groupe(s) de fichiers identiques", $"{result.Groups.Count:N0} group(s) of identical files");
        var extras = result.Groups.Sum(group => group.Files.Count - 1);
        var bytes = result.Groups.Sum(group => group.Size * (group.Files.Count - 1));
        StatsText.Text = L.F($"{result.FilesScanned:N0} fichiers · {extras:N0} copies supplémentaires · {RowView.FormatSize(bytes)} de contenu en double\n", $"{result.FilesScanned:N0} files · {extras:N0} extra copies · {RowView.FormatSize(bytes)} of duplicate contents\n") +
            L.F($"{RowView.FormatSize(result.BytesRead)} lus · {result.FilesReused:N0} repris · {result.FilesHashed:N0} fichiers vérifiés par SHA-256", $"{RowView.FormatSize(result.BytesRead)} read · {result.FilesReused:N0} reused · {result.FilesHashed:N0} files verified with SHA-256");
        EmptyText.Text = complete ? L.T("Aucun groupe de fichiers identiques dans ce dossier.", "No groups of identical files in this folder.") : L.T("La recherche doit être complète avant de retirer des fichiers.", "The scan must finish before files can be removed.");
        CurrentPathText.Text = "";
        ResumeText.Text = result.CheckpointSaved ? L.T("Progression enregistrée. Analyser / reprendre vérifie les changements avant de réutiliser le cache.", "Progress saved. Scan / resume checks for changes before reusing cached results.") : L.T("3 nouvelles tentatives en cas de coupure réseau.", "3 retries after a network interruption.");
        ShowIssues(result.Errors.Concat(result.Warnings));
        UpdateSelection();
    }

    private bool CanRemove => _result is { Cancelled: false, SnapshotVerified: true } && _result.Errors.Count == 0;

    private void ClearResult()
    {
        foreach (var row in _rows) row.PropertyChanged -= Selection_Changed;
        _result = null; _rows = [];
        DuplicateGrid.ItemsSource = null;
        EmptyText.Visibility = Visibility.Visible;
        UpdateSelection();
    }

    private void Selection_Changed(object? sender, PropertyChangedEventArgs e)
    { if (!_updatingSelection && e.PropertyName == nameof(DuplicateRowView.IsSelected)) UpdateSelection(); }

    private void UpdateSelection()
    {
        if (RemoveButton == null) return;
        var selected = _rows.Where(row => row.IsSelected).ToArray();
        bool invalid = _rows.GroupBy(row => row.GroupNumber).Any(group => group.All(row => row.IsSelected));
        SelectionText.Text = invalid ? L.T("Gardez au moins un exemplaire dans chaque groupe : désélectionnez un fichier.", "Keep at least one copy in every group: deselect a file.")
            : selected.Length == 0 ? L.T("Rien n’est sélectionné. Gardez au moins un exemplaire par groupe.", "Nothing selected. Keep at least one copy in each group.")
            : L.F($"{selected.Length:N0} fichier(s) à retirer · {RowView.FormatSize(selected.Sum(row => row.File.Size))}. Les fichiers non cochés restent en place.", $"{selected.Length:N0} file(s) to remove · {RowView.FormatSize(selected.Sum(row => row.File.Size))}. Unchecked files stay in place.");
        RemoveButton.IsEnabled = _cancellation == null && CanRemove && selected.Length > 0 && !invalid;
        SelectExtrasButton.IsEnabled = ClearSelectionButton.IsEnabled = _cancellation == null && CanRemove && _rows.Count > 0;
        DuplicateGrid.IsEnabled = _cancellation == null && CanRemove;
    }

    private void SelectExtras_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRemove || _cancellation != null) return;
        _updatingSelection = true;
        foreach (var group in _rows.GroupBy(row => row.GroupNumber))
        {
            var ordered = group.OrderBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int i = 0; i < ordered.Length; i++) ordered[i].IsSelected = i > 0;
        }
        _updatingSelection = false; UpdateSelection();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _updatingSelection = true;
        foreach (var row in _rows) row.IsSelected = false;
        _updatingSelection = false; UpdateSelection();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModeDetailText == null) return;
        ModeDetailText.Text = RemovalModeBox.SelectedIndex == 1
            ? L.T("Suppression définitive, sans passage par la Corbeille. Un récapitulatif et une confirmation sont requis. Le contenu principal et les métadonnées sont recontrôlés avant l’action.", "Permanent deletion, without the Recycle Bin. A preview and confirmation are required. Main file contents and metadata are checked again before removal.")
            : L.T("La mise à l’écart déplace les copies dans .CheckerSan-Quarantine, sous ce dossier. Leur arborescence est conservée pour une restauration manuelle. Aucun espace n’est libéré.", "Moving aside places copies in .CheckerSan-Quarantine inside this folder. Their folder structure is kept for manual restoration. No space is freed.");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null || !CanRemove || !RemoveButton.IsEnabled) return;
        var scan = _result!;
        var paths = _rows.Where(row => row.IsSelected).Select(row => row.File.FullPath).ToArray();
        var mode = RemovalModeBox.SelectedIndex == 1 ? DuplicateRemovalMode.Permanent : DuplicateRemovalMode.Quarantine;
        var preview = new DuplicateRemovalDialog(scan, paths, mode) { Owner = this };
        if (preview.ShowDialog() != true) return;
        _cancellation = new CancellationTokenSource();
        var active = _cancellation;
        SetBusy(true); _lastProgress = 0;
        StatusText.Text = L.T("Vérification des fichiers avant retrait…", "Verifying files before removal…");
        var progress = new Progress<DuplicateRemovalProgress>(p =>
        {
            if (_cancellation != active || Environment.TickCount64 - _lastProgress < 100) return;
            _lastProgress = Environment.TickCount64;
            StatusText.Text = p.Phase;
            CurrentPathText.Text = p.CurrentPath;
            StatsText.Text = L.F($"{p.CompletedFiles:N0} / {p.TotalFiles:N0} fichiers retirés · {RowView.FormatSize(p.BytesVerified)} revérifiés", $"{p.CompletedFiles:N0} / {p.TotalFiles:N0} files removed · {RowView.FormatSize(p.BytesVerified)} verified again");
        });
        // Invalidate all previous comparison verdicts even if an interrupted remote action has an uncertain outcome.
        FilesChanged = true;
        try
        {
            var result = await new DuplicateRemoval().DeleteAsync(scan, paths, mode, progress, active.Token);
            ClearResult();
            StatusText.Text = result.Complete ? L.T("Opération terminée — relancez l’analyse", "Operation finished — run the scan again") : L.T("Opération interrompue — relancez l’analyse", "Operation interrupted — run the scan again");
            StatsText.Text = mode == DuplicateRemovalMode.Permanent
                ? L.F($"{result.DeletedPaths.Count:N0} fichier(s) supprimé(s) · {RowView.FormatSize(result.BytesRemoved)} de contenu retiré", $"{result.DeletedPaths.Count:N0} file(s) deleted · {RowView.FormatSize(result.BytesRemoved)} of contents removed")
                : L.F($"{result.DeletedPaths.Count:N0} fichier(s) mis à l’écart · {RowView.FormatSize(result.BytesMoved)} déplacés · aucun espace libéré", $"{result.DeletedPaths.Count:N0} file(s) moved aside · {RowView.FormatSize(result.BytesMoved)} moved · no space freed");
            ShowIssues(result.Errors.Concat(result.DeletedPaths.Select(path => L.T("Retiré : ", "Removed: ") + path)));
            if (result.QuarantinePath is {} quarantine)
            {
                _quarantinePath = quarantine;
                OpenQuarantineButton.Visibility = Visibility.Visible;
                StatsText.Text += L.T("\nRestauration manuelle : replacez les fichiers à leur chemin d’origine depuis la mise à l’écart.", "\nManual restoration: move files from the quarantine folder back to their original paths.");
                CurrentPathText.Text = quarantine;
                CurrentPathText.ToolTip = quarantine;
            }
            else CurrentPathText.Text = "";
        }
        catch (Exception ex) { ClearResult(); StatusText.Text = L.T("Opération interrompue — relancez l’analyse", "Operation interrupted — run the scan again"); ShowIssues([ex.Message]); }
        finally { FinishOperation(); }
    }

    private void ShowIssues(IEnumerable<string> issues)
    {
        var lines = issues.ToArray();
        IssuesExpander.Visibility = lines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        IssuesText.Text = string.Join(Environment.NewLine, lines);
    }

    private void SetBusy(bool busy)
    {
        FolderBox.IsEnabled = BrowseButton.IsEnabled = ReuseCheck.IsEnabled = ScanButton.IsEnabled = RemovalModeBox.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
        WorkProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }

    private void FinishOperation()
    {
        _cancellation?.Dispose(); _cancellation = null;
        SetBusy(false);
        if (_closeAfterCancel) Close();
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel(); StopButton.IsEnabled = false;
        StatusText.Text = L.T("Arrêt demandé — en attente de Windows…", "Stop requested — waiting for Windows…");
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellation == null) return;
        e.Cancel = true; _closeAfterCancel = true; _cancellation.Cancel();
        StatusText.Text = L.T("Arrêt et sauvegarde avant fermeture…", "Stopping and saving before closing…");
    }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (DuplicateGrid.SelectedItem is DuplicateRowView row) OpenExplorer(row.File.FullPath, true);
    }
    private void OpenQuarantine_Click(object sender, RoutedEventArgs e)
    { if (_quarantinePath != null) OpenExplorer(_quarantinePath, false); }
    private void OpenExplorer(string path, bool select)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = (select ? "/select," : "") + "\"" + path + "\"", UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Checker San"); }
    }
}
