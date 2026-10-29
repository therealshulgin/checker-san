using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using FolderChecker.Core;
using Microsoft.Win32;

namespace FolderChecker.App;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancellation;
    private ComparisonResult? _result;
    private List<RowView> _rows = [];
    private ICollectionView? _view;
    private readonly Stopwatch _clock = new();
    private bool _closeAfterCancel;
    private long _lastProgressTick;
    private readonly string _checkpointPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Sessions", "resume.json");
    private CheckpointInfo? _checkpointInfo;
    private bool _languageReady;
    private bool _busy;
    private Action? _refreshStatus;

    private void SetStatus(Action update)
    {
        _refreshStatus = update;
        update();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady || _busy || LanguageBox.SelectedValue is not string language || language == L.Language) return;
        AppLanguage.Apply(language);
        if (AppLanguage.LastSaveError is { } error)
            FooterText.Text = L.T("Langue modifiée, mais préférence non enregistrée : ", "Language changed, but preference could not be saved: ") + error;
    }

    private void RefreshLanguage(object? sender, EventArgs e)
    {
        LanguageBox.SelectedValue = L.Language;
        if (_busy) return;
        var selected = Selected?.Row;
        if (_result is { } result)
        {
            ShowResult(result);
            ResultsGrid.SelectedItem = _rows.FirstOrDefault(row => ReferenceEquals(row.Row, selected));
        }
        else
        {
            _refreshStatus?.Invoke();
            ClearContainmentVerdicts();
        }
        Result_Selected(ResultsGrid, null!);
        CompareButton.Content = L.T("Comparer", "Compare");
        RefreshCheckpoint();
        RefreshCopy();
    }

    public MainWindow()
    {
        AppLanguage.InitializeResources();
        InitializeComponent();
        LanguageBox.SelectedValue = L.Language;
        _languageReady = true;
        L.Changed += RefreshLanguage;
        Closed += (_, _) => L.Changed -= RefreshLanguage;
        SourceInitialized += (_, _) =>
        {
            int enabled = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        };
        // Keep the application inside the work area even on a small or scaled display.
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        Loaded += async (_, _) =>
        {
            RefreshCheckpoint();
            RefreshCopy();
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--source") SourceBox.Text = args[++i];
                else if (args[i] == "--target") TargetBox.Text = args[++i];
            }
            // Opened from a scheduled result: the pair's folders and option, read from its list.
            if (Array.IndexOf(args, "--pair") is var pairIndex and > 0 && pairIndex + 1 < args.Length)
            {
                var store = Array.IndexOf(args, "--pairs") is var storeIndex and > 0 && storeIndex + 1 < args.Length ? args[storeIndex + 1] : null;
                try { if (PairStore.Find(args[pairIndex + 1], store) is { } pair) LoadPair(pair); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { FooterText.Text = exception.Message; }
            }
            if (args.Contains("--compare") && SourceBox.Text.Length > 0 && TargetBox.Text.Length > 0) await CompareAsync();
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void BrowseSource_Click(object sender, RoutedEventArgs e) => Browse(SourceBox, L.T("Dossier 1", "Folder 1"));
    private void BrowseTarget_Click(object sender, RoutedEventArgs e) => Browse(TargetBox, L.T("Dossier 2", "Folder 2"));
    private void Duplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        var dialog = new DuplicatesWindow(SourceBox.Text.Trim().Trim('"')) { Owner = this };
        dialog.ShowDialog();
        // Removing duplicates can change either compared tree, so never retain an earlier verdict.
        if (dialog.FilesChanged) Paths_Changed(SourceBox, null!);
    }
    private void Snapshots_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        new SnapshotWindow(SourceBox.Text.Trim().Trim('"')) { Owner = this }.ShowDialog();
    }
    private void Pairs_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        var window = new PairsWindow(SourceBox.Text.Trim().Trim('"'), TargetBox.Text.Trim().Trim('"'), MovedCheck.IsChecked == true,
            PairStore.DefaultPath, Environment.ProcessPath ?? "") { Owner = this };
        if (window.ShowDialog() == true && window.PairToOpen is { } pair) LoadPair(pair);
    }
    /// <summary>Puts a saved pair in the folder boxes; the previous verdict is invalidated as for any edit.</summary>
    public void LoadPair(SavedPair pair)
    {
        SourceBox.Text = pair.Folder1;
        TargetBox.Text = pair.Folder2;
        MovedCheck.IsChecked = pair.FindMovedFiles;
    }
    private void Browse(TextBox box, string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        // Avoid a synchronous network probe on the UI thread. The native picker handles availability.
        if (!string.IsNullOrWhiteSpace(box.Text)) dialog.InitialDirectory = box.Text.Trim().Trim('"');
        if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName;
    }
    private void Swap_Click(object sender, RoutedEventArgs e) => (SourceBox.Text, TargetBox.Text) = (TargetBox.Text, SourceBox.Text);
    private void Paths_Changed(object sender, TextChangedEventArgs e)
    {
        if (_cancellation == null) _narrowedFrom = null;
        if (_result == null || _cancellation != null) return;
        _result = null; _rows = []; _view = null;
        ResultsGrid.ItemsSource = null; ExportButton.IsEnabled = false;
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        SetStatus(() =>
        {
            VerdictTitle.Text = L.T("Les dossiers ont changé — relancez la comparaison", "Folders changed — run the comparison again");
            VerdictDetail.Text = L.T("Les résultats précédents ne s’appliquent pas aux chemins sélectionnés.", "Previous results no longer apply to the selected paths.");
            ClearContainmentVerdicts();
            VerdictCard.Background = RowView.Brush("#242424"); VerdictCard.ToolTip = null;
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitle.Text = L.T("Une nouvelle vérification est nécessaire", "A new check is required");
            EmptyDetail.Text = L.T("Lancez la comparaison avec les dossiers sélectionnés.", "Run a comparison with the selected folders.");
            FooterText.Text = L.T("Prêt • aucun verdict pour cette sélection", "Ready • no verdict for this selection");
        });
        RefreshCopy();
    }
    private void Path_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Path_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Directory.Exists(paths[0])) ((TextBox)sender).Text = paths[0];
    }
    private async void Compare_Click(object sender, RoutedEventArgs e) => await CompareAsync();
    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        RefreshCheckpoint();
        if (_checkpointInfo is not {} checkpoint) return;
        SourceBox.Text = checkpoint.SourceRoot;
        TargetBox.Text = checkpoint.TargetRoot;
        MovedCheck.IsChecked = checkpoint.FindMovedFiles;
        await CompareAsync(resume: true);
    }
    private int _checkpointReads;
    // A resume file of a large tree weighs hundreds of megabytes: it is read off the interface thread, and only
    // the latest request updates the buttons.
    private async void RefreshCheckpoint()
    {
        var request = ++_checkpointReads;
        var info = await Task.Run(() => ComparisonEngine.ReadCheckpointInfo(_checkpointPath));
        if (request != _checkpointReads) return;
        _checkpointInfo = info;
        ResumeButton.IsEnabled = _cancellation == null && _checkpointInfo != null;
        ResumeHint.Text = _checkpointInfo == null
            ? L.T("3 tentatives automatiques en cas de coupure réseau", "3 automatic retries after a network interruption")
            : L.F($"Reprise disponible · {_checkpointInfo.CompletedFileCount:N0} empreintes · {_checkpointInfo.SavedAt.ToLocalTime():dd/MM HH:mm}", $"Resume available · {_checkpointInfo.CompletedFileCount:N0} hashes · {_checkpointInfo.SavedAt.ToLocalTime():g}");
        ResumeButton.ToolTip = _checkpointInfo == null ? L.T("Aucun point de reprise enregistré.", "No saved checkpoint.") : L.F($"Reprendre ce scan :\n1 : {_checkpointInfo.SourceRoot}\n2 : {_checkpointInfo.TargetRoot}\nLes fichiers terminés seront recontrôlés avant réutilisation.", $"Resume this scan:\n1: {_checkpointInfo.SourceRoot}\n2: {_checkpointInfo.TargetRoot}\nCompleted files will be checked before reuse.");
    }
    private async Task CompareAsync(bool resume = false)
    {
        if (_cancellation != null) return;
        if (string.IsNullOrWhiteSpace(SourceBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text))
        { MessageBox.Show(this, L.T("Sélectionnez les deux dossiers à comparer.", "Select both folders to compare."), L.T("Deux dossiers nécessaires", "Two folders required"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        _result = null;
        _refreshStatus = null;
        _rows = [];
        ResultsGrid.ItemsSource = null;
        _view = null;
        ExportButton.IsEnabled = false;
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitle.Text = L.T("Lecture des dossiers en cours…", "Reading folders…");
        EmptyDetail.Text = L.T("Les résultats s’afficheront à la fin de la vérification.", "Results will appear when verification is complete.");
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        VerdictTitle.Text = resume ? L.T("Reprise de la vérification", "Resuming verification") : L.T("Vérification en cours", "Verification in progress");
        VerdictDetail.Text = L.T("Inventaire, comparaison des tailles, puis lecture complète des contenus à vérifier.", "Listing entries, comparing sizes, then reading the file contents that need verification.");
        VerdictCard.Background = RowView.Brush("#242424");
        ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ProgressDetail.Text = L.T("Préparation…", "Preparing…");
        _clock.Restart();
        _lastProgressTick = 0;
        var progress = new Progress<ComparisonProgress>(p =>
        {
            if (_cancellation == null) return;
            // Throttle layout updates; the engine also throttles its read loop notifications.
            if (_clock.ElapsedMilliseconds - _lastProgressTick < 100 && p.RetryAttempt == 0) return;
            _lastProgressTick = _clock.ElapsedMilliseconds;
            ScanProgress.IsIndeterminate = p.TotalHashBytes <= 0 || p.Phase.Contains("inventaire", StringComparison.OrdinalIgnoreCase) || p.Phase.Contains("inventory", StringComparison.OrdinalIgnoreCase) || p.Phase.Contains("contrôle final", StringComparison.OrdinalIgnoreCase) || p.Phase.Contains("final", StringComparison.OrdinalIgnoreCase);
            ScanProgress.Value = p.TotalHashBytes > 0 ? Math.Min(100, 100.0 * p.BytesHashed / p.TotalHashBytes) : 0;
            VerdictDetail.Text = L.F($"{p.Phase} · {p.FilesDiscovered:N0} fichiers recensés · {p.FilesHashed:N0} lus · {p.FilesReused:N0} repris", $"{p.Phase} · {p.FilesDiscovered:N0} files found · {p.FilesHashed:N0} read · {p.FilesReused:N0} reused");
            if (p.RetryAttempt > 0) VerdictTitle.Text = L.F($"Reconnexion en cours · tentative {p.RetryAttempt}/3", $"Reconnecting · attempt {p.RetryAttempt}/3");
            else if (!_cancellation.IsCancellationRequested) VerdictTitle.Text = resume ? L.T("Reprise de la vérification", "Resuming verification") : L.T("Vérification en cours", "Verification in progress");
            var bytesRead = p.BytesHashed + p.SampleBytesRead;
            var rate = _clock.Elapsed.TotalSeconds >= 1 ? $" · {RowView.FormatSize((long)(bytesRead / _clock.Elapsed.TotalSeconds))}/s" : "";
            ProgressDetail.Text = L.F($"{RowView.FormatSize(bytesRead)} lus{rate}  ·  {_clock.Elapsed:hh\\:mm\\:ss}  ·  {p.CurrentPath}", $"{RowView.FormatSize(bytesRead)} read{rate}  ·  {_clock.Elapsed:hh\\:mm\\:ss}  ·  {p.CurrentPath}");
            FooterText.Text = L.T("Comparaison en cours • vous pouvez l’arrêter sans modifier vos fichiers", "Comparison in progress • stopping does not change your files");
        });
        try
        {
            _result = await new ComparisonEngine().CompareAsync(SourceBox.Text.Trim().Trim('"'), TargetBox.Text.Trim().Trim('"'), new ComparisonOptions(MovedCheck.IsChecked == true)
            {
                CheckpointPath = _checkpointPath,
                ResumeFromCheckpoint = resume || ReuseHashesCheck.IsChecked == true,
                UseSamplePrefilter = SampleCheck.IsChecked == true,
                MaxRetries = 3,
                InitialRetryDelayMs = 1000
            }, progress, _cancellation.Token);
            ShowResult(_result);
            ActivityLog.Write(L.T("Comparaison", "Comparison"), $"{_result.SourceRoot} ↔ {_result.TargetRoot} · {VerdictTitle.Text} · {_result.Errors.Count} error(s)");
        }
        catch (Exception ex)
        {
            SetStatus(() =>
            {
                VerdictTitle.Text = L.T("La comparaison n’a pas pu aboutir", "Comparison could not be completed");
                VerdictDetail.Text = ex.Message;
                VerdictCard.Background = RowView.Brush("#242424");
                EmptyTitle.Text = L.T("Vérifiez les dossiers sélectionnés", "Check the selected folders");
                EmptyDetail.Text = L.T("Aucun verdict d’identité n’a été établi.", "No identity verdict has been established.");
                FooterText.Text = L.T("Comparaison non aboutie • aucun fichier modifié", "Comparison incomplete • no files changed");
            });
        }
        finally
        {
            _clock.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
            RefreshCheckpoint();
            RefreshCopy();
            ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Collapsed;
            if (_closeAfterCancel) Close();
        }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (!busy) LanguageBox.SelectedValue = L.Language;
        LanguageBox.IsEnabled = !busy;
        if (busy) ClearContainmentVerdicts();
        FolderInputs.IsEnabled = MovedCheck.IsEnabled = CompareButton.IsEnabled = !busy;
        DuplicatesButton.IsEnabled = ReuseHashesCheck.IsEnabled = SampleCheck.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ResumeButton.IsEnabled = !busy && _checkpointInfo != null;
        TransferButton.IsEnabled = GatherInto1Button.IsEnabled = GatherInto2Button.IsEnabled = false;
        CompareButton.Content = busy ? L.T("Comparaison…", "Comparing…") : L.T("Comparer", "Compare");
    }
    private void ClearContainmentVerdicts()
    {
        ContainsSourceVerdict.Text = L.T("Le dossier 2 contient tout le dossier 1 : Non vérifié", "Folder 2 contains all of folder 1: Not verified");
        ContainsTargetVerdict.Text = L.T("Le dossier 1 contient tout le dossier 2 : Non vérifié", "Folder 1 contains all of folder 2: Not verified");
        ContainsSourceVerdict.Foreground = ContainsTargetVerdict.Foreground = RowView.Brush("#AEAEAE");
        GatherInto1Button.Visibility = GatherInto2Button.Visibility = Visibility.Collapsed;
    }
    private void ShowContainmentVerdicts(ComparisonResult result)
    {
        ClearContainmentVerdicts();
        // The same rule as the reports and the command line, so the screen and an export never disagree.
        var sourceContained = ComparisonVerdicts.Folder2HoldsFolder1(result);
        var targetContained = ComparisonVerdicts.Folder1HoldsFolder2(result);
        if (sourceContained is { } source)
        {
            ContainsSourceVerdict.Text = L.F($"Le dossier 2 contient tout le dossier 1 : {(source ? "Oui" : "Non")}", $"Folder 2 contains all of folder 1: {(source ? "Yes" : "No")}");
            ContainsSourceVerdict.Foreground = RowView.Brush(source ? "#A5D9BB" : "#E8C395");
        }
        if (targetContained is { } target)
        {
            ContainsTargetVerdict.Text = L.F($"Le dossier 1 contient tout le dossier 2 : {(target ? "Oui" : "Non")}", $"Folder 1 contains all of folder 2: {(target ? "Yes" : "No")}");
            ContainsTargetVerdict.Foreground = RowView.Brush(target ? "#A5D9BB" : "#E8C395");
        }
        ShowGather(GatherInto1Button, result, intoTarget: false);
        ShowGather(GatherInto2Button, result, intoTarget: true);
    }
    /// <summary>Offers to gather into a folder the files it has no copy of, next to the verdict that it lacks them.</summary>
    private void ShowGather(Button button, ComparisonResult result, bool intoTarget)
    {
        var files = CopyEngine.GatherFileCount(result, intoTarget);
        button.Visibility = files > 0 ? Visibility.Visible : Visibility.Collapsed;
        button.IsEnabled = _cancellation == null;
        var folder = intoTarget ? 2 : 1;
        // An unfinished copy of these folders is offered first by the dialog: the button says so.
        button.Content = _savedCopy is { } saved && saved.Items.Any(item => !item.Completed) && CopyDialog.SamePair(saved, result)
            ? L.F($"Reprendre la copie inachevée, puis regrouper dans le {folder}…", $"Resume the unfinished copy, then gather into folder {folder}…")
            : L.F($"Tout regrouper dans le {folder} · {files:N0} fichier(s) à copier…", $"Gather everything into folder {folder} · {files:N0} file(s) to copy…");
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel(); CancelButton.IsEnabled = false;
        VerdictTitle.Text = L.T("Arrêt demandé — attente de la réponse du disque ou du réseau…", "Stop requested — waiting for the disk or network…");
    }
    private void ShowResult(ComparisonResult result)
    {
        _rows = result.Rows.Select(r => new RowView(r)).OrderBy(r => Priority(r.Row.Status)).ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ResultsGrid.ItemsSource = _view;
        int Count(ComparisonStatus status) => result.Rows.Count(r => r.Status == status);
        IdenticalCount.Text = Count(ComparisonStatus.Identical).ToString("N0", L.Culture);
        MissingCount.Text = Count(ComparisonStatus.OnlySource).ToString("N0", L.Culture);
        DifferentCount.Text = (Count(ComparisonStatus.Different) + Count(ComparisonStatus.Conflict)).ToString("N0", L.Culture);
        MovedCount.Text = Count(ComparisonStatus.Moved).ToString("N0", L.Culture);
        ExtraCount.Text = Count(ComparisonStatus.OnlyTarget).ToString("N0", L.Culture);
        string title, detail, color;
        var onlineOnly = result.Rows.Count(r => r.Status == ComparisonStatus.Unverified && r.OnlineOnly);
        var onlineNote = L.F($"{onlineOnly:N0} fichier(s) disponible(s) uniquement en ligne n’ont pas été téléchargés : leur contenu n’est pas vérifié. Rendez-les disponibles sur cet appareil pour les vérifier.", $"{onlineOnly:N0} online-only file(s) were not downloaded, so their contents are not verified. Make them available on this device to verify them.");
        if (result.Cancelled)
            (title, detail, color) = (L.T("Comparaison interrompue — résultat incomplet", "Comparison stopped — incomplete result"), result.CheckpointSaved ? L.T("Les fichiers terminés sont enregistrés. Cliquez sur « Reprendre » pour continuer sans les relire inutilement.", "Completed files have been saved. Click Resume to continue without unnecessary rereading.") : L.T("Les lignes ci-dessous sont partielles. Aucun point de reprise n’a pu être enregistré.", "The entries below are incomplete. No checkpoint could be saved."), "#242424");
        else if (result.Errors.Count > 0 || !result.SnapshotVerified || Count(ComparisonStatus.Unverified) > 0)
            (title, detail, color) = (L.T("Vérification incomplète — des éléments restent à contrôler", "Verification incomplete — some items still need checking"),
                result.Errors.Count == 0 && result.SnapshotVerified && Count(ComparisonStatus.Unverified) == onlineOnly ? onlineNote
                    : L.F($"{result.Errors.Count:N0} incident(s). Consultez le filtre « Non vérifiés, liens, erreurs » et le rapport avant de décider de supprimer une copie.", $"{result.Errors.Count:N0} issue(s). Check the Not verified, links, errors filter and the report before deciding to remove a copy.")
                        + (onlineOnly > 0 ? " " + onlineNote : ""), "#242424");
        else if (result.TreesIdentical)
            (title, detail, color) = (L.T("Les deux dossiers sont identiques", "Both folders are identical"), L.T("Mêmes chemins, mêmes dossiers et mêmes contenus de fichiers, confirmés par SHA-256. Résultat valable pour l’état contrôlé à la fin du scan.", "Same paths, folders and file contents, confirmed by SHA-256. This result describes the state checked at the end of the scan."), "#242424");
        else if (result.SourceFullyContained)
            (title, detail, color) = (L.T("Le dossier 2 contient tout le dossier 1", "Folder 2 contains all of folder 1"), L.T("Tous les éléments du dossier 1 sont présents aux mêmes chemins, avec des contenus identiques. Le dossier 2 contient aussi des éléments supplémentaires.", "Every item in folder 1 is present at the same path with identical content. Folder 2 also contains additional items."), "#242424");
        else if (result.TargetFullyContained)
            (title, detail, color) = (L.T("Le dossier 1 contient tout le dossier 2", "Folder 1 contains all of folder 2"), L.T("Tous les éléments du dossier 2 sont présents aux mêmes chemins, avec des contenus identiques. Le dossier 1 contient aussi des éléments supplémentaires.", "Every item in folder 2 is present at the same path with identical content. Folder 1 also contains additional items."), "#242424");
        else
            (title, detail, color) = (L.T("Aucun dossier ne contient entièrement l’autre", "Neither folder fully contains the other"), L.T("Des éléments manquent au même chemin ou ont un contenu différent. Consultez « Tous les écarts » : les fichiers retrouvés ailleurs restent distincts des correspondances au même chemin.", "Some paths are missing or have different contents. Check All differences: files found elsewhere are distinct from matches at the same path."), "#242424");
        if (Count(ComparisonStatus.Skipped) is var links and > 0) detail += L.F($" {links:N0} élément(s) concerné(s) par des liens ou jonctions non suivis, exclus du verdict.", $" {links:N0} item(s) involve links or junctions that were not followed and are excluded from the verdict.");
        // After a gathering the folders differ by paths, not by contents: only the comparison's own proof says so, and
        // only with nothing small left to copy for its name.
        if (result.ContentCovered && !result.SourceFullyContained && CopyEngine.GatherFileCount(result, true) == 0)
            detail += L.T(" Le dossier 2 a un exemplaire de chaque fichier du dossier 1, parfois à un autre chemin (SHA-256).", " Folder 2 has a copy of every file of folder 1, sometimes at another path (SHA-256).");
        if (result.TargetContentCovered && !result.TargetFullyContained && CopyEngine.GatherFileCount(result, false) == 0)
            detail += L.T(" Le dossier 1 a un exemplaire de chaque fichier du dossier 2, parfois à un autre chemin (SHA-256).", " Folder 1 has a copy of every file of folder 2, sometimes at another path (SHA-256).");
        if (FolderAlignment.Suggest(result) is { } aligned) detail += " " + CopyDialog.AlignmentVerdictText(aligned);
        if (_narrowedFrom is { } narrowed && SameFolder((narrowed.InFolder2 ? TargetBox : SourceBox).Text, narrowed.Folder))
            detail += L.F($" Comparaison limitée à « {narrowed.Folder} » : les {narrowed.Outside:N0} fichier(s) du dossier {(narrowed.InFolder2 ? 2 : 1)} d’origine situés hors de ce sous-dossier ne sont pas couverts par ce verdict.",
                $" Comparison limited to ‘{narrowed.Folder}’: the {narrowed.Outside:N0} file(s) of the original folder {(narrowed.InFolder2 ? 2 : 1)} outside that subfolder are not covered by this verdict.");
        if (result.FilesReused > 0) detail += L.F($" Reprise : {result.FilesReused:N0} empreintes réutilisées après contrôle de l’identité et des métadonnées des fichiers.", $" Resume: {result.FilesReused:N0} hashes reused after checking file identities and metadata.");
        if (result.Warnings.Count > 0) detail += " " + string.Join(" ", result.Warnings);
        VerdictTitle.Text = title; VerdictDetail.Text = detail; VerdictCard.Background = RowView.Brush(color);
        ShowContainmentVerdicts(result);
        ExportButton.IsEnabled = true;
        RefreshView();
        if (result.Errors.Count > 0) VerdictCard.ToolTip = string.Join("\n", result.Errors.Take(10));
        else VerdictCard.ToolTip = null;
    }
    private static int Priority(ComparisonStatus status) => status switch
    {
        ComparisonStatus.Unverified => 0, ComparisonStatus.Conflict => 1, ComparisonStatus.Different => 2,
        ComparisonStatus.OnlySource => 3, ComparisonStatus.Moved => 4, ComparisonStatus.OnlyTarget => 5,
        ComparisonStatus.Skipped => 6, ComparisonStatus.Identical => 7, _ => 8
    };
    private bool FilterRow(object item)
    {
        if (item is not RowView view) return false;
        var row = view.Row;
        bool match = FilterBox.SelectedIndex switch
        {
            1 => row.Status is ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget or ComparisonStatus.Different or ComparisonStatus.Conflict or ComparisonStatus.Moved or ComparisonStatus.Unverified or ComparisonStatus.Skipped,
            2 => row.Status is ComparisonStatus.Identical or ComparisonStatus.CommonDirectory,
            3 => row.Status == ComparisonStatus.OnlySource,
            4 => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict,
            5 => row.Status == ComparisonStatus.Moved,
            6 => row.Status == ComparisonStatus.OnlyTarget,
            7 => row.Status is ComparisonStatus.Unverified or ComparisonStatus.Skipped,
            8 => row.Kind == EntryKind.Directory,
            _ => true
        };
        var query = SearchBox.Text.Trim();
        return match && (query.Length == 0 || (row.RelativePath + " " + row.TargetPath + " " + row.Detail).Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshView();
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshView();
    private void RefreshView()
    {
        if (_view == null) return;
        _view.Refresh();
        int count = _view.Cast<object>().Count();
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _rows.Count == 0 ? L.T("Aucun élément dans ces dossiers", "No items in these folders") : L.T("Aucun élément pour ce filtre", "No items match this filter");
        EmptyDetail.Text = _rows.Count == 0 ? L.T("Le verdict ci-dessus précise si le contrôle a abouti.", "The verdict above shows whether verification completed.") : L.T("Changez le filtre ou le texte de recherche.", "Change the filter or search text.");
        if (_result != null)
        {
            var duration = _result.CompletedAt - _result.StartedAt;
            FooterText.Text = L.F($"{count:N0} / {_rows.Count:N0} éléments · {_result.SourceFileCount:N0} fichiers dans le 1, {_result.TargetFileCount:N0} dans le 2 · {RowView.FormatSize(_result.BytesRead + _result.SampleBytesRead)} lus · {_result.FilesReused:N0} repris · {duration:hh\\:mm\\:ss} · {_result.Errors.Count:N0} incident(s)", $"{count:N0} / {_rows.Count:N0} items · {_result.SourceFileCount:N0} files in 1, {_result.TargetFileCount:N0} in 2 · {RowView.FormatSize(_result.BytesRead + _result.SampleBytesRead)} read · {_result.FilesReused:N0} reused · {duration:hh\\:mm\\:ss} · {_result.Errors.Count:N0} issue(s)");
        }
    }
    private RowView? Selected => ResultsGrid.SelectedItem as RowView;
    private void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionTitle == null) return;
        var view = Selected;
        SelectionTitle.Text = view == null ? L.T("Sélectionnez une ligne pour afficher ses détails.", "Select a row to see its details.") : view.RelativePath;
        SelectionDetail.Text = view?.Row.Detail ?? "";
        SelectedPaths.Text = view == null ? "" : $"{Side(view.Row, 1)}     |     {Side(view.Row, 2)}";
        SelectedPaths.ToolTip = view == null ? null : Details(view);
        OpenSourceButton.IsEnabled = (view?.Row.SourcePath ?? view?.Row.SourceCopyPath) != null;
        OpenTargetButton.IsEnabled = view?.Row.TargetPath != null;
        CopyButton.IsEnabled = view != null;
    }
    /// <summary>
    /// The file of one folder for a row, or, when it lies at another path (a moved file's copy in folder 2, a copy found in
    /// folder 1), that copy, said to be elsewhere so it is never taken for the file at the row's path.
    /// </summary>
    private static (string? Path, bool Elsewhere) RowFile(ComparisonRow row, int folder) => folder == 1
        ? row.SourcePath is not null ? (row.SourcePath, false) : (row.SourceCopyPath, row.SourceCopyPath is not null)
        : (row.TargetPath, row.Status == ComparisonStatus.Moved);
    private static string Side(ComparisonRow row, int folder) => RowFile(row, folder) is (var path, true)
        ? L.F($"{folder} (même contenu, autre chemin) : {path}", $"{folder} (same contents, other path): {path}")
        : $"{folder} : {RowFile(row, folder).Path ?? "—"}";
    private static string Details(RowView view)
    {
        var row = view.Row;
        var (first, firstElsewhere) = RowFile(row, 1);
        var (second, secondElsewhere) = RowFile(row, 2);
        // A copy found in folder 1 has the hash of the file of folder 2 it matched.
        var firstHash = row.SourceHash ?? (firstElsewhere ? row.TargetHash : null);
        string Label(int folder, bool elsewhere) => elsewhere ? L.F($"Copie dans le dossier {folder}", $"Copy in folder {folder}") : L.F($"Dossier {folder}", $"Folder {folder}");
        string Hash(int folder, bool elsewhere) => elsewhere ? L.F($"SHA-256 (copie dans {folder})", $"SHA-256 (copy in {folder})") : $"SHA-256 ({folder})";
        var none = L.T("Non calculé", "Not calculated");
        return $"{view.StatusLabel} — {view.KindLabel}\n{view.RelativePath}\n{row.Detail}\n{Label(1, firstElsewhere)}{L.T(" : ", ": ")}{first ?? "—"}\n{Label(2, secondElsewhere)}{L.T(" : ", ": ")}{second ?? "—"}\n" +
            $"{Hash(1, firstElsewhere)}{L.T(" : ", ": ")}{firstHash ?? none}\n{Hash(2, secondElsewhere)}{L.T(" : ", ": ")}{row.TargetHash ?? none}";
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is {} view) { Clipboard.SetText(Details(view)); FooterText.Text = L.T("Détails copiés dans le presse-papiers.", "Details copied to the clipboard."); }
    }
    private void OpenSource_Click(object sender, RoutedEventArgs e) => Reveal(Selected?.Row.SourcePath ?? Selected?.Row.SourceCopyPath);
    private void OpenTarget_Click(object sender, RoutedEventArgs e) => Reveal(Selected?.Row.TargetPath);
    private void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { MessageBox.Show(this, L.T("Cet élément n’est plus accessible. Relancez une comparaison si le dossier a changé.", "This item is no longer accessible. Run another comparison if the folder has changed."), L.T("Emplacement indisponible", "Location unavailable")); return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Ouverture impossible", "Cannot open location")); }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;
        var dialog = new SaveFileDialog { Title = L.T("Exporter le rapport complet dans un nouveau fichier", "Export the full report to a new file"), FileName = $"Checker San-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv", Filter = L.T("Tableau CSV pour Excel (*.csv)|*.csv|Rapport JSON complet (*.json)|*.json", "CSV for Excel (*.csv)|*.csv|Complete JSON report (*.json)|*.json"), AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        // An exported report must not overwrite a compared file or change the snapshot itself.
        if (Inside(dialog.FileName, _result.SourceRoot) || Inside(dialog.FileName, _result.TargetRoot))
        { MessageBox.Show(this, L.T("Enregistrez le rapport à l’extérieur des deux dossiers comparés, pour préserver les fichiers et l’état vérifié.", "Save the report outside both compared folders to preserve the files and verified state."), L.T("Choisissez un autre emplacement", "Choose another location"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try { ReportWriter.Write(dialog.FileName, _result); FooterText.Text = L.T("Rapport complet exporté : ", "Full report exported: ") + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Export impossible", "Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static bool Inside(string file, string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return Path.GetFullPath(file).StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var body = L.T(
        "1. Sélectionnez les dossiers 1 et 2, sans rôle prédéfini.\n2. Lancez la comparaison et attendez son terme.\n3. Consultez les deux verdicts d’inclusion et les écarts, puis exportez le rapport si besoin.\n\n" +
        "Les deux sens sont vérifiés pendant le même scan : le dossier 1 contient-il tout le dossier 2, et le dossier 2 contient-il tout le dossier 1 ? « Oui » exige les mêmes chemins relatifs, tous les dossiers et des contenus identiques. « Non vérifié » indique que le contrôle n’est pas complet ; un chemin présent d’un seul côté donne toutefois « Non », même avec des incidents. Aucune copie à conserver n’est désignée automatiquement.\n\n" +
        "Tous les types de fichiers sont comparés, y compris les documents, images, archives et fichiers sans extension.\n\n" +
        "Chemins réseau : collez un chemin \\\\PC\\Partage\\Dossier ou utilisez un lecteur réseau (Z:\\Dossier). Le partage doit être accessible dans l’Explorateur Windows avec votre session. Trois tentatives de reconnexion sont effectuées. Si elles échouent, la vérification reste incomplète. L’arrêt peut attendre le délai de réponse de Windows.\n\n" +
        "Identique : même chemin relatif, même taille et même SHA-256 calculé sur tout le fichier. Un nom ou une date identiques ne suffisent pas.\n\n" +
        "Retrouvé ailleurs : le contenu existe dans le dossier 2 sous un autre chemin. Cela ne suffit pas pour déclarer les arborescences identiques. Une même copie cible peut couvrir plusieurs doublons source.\n\n" +
        "Les tailles éliminent les comparaisons impossibles. Les fichiers sont lus par blocs, sans charger de gros fichiers en mémoire ; seules les tailles présentes dans les deux dossiers nécessitent des empreintes. Désactivez la recherche des déplacements pour ne vérifier que les mêmes chemins.\n\n" +
        "Les erreurs d’accès et les fichiers modifiés pendant le scan empêchent un verdict complet. Évitez toute modification pendant la vérification ; le rapport est un constat à cet instant. Les liens et jonctions sont signalés sans être suivis ni comparés. Des liens identiques des deux côtés n’empêchent pas le verdict ; un élément réel face à un lien, si : son contenu n’est pas comparé, et la vérification reste incomplète. Les fichiers OneDrive disponibles uniquement en ligne ne sont jamais téléchargés : leur contenu reste non vérifié.\n\n" +
        "Le contrôle couvre le contenu principal et l’arborescence, y compris les dossiers vides. Il ne compare pas les autorisations, dates, flux NTFS alternatifs ni métadonnées du système de fichiers. Les métadonnées intégrées aux fichiers audio/vidéo font partie du contenu comparé.\n\n" +
        "Vitesse : dans « Options du scan », la réutilisation des empreintes est activée par défaut : un fichier n’est pas relu si son identité, sa date de changement et ses métadonnées n’ont pas bougé. Elle fait confiance au système de fichiers et ne détecte ni une falsification délibérée de ces informations ni une corruption silencieuse du disque ; elle est désactivée sur FAT32 et exFAT. Décochez-la pour une preuve complète, qui recalcule tous les SHA-256. La présélection lit quelques blocs des gros fichiers pour écarter rapidement ceux qui diffèrent ; elle ne prouve jamais l’égalité, qui exige un SHA-256 complet. Deux copies identiques jamais vérifiées doivent toujours être lues entièrement, au débit du disque ou du réseau.\n\n" +
        "Reprise : « Arrêter et garder » enregistre les fichiers terminés. « Reprendre » restaure le dernier scan, même après fermeture. Le fichier interrompu est relu depuis le début. Les empreintes terminées sont réutilisées uniquement si les métadonnées, l’identité et la date de changement du fichier restent identiques.\n\n" +
        "Doublons : le bouton en haut ouvre le scan d’un seul dossier, sous-dossiers inclus. Les groupes sont confirmés par SHA-256 complet. Des filtres écartent les fichiers vides ou trop petits, les noms choisis (Thumbs.db, desktop.ini…) et les fichiers cachés ou système. Sélectionnez les copies à retirer, à la main ou avec une règle (le plus ancien, le plus récent, le chemin le plus court ou un dossier choisi), en gardant au moins un fichier par groupe. La Corbeille, disponible sur les disques locaux, libère l’espace une fois vidée. La mise à l’écart déplace les fichiers dans un dossier de quarantaine du même volume, sans libérer d’espace, et « Restaurer » les remet en place. La suppression définitive est une option distincte, avec confirmation. Les fichiers sont relus avant chaque action, et chaque action est inscrite dans le journal des actions. La liste des doublons s’exporte en CSV ou JSON.\n\n" +
        "Regrouper : à côté d’un verdict « Non », quand des fichiers peuvent être copiés, « Tout regrouper dans le 1 » ou « dans le 2 » copie dans ce dossier les fichiers dont il n’a aucun exemplaire, même ailleurs : un contenu retrouvé sous un autre chemin n’est pas recopié. Un fichier de moins de 4 Kio est toujours copié, car son nom compte plus que son contenu. Un fichier temporaire, une copie mise en quarantaine ou un fichier de corbeille ne comptent jamais comme exemplaire ; à la racine d’un disque, la Corbeille et les dossiers système sont écartés de la comparaison. Les éléments avec un incident sont écartés, avec ce qu’ils contiennent, sans bloquer le reste. Si la plupart des fichiers d’un dossier se trouvent dans un sous-dossier de l’autre, Checker San propose, pour une copie vers ce sous-dossier, de comparer avec lui ; les autres fichiers de ce dossier sortent alors de la comparaison.\n\n" +
        "Copie : après une comparaison menée à son terme, « Compléter les dossiers… » propose de compléter le dossier 1, le dossier 2, ou les deux en une opération, chemin par chemin, ou de tout regrouper dans l’un d’eux. Consultez la liste et le volume de chaque sens avant de lancer. Les dates et attributs des fichiers et des dossiers créés sont conservés, ainsi que la marque du Web ; les autres flux NTFS alternatifs et les autorisations ne sont pas copiés. L’espace libre est contrôlé avant de commencer, selon la taille réelle des clusters du disque. Un contenu retrouvé sous un autre nom est aussi recopié à son chemin manquant pour compléter l’arborescence. Les fichiers différents au même chemin et les conflits restent à résoudre séparément ; ils ne sont jamais écrasés.\n\n" +
        "« Compléter / reprendre… » poursuit une copie interrompue dans le ou les sens choisis, après contrôle de la partie déjà transférée. Chaque copie est vérifiée par SHA-256 avant de recevoir son nom définitif. Relancez ensuite une comparaison : une copie terminée ne signifie pas que les conflits ont disparu. « Exporter la liste… » enregistre en CSV ce qui serait copié, sans rien copier.\n\n" +
        "Instantanés : le bouton en haut enregistre le SHA-256 de chaque fichier d’un dossier, pour vérifier plus tard ce dossier ou une sauvegarde débranchée sans l’original.\n\n" +
        "Paires : « Paires… » enregistre les dossiers 1 et 2 sous un nom. Windows peut ensuite les comparer chaque jour ou chaque semaine, sans fenêtre : Checker San ne s’affiche que si un écart est prouvé ou si la vérification est incomplète. Chaque vérification laisse un rapport JSON et une ligne dans le journal d’activité.\n\nLa comparaison seule ne modifie aucun fichier. Seule l’action explicite du mode Doublons peut retirer les fichiers sélectionnés.",
        "1. Choose folders 1 and 2 in either order.\n2. Run the comparison and wait for it to finish.\n3. Review both containment verdicts and the differences, then export a report if needed.\n\nBoth directions are checked in the same scan. “Yes” requires matching relative paths, every folder and identical file contents. “Not verified” means the scan is incomplete; a path on one side only still gives “No”, even with issues. Neither folder is automatically designated as the copy to keep.\n\nAll file types are included, with hidden files, extensionless files and empty folders. Network shares and mapped drives must be accessible in Windows Explorer with your Windows account. Checker San retries temporary network failures three times; stopping may wait for Windows to return from a blocked network request.\n\nIdentical means the same path, size and complete SHA-256. Names or dates alone are insufficient. Found elsewhere means the content exists in folder 2 under a different path; this does not make the folder trees identical.\n\nScan options can reuse previous hashes: a file is not read again if its identity, change time and metadata are unchanged. Reuse trusts the file system and detects neither deliberate forging of that information nor silent disk corruption; it is off on FAT32 and exFAT. Turn it off for complete proof, recalculating every SHA-256. The sample prefilter rejects large files with different sampled bytes; matching samples always require a full SHA-256. A first scan of identical copies must still read all candidate bytes at disk or network speed. Disable moved-file detection if only matching paths matter.\n\nStop and save keeps completed work. Resume restores the previous scan, including after restarting the app. The interrupted file is read again from its beginning. Access errors and files changing during the scan prevent a complete verdict. Avoid editing the folders while scanning. Links and junctions are reported, never followed or compared. Matching links on both sides do not block the verdict; a real item facing a link does, as its contents are not compared and the check stays incomplete. Online-only OneDrive files are never downloaded, so their contents stay unverified.\n\nThe check covers the main file contents and folder structure, not permissions, NTFS alternate streams or equality of filesystem dates. Metadata embedded inside file contents is included. This is a check of the observed state, not a VSS snapshot.\n\nDuplicates in one folder scans a single tree and groups files by complete SHA-256. Filters leave out empty or small files, chosen names (Thumbs.db, desktop.ini…) and hidden or system files. Select the copies to remove by hand or with a rule (oldest, newest, shortest path or a chosen folder), keeping at least one file per group. The Recycle Bin, available on local drives, frees space once emptied. Quarantine moves files inside the same folder without freeing space, and Restore puts them back. Permanent deletion is separate and requires confirmation. Files are reread before every action, every action is recorded in the action journal, and the duplicate list can be exported as CSV or JSON.\n\nGather: next to a “No” verdict, when files can be copied, Gather everything into folder 1 or folder 2 copies into that folder the files it has no copy of, even elsewhere: contents found under another path are not copied again. A file under 4 KiB is always copied, as its name matters more than its contents. A temporary file, a quarantined copy or a file in a recycle bin never counts as a copy; at the root of a drive, the Recycle Bin and system folders are left out of the comparison. Items with an issue are left out, with what they contain, without holding back the rest. When most files of one folder sit in a subfolder of the other, Checker San suggests, for a copy into that subfolder, comparing with it; the other files of that folder then leave the comparison.\n\nComplete folders can add missing items in either direction or both at once, path by path, or gather everything into one of them. Review the paths and size before starting. Dates and attributes of files and of the folders created are kept, as is the mark of the Web; other NTFS alternate streams and permissions are not copied. Free space is checked before starting, using the drive’s real cluster size. Files found under another name are also copied to their missing path to complete the tree. Existing files, differing contents at the same path and file/folder conflicts are never overwritten.\n\nComplete / resume continues an interrupted copy after checking the existing partial data. Each completed copy is checked with SHA-256 before its final rename. Run a new comparison afterwards: completed copying does not resolve existing conflicts. Export the list saves what would be copied as CSV, without copying anything.\n\nSnapshots, in the header, record the SHA-256 of every file in a folder, so you can check that folder or a disconnected backup later without the original.\n\nPairs saves folders 1 and 2 under a name. Windows can then compare them every day or every week without a window: Checker San only shows up when a difference is proven or the check is incomplete. Every check leaves a JSON report and a line in the activity log.\n\nComparison is read-only. Only the explicit duplicate-removal action can remove selected original files.");
        var help = new Window { Owner = this, Title = "Checker San", Width = 730, Height = Math.Min(730, SystemParameters.WorkArea.Height - 40), MinHeight = 420, Background = RowView.Brush("#1A1A1A"), Foreground = RowView.Brush("#F0F0F0"), WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = 13 };
        var panel = new DockPanel { Margin = new Thickness(26) };
        var title = new TextBlock { Text = L.T("Comparer en toute clarté", "Understanding the comparison"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        var close = new Button { Content = L.T("Compris", "Got it"), Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsCancel = true };
        close.Click += (_, _) => help.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 0, 12, 0) } });
        help.Content = panel;
        help.SourceInitialized += (_, _) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(help).Handle, 20, ref dark, sizeof(int)); };
        help.ShowDialog();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellation == null) return;
        e.Cancel = true; _closeAfterCancel = true; _cancellation.Cancel();
        VerdictTitle.Text = L.T("Arrêt de la lecture avant fermeture…", "Stopping before closing…");
    }
}
