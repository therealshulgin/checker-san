using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;
using Microsoft.Win32;

namespace FolderChecker.App;

public partial class DuplicatesWindow : Window
{
    private readonly string _checkpointPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Sessions", "duplicates.json");
    private readonly string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Logs", "duplicate-actions.csv");
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
        ExcludeBox.Text = string.Join("; ", DuplicateScanOptions.DefaultExcludedNames);
        Loaded += async (_, _) =>
        {
            var savedRoot = await Task.Run(() => DuplicateEngine.ReadCheckpointRoot(_checkpointPath));
            if (_cancellation == null && savedRoot != null)
            {
                if (string.IsNullOrWhiteSpace(FolderBox.Text)) FolderBox.Text = savedRoot;
                ResumeText.Text = L.T("Une recherche est enregistrée. Analyser / reprendre réutilise les fichiers encore valides.", "A scan is saved. Scan / resume reuses files that are still valid.");
            }
            RefreshRestore();
        };
    }

    private DuplicateRemovalMode SelectedMode => (RemovalModeBox.SelectedItem as ComboBoxItem)?.Tag switch
    {
        "RecycleBin" => DuplicateRemovalMode.RecycleBin,
        "Permanent" => DuplicateRemovalMode.Permanent,
        _ => DuplicateRemovalMode.Quarantine
    };

    private string CurrentRoot => _result?.Root ?? FolderBox.Text.Trim().Trim('"');

    /// <summary>Shows the restore button only when the folder holds files moved aside with a manifest.</summary>
    private async void RefreshRestore()
    {
        var root = CurrentRoot;
        var sessions = string.IsNullOrWhiteSpace(root) ? [] : await Task.Run(() => DuplicateRemoval.QuarantineSessions(root));
        RestoreButton.Visibility = sessions.Count > 0 && root == CurrentRoot ? Visibility.Visible : Visibility.Collapsed;
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
            {
                CheckpointPath = _checkpointPath, ResumeFromCheckpoint = ReuseCheck.IsChecked == true,
                MinimumSize = MinSizeBox.SelectedItem is ComboBoxItem { Tag: string size } ? long.Parse(size, CultureInfo.InvariantCulture) : 1,
                ExcludedNames = ExcludeBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                SkipHiddenAndSystem = HiddenCheck.IsChecked == true
            }, progress, active.Token);
            ApplyResult(result);
            ActivityLog.Write(L.T("Doublons", "Duplicates"), $"{result.Root} · {result.Groups.Count} group(s) · verified={result.SnapshotVerified} · {result.Errors.Count} error(s)");
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
            L.F($"{RowView.FormatSize(result.BytesRead)} lus · {result.FilesReused:N0} repris · {result.FilesHashed:N0} fichiers vérifiés par SHA-256", $"{RowView.FormatSize(result.BytesRead)} read · {result.FilesReused:N0} reused · {result.FilesHashed:N0} files verified with SHA-256") +
            (result.FilesFiltered > 0 ? L.F($" · {result.FilesFiltered:N0} exclus par les filtres", $" · {result.FilesFiltered:N0} left out by filters") : "");
        // Only local fixed drives have a Recycle Bin; elsewhere Windows would delete permanently.
        RecycleChoice.IsEnabled = DuplicateRemoval.RecycleBinAvailable(result.Root);
        if (!RecycleChoice.IsEnabled && ReferenceEquals(RemovalModeBox.SelectedItem, RecycleChoice)) RemovalModeBox.SelectedIndex = 1;
        ExportButton.IsEnabled = result.Groups.Count > 0;
        RefreshRestore();
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
        if (ExportButton != null) ExportButton.IsEnabled = false;
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
        SelectExtrasButton.IsEnabled = ClearSelectionButton.IsEnabled = KeepRuleBox.IsEnabled = _cancellation == null && CanRemove && _rows.Count > 0;
        DuplicateGrid.IsEnabled = _cancellation == null && CanRemove;
    }

    private void SelectExtras_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRemove || _cancellation != null) return;
        var rule = Enum.Parse<KeepRule>((KeepRuleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? nameof(KeepRule.FirstPath));
        string? folder = null;
        if (rule == KeepRule.InFolder)
        {
            var picker = new OpenFolderDialog { Title = L.T("Dossier dont les copies sont conservées", "Folder whose copies are kept"), Multiselect = false, InitialDirectory = _result!.Root };
            if (picker.ShowDialog(this) != true) return;
            folder = picker.FolderName;
        }
        var plan = DuplicateSelection.Extras(_result!, rule, folder);
        var remove = plan.Remove.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _updatingSelection = true;
        foreach (var row in _rows) row.IsSelected = remove.Contains(row.File.FullPath);
        _updatingSelection = false; UpdateSelection();
        if (plan.UndecidedGroups > 0)
            SelectionText.Text += L.F($" {plan.UndecidedGroups:N0} groupe(s) sans copie dans ce dossier restent sans sélection.", $" {plan.UndecidedGroups:N0} group(s) without a copy in that folder are left unselected.");
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
        ModeDetailText.Text = SelectedMode switch
        {
            DuplicateRemovalMode.Permanent => L.T("Suppression définitive, sans passage par la Corbeille. Un récapitulatif et une confirmation sont requis. Le contenu principal et les métadonnées sont recontrôlés avant l’action.", "Permanent deletion, without the Recycle Bin. A preview and confirmation are required. Main file contents and metadata are checked again before removal."),
            DuplicateRemovalMode.RecycleBin => L.T("Les copies vont dans la Corbeille de Windows, d’où vous pouvez les restaurer. Si un fichier est trop volumineux pour la Corbeille, Windows demande confirmation avant de le supprimer.", "Copies go to the Windows Recycle Bin, where you can restore them. If a file is too large for the Recycle Bin, Windows asks before deleting it."),
            _ => L.T("La mise à l’écart déplace les copies dans .CheckerSan-Quarantine, sous ce dossier, avec leur arborescence. « Restaurer » les remet à leur place. Aucun espace n’est libéré.", "Moving aside places copies in .CheckerSan-Quarantine inside this folder, keeping their structure. Restore puts them back. No space is freed.")
        };
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null || !CanRemove || !RemoveButton.IsEnabled) return;
        var scan = _result!;
        var paths = _rows.Where(row => row.IsSelected).Select(row => row.File.FullPath).ToArray();
        var mode = SelectedMode;
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
            var result = await new DuplicateRemoval { LogPath = _logPath }.DeleteAsync(scan, paths, mode, progress, active.Token);
            ActivityLog.Write(L.T("Retrait de doublons", "Duplicate removal"), $"{scan.Root} · {mode} · {result.DeletedPaths.Count} file(s) · complete={result.Complete} · details in {_logPath}");
            ClearResult();
            StatusText.Text = result.Complete ? L.T("Opération terminée — relancez l’analyse", "Operation finished — run the scan again") : L.T("Opération interrompue — relancez l’analyse", "Operation interrupted — run the scan again");
            StatsText.Text = mode switch
            {
                DuplicateRemovalMode.Permanent => L.F($"{result.DeletedPaths.Count:N0} fichier(s) supprimé(s) · {RowView.FormatSize(result.BytesRemoved)} de contenu retiré", $"{result.DeletedPaths.Count:N0} file(s) deleted · {RowView.FormatSize(result.BytesRemoved)} of contents removed"),
                DuplicateRemovalMode.RecycleBin => L.F($"{result.DeletedPaths.Count:N0} fichier(s) mis à la Corbeille · {RowView.FormatSize(result.BytesRecycled)} libérés une fois la Corbeille vidée", $"{result.DeletedPaths.Count:N0} file(s) moved to the Recycle Bin · {RowView.FormatSize(result.BytesRecycled)} freed once it is emptied"),
                _ => L.F($"{result.DeletedPaths.Count:N0} fichier(s) mis à l’écart · {RowView.FormatSize(result.BytesMoved)} déplacés · aucun espace libéré", $"{result.DeletedPaths.Count:N0} file(s) moved aside · {RowView.FormatSize(result.BytesMoved)} moved · no space freed")
            } + L.T("\nChaque action est inscrite dans le journal des actions.", "\nEvery action is recorded in the action journal.");
            ShowIssues(result.Errors.Concat(result.Warnings).Concat(result.DeletedPaths.Select(path => L.T("Retiré : ", "Removed: ") + path)));
            if (result.QuarantinePath is {} quarantine)
            {
                _quarantinePath = quarantine;
                OpenQuarantineButton.Visibility = Visibility.Visible;
                StatsText.Text += L.T("\n« Restaurer des fichiers mis à l’écart… » les remet à leur chemin d’origine.", "\nRestore files moved aside… puts them back at their original paths.");
                CurrentPathText.Text = quarantine;
                CurrentPathText.ToolTip = quarantine;
            }
            else CurrentPathText.Text = "";
        }
        catch (Exception ex) { ClearResult(); StatusText.Text = L.T("Opération interrompue — relancez l’analyse", "Operation interrupted — run the scan again"); ShowIssues([ex.Message]); }
        finally { FinishOperation(); RefreshRestore(); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        var root = CurrentRoot;
        var sessions = await Task.Run(() => DuplicateRemoval.QuarantineSessions(root));
        if (sessions.Count == 0) { RefreshRestore(); return; }
        var picker = new QuarantineRestoreDialog(sessions) { Owner = this };
        if (picker.ShowDialog() != true || picker.Session is not { } session) return;
        _cancellation = new CancellationTokenSource();
        var active = _cancellation;
        SetBusy(true);
        StatusText.Text = L.T("Restauration des fichiers mis à l’écart…", "Restoring files moved aside…");
        // Restored files change the folder, so earlier results no longer describe it.
        FilesChanged = true;
        try
        {
            var result = await new DuplicateRemoval { LogPath = _logPath }.RestoreAsync(session.Path, active.Token);
            ClearResult();
            StatusText.Text = result.Complete ? L.T("Restauration terminée — relancez l’analyse", "Restore finished — run the scan again") : L.T("Restauration incomplète — relancez l’analyse", "Restore incomplete — run the scan again");
            StatsText.Text = L.F($"{result.Restored:N0} fichier(s) remis à leur place. Aucun fichier existant n’a été remplacé.", $"{result.Restored:N0} file(s) put back. No existing file was replaced.");
            ShowIssues(result.Errors);
        }
        catch (Exception ex) { ClearResult(); StatusText.Text = L.T("Restauration interrompue", "Restore interrupted"); ShowIssues([ex.Message]); }
        finally { FinishOperation(); RefreshRestore(); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_result is not { } result) return;
        var dialog = new SaveFileDialog
        {
            Title = L.T("Exporter la liste des doublons", "Export the duplicate list"), FileName = $"Checker San duplicates-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv",
            Filter = L.T("Tableau CSV pour Excel (*.csv)|*.csv|Liste JSON complète (*.json)|*.json", "CSV for Excel (*.csv)|*.csv|Complete JSON list (*.json)|*.json"),
            AddExtension = true, OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            DuplicateReportWriter.Write(dialog.FileName, result, _rows.Where(row => row.IsSelected).Select(row => row.File.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase));
            CurrentPathText.Text = L.T("Liste exportée : ", "List exported: ") + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Export impossible", "Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_logPath)) OpenExplorer(_logPath, true);
        else MessageBox.Show(this, L.T("Aucune action n’a encore été enregistrée. Le journal se remplit à chaque retrait ou restauration.", "No action has been recorded yet. The journal grows with every removal or restoration."), "Checker San");
    }

    private void ShowIssues(IEnumerable<string> issues)
    {
        var lines = issues.ToArray();
        IssuesExpander.Visibility = lines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        IssuesText.Text = string.Join(Environment.NewLine, lines);
    }

    private void SetBusy(bool busy)
    {
        FolderBox.IsEnabled = BrowseButton.IsEnabled = ReuseCheck.IsEnabled = ScanButton.IsEnabled = RemovalModeBox.IsEnabled = FiltersExpander.IsEnabled = !busy;
        RestoreButton.IsEnabled = LogButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _result is { Groups.Count: > 0 };
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
