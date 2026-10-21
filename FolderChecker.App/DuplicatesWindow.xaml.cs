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
            ResumeText.Text = "Une recherche est enregistrée. Analyser / reprendre réutilise les fichiers encore valides.";
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Dossier à analyser", Multiselect = false };
        if (!string.IsNullOrWhiteSpace(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text.Trim().Trim('"');
        if (picker.ShowDialog(this) == true) FolderBox.Text = picker.FolderName;
    }

    private void Folder_Changed(object sender, TextChangedEventArgs e)
    {
        if (_result == null || _cancellation != null) return;
        ClearResult();
        StatusText.Text = "Le dossier a changé — relancez l’analyse";
        StatsText.Text = "Les résultats précédents ne s’appliquent plus à cette sélection.";
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
        StatusText.Text = "Préparation de l’analyse…";
        var active = _cancellation;
        var progress = new Progress<DuplicateProgress>(p =>
        {
            if (_cancellation != active || (Environment.TickCount64 - _lastProgress < 100 && p.RetryAttempt == 0)) return;
            _lastProgress = Environment.TickCount64;
            StatusText.Text = p.Phase;
            StatsText.Text = $"{p.FilesScanned:N0} fichiers · {RowView.FormatSize(p.BytesRead)} lus · {p.FilesReused:N0} repris · {timer.Elapsed:hh\\:mm\\:ss}";
            CurrentPathText.Text = p.CurrentPath;
        });
        try
        {
            var result = await new DuplicateEngine().ScanAsync(FolderBox.Text, new DuplicateScanOptions
            { CheckpointPath = _checkpointPath, ResumeFromCheckpoint = ReuseCheck.IsChecked == true }, progress, active.Token);
            ApplyResult(result);
        }
        catch (Exception ex) { StatusText.Text = "Analyse impossible"; ShowIssues([ex.Message]); }
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
        StatusText.Text = !complete ? result.Cancelled ? "Analyse arrêtée — reprenez pour terminer" : "Analyse incomplète — aucun retrait autorisé"
            : result.Groups.Count == 0 ? "Aucun doublon trouvé" : $"{result.Groups.Count:N0} groupe(s) de fichiers identiques";
        var extras = result.Groups.Sum(group => group.Files.Count - 1);
        var bytes = result.Groups.Sum(group => group.Size * (group.Files.Count - 1));
        StatsText.Text = $"{result.FilesScanned:N0} fichiers · {extras:N0} copies supplémentaires · {RowView.FormatSize(bytes)} de contenu en double\n" +
            $"{RowView.FormatSize(result.BytesRead)} lus · {result.FilesReused:N0} repris · {result.FilesHashed:N0} fichiers vérifiés par SHA-256";
        EmptyText.Text = complete ? "Aucun groupe de fichiers identiques dans ce dossier." : "La recherche doit être complète avant de retirer des fichiers.";
        CurrentPathText.Text = "";
        ResumeText.Text = result.CheckpointSaved ? "Progression enregistrée. Analyser / reprendre vérifie les changements avant de réutiliser le cache." : "3 nouvelles tentatives en cas de coupure réseau.";
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
        SelectionText.Text = invalid ? "Gardez au moins un exemplaire dans chaque groupe : désélectionnez un fichier."
            : selected.Length == 0 ? "Rien n’est sélectionné. Gardez au moins un exemplaire par groupe."
            : $"{selected.Length:N0} fichier(s) à retirer · {RowView.FormatSize(selected.Sum(row => row.File.Size))}. Les fichiers non cochés restent en place.";
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
            ? "Suppression définitive, sans passage par la Corbeille. Un récapitulatif et une confirmation sont requis. Le contenu principal et les métadonnées sont recontrôlés avant l’action."
            : "La mise à l’écart déplace les copies dans .CheckerSan-Quarantine, sous ce dossier. Leur arborescence est conservée pour une restauration manuelle. Aucun espace n’est libéré.";
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
        StatusText.Text = "Vérification des fichiers avant retrait…";
        var progress = new Progress<DuplicateRemovalProgress>(p =>
        {
            if (_cancellation != active || Environment.TickCount64 - _lastProgress < 100) return;
            _lastProgress = Environment.TickCount64;
            StatusText.Text = p.Phase;
            CurrentPathText.Text = p.CurrentPath;
            StatsText.Text = $"{p.CompletedFiles:N0} / {p.TotalFiles:N0} fichiers retirés · {RowView.FormatSize(p.BytesVerified)} revérifiés";
        });
        // Invalidate all previous comparison verdicts even if an interrupted remote action has an uncertain outcome.
        FilesChanged = true;
        try
        {
            var result = await new DuplicateRemoval().DeleteAsync(scan, paths, mode, progress, active.Token);
            ClearResult();
            StatusText.Text = result.Complete ? "Opération terminée — relancez l’analyse" : "Opération interrompue — relancez l’analyse";
            StatsText.Text = mode == DuplicateRemovalMode.Permanent
                ? $"{result.DeletedPaths.Count:N0} fichier(s) supprimé(s) · {RowView.FormatSize(result.BytesRemoved)} de contenu retiré"
                : $"{result.DeletedPaths.Count:N0} fichier(s) mis à l’écart · {RowView.FormatSize(result.BytesMoved)} déplacés · aucun espace libéré";
            ShowIssues(result.Errors.Concat(result.DeletedPaths.Select(path => "Retiré : " + path)));
            if (result.QuarantinePath is {} quarantine)
            {
                _quarantinePath = quarantine;
                OpenQuarantineButton.Visibility = Visibility.Visible;
                StatsText.Text += "\nRestauration manuelle : replacez les fichiers à leur chemin d’origine depuis la mise à l’écart.";
                CurrentPathText.Text = quarantine;
                CurrentPathText.ToolTip = quarantine;
            }
            else CurrentPathText.Text = "";
        }
        catch (Exception ex) { ClearResult(); StatusText.Text = "Opération interrompue — relancez l’analyse"; ShowIssues([ex.Message]); }
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
        StatusText.Text = "Arrêt demandé — en attente de Windows…";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellation == null) return;
        e.Cancel = true; _closeAfterCancel = true; _cancellation.Cancel();
        StatusText.Text = "Arrêt et sauvegarde avant fermeture…";
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
