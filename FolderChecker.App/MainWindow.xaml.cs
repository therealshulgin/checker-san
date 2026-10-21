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

    public MainWindow()
    {
        InitializeComponent();
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
            if (args.Contains("--compare") && SourceBox.Text.Length > 0 && TargetBox.Text.Length > 0) await CompareAsync();
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void BrowseSource_Click(object sender, RoutedEventArgs e) => Browse(SourceBox, "Dossier 1");
    private void BrowseTarget_Click(object sender, RoutedEventArgs e) => Browse(TargetBox, "Dossier 2");
    private void Duplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        var dialog = new DuplicatesWindow(SourceBox.Text.Trim().Trim('"')) { Owner = this };
        dialog.ShowDialog();
        // Removing duplicates can change either compared tree, so never retain an earlier verdict.
        if (dialog.FilesChanged) Paths_Changed(SourceBox, null!);
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
        if (_result == null || _cancellation != null) return;
        _result = null; _rows = []; _view = null;
        ResultsGrid.ItemsSource = null; ExportButton.IsEnabled = false;
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        VerdictTitle.Text = "Les dossiers ont changé — relancez la comparaison";
        VerdictDetail.Text = "Les résultats précédents ne s’appliquent pas aux chemins sélectionnés.";
        ClearContainmentVerdicts();
        VerdictCard.Background = RowView.Brush("#242424"); VerdictCard.ToolTip = null;
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitle.Text = "Une nouvelle vérification est nécessaire";
        EmptyDetail.Text = "Lancez la comparaison avec les dossiers sélectionnés.";
        FooterText.Text = "Prêt • aucun verdict pour cette sélection";
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
    private void RefreshCheckpoint()
    {
        _checkpointInfo = ComparisonEngine.ReadCheckpointInfo(_checkpointPath);
        ResumeButton.IsEnabled = _cancellation == null && _checkpointInfo != null;
        ResumeHint.Text = _checkpointInfo == null
            ? "3 tentatives automatiques en cas de coupure réseau"
            : $"Reprise disponible · {_checkpointInfo.CompletedFileCount:N0} empreintes · {_checkpointInfo.SavedAt.ToLocalTime():dd/MM HH:mm}";
        ResumeButton.ToolTip = _checkpointInfo == null ? "Aucun point de reprise enregistré." : $"Reprendre ce scan :\n1 : {_checkpointInfo.SourceRoot}\n2 : {_checkpointInfo.TargetRoot}\nLes fichiers terminés seront recontrôlés avant réutilisation.";
    }
    private async Task CompareAsync(bool resume = false)
    {
        if (_cancellation != null) return;
        if (string.IsNullOrWhiteSpace(SourceBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text))
        { MessageBox.Show(this, "Sélectionnez les deux dossiers à comparer.", "Deux dossiers nécessaires", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        _result = null;
        _rows = [];
        ResultsGrid.ItemsSource = null;
        _view = null;
        ExportButton.IsEnabled = false;
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitle.Text = "Lecture des dossiers en cours…";
        EmptyDetail.Text = "Les résultats s’afficheront à la fin de la vérification.";
        IdenticalCount.Text = MissingCount.Text = DifferentCount.Text = MovedCount.Text = ExtraCount.Text = "—";
        VerdictTitle.Text = resume ? "Reprise de la vérification" : "Vérification en cours";
        VerdictDetail.Text = "Inventaire, comparaison des tailles, puis lecture complète des contenus à vérifier.";
        VerdictCard.Background = RowView.Brush("#242424");
        ScanProgress.Visibility = ProgressDetail.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ProgressDetail.Text = "Préparation…";
        _clock.Restart();
        _lastProgressTick = 0;
        var progress = new Progress<ComparisonProgress>(p =>
        {
            if (_cancellation == null) return;
            // Throttle layout updates; the engine also throttles its read loop notifications.
            if (_clock.ElapsedMilliseconds - _lastProgressTick < 100 && p.RetryAttempt == 0) return;
            _lastProgressTick = _clock.ElapsedMilliseconds;
            ScanProgress.IsIndeterminate = p.TotalHashBytes <= 0 || p.Phase.Contains("inventaire", StringComparison.OrdinalIgnoreCase) || p.Phase.Contains("stabilité", StringComparison.OrdinalIgnoreCase);
            ScanProgress.Value = p.TotalHashBytes > 0 ? Math.Min(100, 100.0 * p.BytesHashed / p.TotalHashBytes) : 0;
            VerdictDetail.Text = $"{p.Phase} · {p.FilesDiscovered:N0} fichiers recensés · {p.FilesHashed:N0} lus · {p.FilesReused:N0} repris";
            if (p.RetryAttempt > 0) VerdictTitle.Text = $"Reconnexion en cours · tentative {p.RetryAttempt}/3";
            else if (!_cancellation.IsCancellationRequested) VerdictTitle.Text = resume ? "Reprise de la vérification" : "Vérification en cours";
            var bytesRead = p.BytesHashed + p.SampleBytesRead;
            var rate = _clock.Elapsed.TotalSeconds >= 1 ? $" · {RowView.FormatSize((long)(bytesRead / _clock.Elapsed.TotalSeconds))}/s" : "";
            ProgressDetail.Text = $"{RowView.FormatSize(bytesRead)} lus{rate}  ·  {_clock.Elapsed:hh\\:mm\\:ss}  ·  {p.CurrentPath}";
            FooterText.Text = "Comparaison en cours • vous pouvez l’arrêter sans modifier vos fichiers";
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
        }
        catch (Exception ex)
        {
            VerdictTitle.Text = "La comparaison n’a pas pu aboutir";
            VerdictDetail.Text = ex.Message;
            VerdictCard.Background = RowView.Brush("#242424");
            EmptyTitle.Text = "Vérifiez les dossiers sélectionnés";
            EmptyDetail.Text = "Aucun verdict d’identité n’a été établi.";
            FooterText.Text = "Comparaison non aboutie • aucun fichier modifié";
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
        if (busy) ClearContainmentVerdicts();
        FolderInputs.IsEnabled = MovedCheck.IsEnabled = CompareButton.IsEnabled = !busy;
        DuplicatesButton.IsEnabled = ReuseHashesCheck.IsEnabled = SampleCheck.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ResumeButton.IsEnabled = !busy && _checkpointInfo != null;
        TransferButton.IsEnabled = false;
        CompareButton.Content = busy ? "Comparaison…" : "Comparer";
    }
    private void ClearContainmentVerdicts()
    {
        ContainsSourceVerdict.Text = "Le dossier 2 contient tout le dossier 1 : Non vérifié";
        ContainsTargetVerdict.Text = "Le dossier 1 contient tout le dossier 2 : Non vérifié";
        ContainsSourceVerdict.Foreground = ContainsTargetVerdict.Foreground = RowView.Brush("#AEAEAE");
    }
    private void ShowContainmentVerdicts(ComparisonResult result)
    {
        ClearContainmentVerdicts();
        if (result.Cancelled || !result.SnapshotVerified || result.Errors.Count > 0 ||
            result.Rows.Any(row => row.Status == ComparisonStatus.Unverified)) return;
        ContainsSourceVerdict.Text = $"Le dossier 2 contient tout le dossier 1 : {(result.SourceFullyContained ? "Oui" : "Non")}";
        ContainsTargetVerdict.Text = $"Le dossier 1 contient tout le dossier 2 : {(result.TargetFullyContained ? "Oui" : "Non")}";
        ContainsSourceVerdict.Foreground = RowView.Brush(result.SourceFullyContained ? "#A5D9BB" : "#E8C395");
        ContainsTargetVerdict.Foreground = RowView.Brush(result.TargetFullyContained ? "#A5D9BB" : "#E8C395");
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel(); CancelButton.IsEnabled = false;
        VerdictTitle.Text = "Arrêt demandé — attente de la réponse du disque ou du réseau…";
    }
    private void ShowResult(ComparisonResult result)
    {
        _rows = result.Rows.Select(r => new RowView(r)).OrderBy(r => Priority(r.Row.Status)).ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ResultsGrid.ItemsSource = _view;
        int Count(ComparisonStatus status) => result.Rows.Count(r => r.Status == status);
        IdenticalCount.Text = Count(ComparisonStatus.Identical).ToString("N0");
        MissingCount.Text = Count(ComparisonStatus.OnlySource).ToString("N0");
        DifferentCount.Text = (Count(ComparisonStatus.Different) + Count(ComparisonStatus.Conflict)).ToString("N0");
        MovedCount.Text = Count(ComparisonStatus.Moved).ToString("N0");
        ExtraCount.Text = Count(ComparisonStatus.OnlyTarget).ToString("N0");
        string title, detail, color;
        if (result.Cancelled)
            (title, detail, color) = ("Comparaison interrompue — résultat incomplet", result.CheckpointSaved ? "Les fichiers terminés sont enregistrés. Cliquez sur « Reprendre » pour continuer sans les relire inutilement." : "Les lignes ci-dessous sont partielles. Aucun point de reprise n’a pu être enregistré.", "#242424");
        else if (result.Errors.Count > 0 || !result.SnapshotVerified || Count(ComparisonStatus.Unverified) > 0)
            (title, detail, color) = ("Vérification incomplète — des éléments restent à contrôler", $"{result.Errors.Count:N0} incident(s). Consultez le filtre « Non vérifiés / erreurs » et le rapport avant de décider de supprimer une copie.", "#242424");
        else if (result.TreesIdentical)
            (title, detail, color) = ("Les deux dossiers sont identiques", "Mêmes chemins, mêmes dossiers et mêmes contenus de fichiers, confirmés par SHA-256. Résultat valable pour l’état contrôlé à la fin du scan.", "#242424");
        else if (result.SourceFullyContained)
            (title, detail, color) = ("Le dossier 2 contient tout le dossier 1", "Tous les éléments du dossier 1 sont présents aux mêmes chemins, avec des contenus identiques. Le dossier 2 contient aussi des éléments supplémentaires.", "#242424");
        else if (result.TargetFullyContained)
            (title, detail, color) = ("Le dossier 1 contient tout le dossier 2", "Tous les éléments du dossier 2 sont présents aux mêmes chemins, avec des contenus identiques. Le dossier 1 contient aussi des éléments supplémentaires.", "#242424");
        else
            (title, detail, color) = ("Aucun dossier ne contient entièrement l’autre", "Des éléments manquent au même chemin ou ont un contenu différent. Consultez « Tous les écarts » : les fichiers retrouvés ailleurs restent distincts des correspondances au même chemin.", "#242424");
        if (result.FilesReused > 0) detail += $" Reprise : {result.FilesReused:N0} empreintes réutilisées après contrôle de l’identité et des métadonnées des fichiers.";
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
        ComparisonStatus.Identical => 6, _ => 7
    };
    private bool FilterRow(object item)
    {
        if (item is not RowView view) return false;
        var row = view.Row;
        bool match = FilterBox.SelectedIndex switch
        {
            1 => row.Status is ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget or ComparisonStatus.Different or ComparisonStatus.Conflict or ComparisonStatus.Moved or ComparisonStatus.Unverified,
            2 => row.Status is ComparisonStatus.Identical or ComparisonStatus.CommonDirectory,
            3 => row.Status == ComparisonStatus.OnlySource,
            4 => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict,
            5 => row.Status == ComparisonStatus.Moved,
            6 => row.Status == ComparisonStatus.OnlyTarget,
            7 => row.Status == ComparisonStatus.Unverified,
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
        EmptyTitle.Text = _rows.Count == 0 ? "Aucun élément dans ces dossiers" : "Aucun élément pour ce filtre";
        EmptyDetail.Text = _rows.Count == 0 ? "Le verdict ci-dessus précise si le contrôle a abouti." : "Changez le filtre ou le texte de recherche.";
        if (_result != null)
        {
            var duration = _result.CompletedAt - _result.StartedAt;
            FooterText.Text = $"{count:N0} / {_rows.Count:N0} éléments · {_result.SourceFileCount:N0} fichiers dans le 1, {_result.TargetFileCount:N0} dans le 2 · {RowView.FormatSize(_result.BytesRead + _result.SampleBytesRead)} lus · {_result.FilesReused:N0} repris · {duration:hh\\:mm\\:ss} · {_result.Errors.Count:N0} incident(s)";
        }
    }
    private RowView? Selected => ResultsGrid.SelectedItem as RowView;
    private void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionTitle == null) return;
        var view = Selected;
        SelectionTitle.Text = view == null ? "Sélectionnez une ligne pour afficher ses détails." : view.RelativePath;
        SelectionDetail.Text = view?.Row.Detail ?? "";
        SelectedPaths.Text = view == null ? "" : $"1 : {view.Row.SourcePath ?? "—"}     |     2 : {view.Row.TargetPath ?? "—"}";
        SelectedPaths.ToolTip = view == null ? null : Details(view);
        OpenSourceButton.IsEnabled = view?.Row.SourcePath != null;
        OpenTargetButton.IsEnabled = view?.Row.TargetPath != null;
        CopyButton.IsEnabled = view != null;
    }
    private static string Details(RowView view) => $"{view.StatusLabel} — {view.KindLabel}\n{view.RelativePath}\n{view.Row.Detail}\nDossier 1 : {view.Row.SourcePath ?? "—"}\nDossier 2 : {view.Row.TargetPath ?? "—"}\nSHA-256 (1) : {view.Row.SourceHash ?? "Non calculé"}\nSHA-256 (2) : {view.Row.TargetHash ?? "Non calculé"}";
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is {} view) { Clipboard.SetText(Details(view)); FooterText.Text = "Détails copiés dans le presse-papiers."; }
    }
    private void OpenSource_Click(object sender, RoutedEventArgs e) => Reveal(Selected?.Row.SourcePath);
    private void OpenTarget_Click(object sender, RoutedEventArgs e) => Reveal(Selected?.Row.TargetPath);
    private void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { MessageBox.Show(this, "Cet élément n’est plus accessible. Relancez une comparaison si le dossier a changé.", "Emplacement indisponible"); return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ouverture impossible"); }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;
        var dialog = new SaveFileDialog { Title = "Exporter le rapport complet dans un nouveau fichier", FileName = $"Checker San-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv", Filter = "Tableau CSV pour Excel (*.csv)|*.csv|Rapport JSON complet (*.json)|*.json", AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        // An exported report must not overwrite a compared file or change the snapshot itself.
        if (Inside(dialog.FileName, _result.SourceRoot) || Inside(dialog.FileName, _result.TargetRoot))
        { MessageBox.Show(this, "Enregistrez le rapport à l’extérieur des deux dossiers comparés, pour préserver les fichiers et l’état vérifié.", "Choisissez un autre emplacement", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try { ReportWriter.Write(dialog.FileName, _result); FooterText.Text = "Rapport complet exporté : " + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export impossible", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static bool Inside(string file, string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return Path.GetFullPath(file).StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var body =
        "1. Sélectionnez les dossiers 1 et 2, sans rôle prédéfini.\n2. Lancez la comparaison et attendez son terme.\n3. Consultez les deux verdicts d’inclusion et les écarts, puis exportez le rapport si besoin.\n\n" +
        "Les deux sens sont vérifiés pendant le même scan : le dossier 1 contient-il tout le dossier 2, et le dossier 2 contient-il tout le dossier 1 ? « Oui » exige les mêmes chemins relatifs, tous les dossiers et des contenus identiques. « Non vérifié » indique que le contrôle n’est pas complet. Aucune copie à conserver n’est désignée automatiquement.\n\n" +
        "Tous les types de fichiers sont comparés, y compris les documents, images, archives et fichiers sans extension.\n\n" +
        "Chemins réseau : collez un chemin \\\\PC\\Partage\\Dossier ou utilisez un lecteur réseau (Z:\\Dossier). Le partage doit être accessible dans l’Explorateur Windows avec votre session. Trois tentatives de reconnexion sont effectuées. Si elles échouent, la vérification reste incomplète. L’arrêt peut attendre le délai de réponse de Windows.\n\n" +
        "Identique : même chemin relatif, même taille et même SHA-256 calculé sur tout le fichier. Un nom ou une date identiques ne suffisent pas.\n\n" +
        "Retrouvé ailleurs : le contenu existe dans le dossier 2 sous un autre chemin. Cela ne suffit pas pour déclarer les arborescences identiques. Une même copie cible peut couvrir plusieurs doublons source.\n\n" +
        "Les tailles éliminent les comparaisons impossibles. Les fichiers sont lus par blocs, sans charger de gros fichiers en mémoire ; seules les tailles présentes dans les deux dossiers nécessitent des empreintes. Désactivez la recherche des déplacements pour ne vérifier que les mêmes chemins.\n\n" +
        "Les erreurs d’accès, liens/jonctions ignorés et fichiers modifiés pendant le scan empêchent un verdict complet. Évitez toute modification pendant la vérification ; le rapport est un constat à cet instant.\n\n" +
        "Le contrôle couvre le contenu principal et l’arborescence, y compris les dossiers vides. Il ne compare pas les autorisations, dates, flux NTFS alternatifs ni métadonnées du système de fichiers. Les métadonnées intégrées aux fichiers audio/vidéo font partie du contenu comparé.\n\n" +
        "Vitesse : dans « Options du scan », la réutilisation des empreintes est activée par défaut et revalide chaque fichier. Désactivez-la pour recalculer tous les SHA-256. La présélection lit quelques blocs des gros fichiers pour écarter rapidement ceux qui diffèrent ; elle ne prouve jamais l’égalité, qui exige un SHA-256 complet. Deux copies identiques jamais vérifiées doivent toujours être lues entièrement, au débit du disque ou du réseau.\n\n" +
        "Reprise : « Arrêter et garder » enregistre les fichiers terminés. « Reprendre » restaure le dernier scan, même après fermeture. Le fichier interrompu est relu depuis le début. Les empreintes terminées sont réutilisées uniquement si les métadonnées, l’identité et la date de changement du fichier restent identiques.\n\n" +
        "Doublons : le bouton en haut ouvre le scan d’un seul dossier, sous-dossiers inclus. Les groupes sont confirmés par SHA-256 complet. Sélectionnez les copies à retirer, en gardant au moins un fichier par groupe. La mise à l’écart déplace les fichiers dans un dossier de quarantaine du même volume et ne libère pas d’espace. La suppression définitive est une option distincte, avec confirmation et relecture des fichiers avant action.\n\n" +
        "Copie : après une comparaison complète, « Compléter les dossiers… » propose de compléter le dossier 1, le dossier 2, ou les deux en une opération. Consultez la liste et le volume de chaque sens avant de lancer. Un contenu retrouvé sous un autre nom est aussi recopié à son chemin manquant pour compléter l’arborescence. Les fichiers différents au même chemin et les conflits restent à résoudre séparément ; ils ne sont jamais écrasés.\n\n" +
        "« Compléter / reprendre… » poursuit une copie interrompue dans le ou les sens choisis, après contrôle de la partie déjà transférée. Chaque copie est vérifiée par SHA-256 avant de recevoir son nom définitif. Relancez ensuite une comparaison : une copie terminée ne signifie pas que les conflits ont disparu.\n\nLa comparaison seule ne modifie aucun fichier. Seule l’action explicite du mode Doublons peut retirer les fichiers sélectionnés.";
        var help = new Window { Owner = this, Title = "Checker San", Width = 730, Height = Math.Min(730, SystemParameters.WorkArea.Height - 40), MinHeight = 420, Background = RowView.Brush("#1A1A1A"), Foreground = RowView.Brush("#F0F0F0"), WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = 13 };
        var panel = new DockPanel { Margin = new Thickness(26) };
        var title = new TextBlock { Text = "Comparer en toute clarté", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        var close = new Button { Content = "Compris", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsCancel = true };
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
        VerdictTitle.Text = "Arrêt de la lecture avant fermeture…";
    }
}
