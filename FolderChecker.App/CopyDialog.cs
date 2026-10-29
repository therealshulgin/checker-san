using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class CopyDialog : Window
{
    public enum Mode { Both, Forward, Backward, IntoFolder2, IntoFolder1, Resume }
    public CopyPlan? Plan { get; private set; }
    public Mode? SelectedMode => (FindName("DirectionBox") as ComboBox)?.SelectedItem is Choice choice ? choice.Mode : null;
    /// <summary>Set when the user chose to compare with the subfolder holding most files instead of copying.</summary>
    public FolderAlignment.Suggestion? CompareInstead { get; private set; }
    // The closed box shows the item itself where the display path is not applied: it must read as its label.
    private sealed record Choice(string Label, Mode Mode) { public override string ToString() => Label; }
    private int _version;
    public CopyDialog(ComparisonResult? result, CopyPlan? saved, Mode? initial = null)
    {
        Title = "Checker San";
        Width = 880; Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        Closed += (_, _) => _version++;
        NameScope.SetNameScope(this, new NameScope());
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel();
        var title = new TextBlock { Text = L.T("Compléter les dossiers", "Complete folders"), FontSize = 25, FontWeight = FontWeights.SemiBold };
        RegisterName("DialogTitle", title);
        heading.Children.Add(title);
        heading.Children.Add(new TextBlock { Text = L.T("Regroupez tout dans l’un des deux dossiers, ou complétez-les chemin par chemin. Les originaux restent à leur place et les fichiers existants ne sont jamais écrasés. Dates, attributs et marque du Web sont conservés ; les autres flux NTFS et les autorisations ne sont pas copiés.", "Gather everything into one of the two folders, or complete them path by path. Originals stay in place and existing files are never overwritten. Dates, attributes and the mark of the Web are kept; other NTFS streams and permissions are not copied."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16), Foreground = RowView.Brush("#ACACAC") });
        var aligned = result is null ? null : FolderAlignment.Suggest(result);
        // Shown only for a copy into the folder holding the subfolder: narrowing the folder that supplies the files
        // would take them out of the comparison instead of placing the copies beside their kin.
        var hint = new DockPanel { Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
        if (aligned is not null)
        {
            var instead = new Button { Content = L.T("Comparer avec ce dossier", "Compare with this folder"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            RegisterName("CompareInsteadButton", instead);
            instead.Click += (_, _) => { CompareInstead = aligned; DialogResult = false; Close(); };
            DockPanel.SetDock(instead, Dock.Right); hint.Children.Add(instead);
            var text = new TextBlock { Text = AlignmentText(aligned), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#E8C395"), VerticalAlignment = VerticalAlignment.Center };
            RegisterName("AlignmentText", text);
            hint.Children.Add(text);
            heading.Children.Add(hint);
        }
        var direction = new ComboBox { Height = 40, DisplayMemberPath = nameof(Choice.Label) };
        RegisterName("DirectionBox", direction);
        bool canCreate = result is not null && CopyEngine.CanCreatePlan(result);
        if (canCreate)
        {
            direction.Items.Add(new Choice(L.T("Compléter les deux dossiers (1 ⇄ 2)", "Complete both folders (1 ⇄ 2)"), Mode.Both));
            direction.Items.Add(new Choice(L.T("Compléter le dossier 2 (1 → 2)", "Complete folder 2 (1 → 2)"), Mode.Forward));
            direction.Items.Add(new Choice(L.T("Compléter le dossier 1 (2 → 1)", "Complete folder 1 (2 → 1)"), Mode.Backward));
            // Without the search for moved files nothing is known elsewhere: the promise would be false.
            var known = result!.MovedFilesSearched ? L.T(", sans recopier ce qu’il a déjà", ", without copying what it already has") : "";
            direction.Items.Add(new Choice(L.F($"Tout regrouper dans le dossier 2{known} (1 → 2)", $"Gather everything into folder 2{known} (1 → 2)"), Mode.IntoFolder2));
            direction.Items.Add(new Choice(L.F($"Tout regrouper dans le dossier 1{known} (2 → 1)", $"Gather everything into folder 1{known} (2 → 1)"), Mode.IntoFolder1));
        }
        bool canResume = saved != null && saved.Items.Any(i => !i.Completed);
        // An unfinished copy of these same folders, in either direction, comes first: another copy would replace its
        // journal and strand its temporary in the destination.
        bool resumeFirst = canResume && result is not null && SamePair(saved!, result);
        if (canResume) direction.Items.Add(new Choice(L.T("Reprendre la copie enregistrée", "Resume saved copy"), Mode.Resume));
        heading.Children.Add(direction);
        var summary = new TextBlock { Text = L.T("Choisissez le sens de la copie.", "Choose the copy direction."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) };
        RegisterName("SummaryText", summary);
        heading.Children.Add(summary);
        var conflictCount = result?.Rows.Count(row => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict) ?? 0;
        string Exclusions(bool gather) => conflictCount > 0
            ? gather
                ? L.F($"{conflictCount:N0} contenu(s) différent(s) ou conflit(s) au même chemin : aucune des deux versions n’est remplacée ni copiée sous un autre nom. Ces écarts resteront à résoudre.", $"{conflictCount:N0} different file(s) or conflict(s) at the same path: neither version is replaced or copied under another name. These differences will still need resolving.")
                : L.F($"{conflictCount:N0} contenu(s) différent(s) ou conflit(s) exclus : aucun remplacement automatique. Ces écarts resteront à résoudre après la copie.", $"{conflictCount:N0} different file(s) or conflict(s) excluded: no automatic replacement. These differences will still need resolving after copying.")
            : gather
                ? L.T("Seuls les contenus absents de ce dossier seront copiés.", "Only contents missing from this folder will be copied.")
                : L.T("Seuls les chemins absents seront complétés. Un même chemin avec deux contenus différents doit être résolu séparément.", "Only missing paths will be filled. Different contents at the same path must be resolved separately.");
        var exclusions = new TextBlock { Text = Exclusions(initial is Mode.IntoFolder1 or Mode.IntoFolder2),
            TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush(conflictCount > 0 ? "#E8C395" : "#ACACAC"), Margin = new Thickness(0, 0, 0, 10) };
        RegisterName("ExclusionsText", exclusions);
        heading.Children.Add(exclusions);
        var incidents = result?.Rows.Count(row => row.Status == ComparisonStatus.Unverified && !row.OnlineOnly) ?? 0;
        if (canCreate && incidents > 0)
        {
            var incidentText = new TextBlock { Text = L.F($"{incidents:N0} élément(s) avec un incident (illisible, modifié pendant la comparaison…) écarté(s), avec ce qu’ils contiennent : ils ne seront pas copiés. Consultez le filtre « Non vérifiés, liens, erreurs ».", $"{incidents:N0} item(s) with an issue (unreadable, changed during the comparison…) left out, with what they contain: they will not be copied. Check the Not verified, links, errors filter."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#E8C395"), Margin = new Thickness(0, 0, 0, 10) };
            RegisterName("IncidentsText", incidentText);
            heading.Children.Add(incidentText);
        }
        var modeNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#ACACAC"), Margin = new Thickness(0, 0, 0, 12) };
        RegisterName("ModeNote", modeNote);
        heading.Children.Add(modeNote);
        var onlineOnly = result?.Rows.Count(row => row.OnlineOnly && row.Status is ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget) ?? 0;
        if (onlineOnly > 0) heading.Children.Add(new TextBlock { Text = L.F($"{onlineOnly:N0} fichier(s) disponible(s) uniquement en ligne ne seront pas copiés, pour ne pas les télécharger. Rendez-les disponibles sur cet appareil puis comparez à nouveau pour les inclure.", $"{onlineOnly:N0} online-only file(s) will not be copied, to avoid downloading them. Make them available on this device, then compare again to include them."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#E8C395"), Margin = new Thickness(0, 0, 0, 12) });
        if (canResume) heading.Children.Add(new TextBlock { Text = resumeFirst
            ? L.T("Une copie inachevée de ces dossiers est enregistrée : elle est proposée d’abord. Reprenez-la avant toute autre copie, qui remplacerait son journal et laisserait son fichier temporaire dans la destination.", "An unfinished copy of these folders is saved, so it comes first. Resume it before any other copy, which would replace its journal and leave its temporary file in the destination.")
            : L.T("Une copie incomplète est enregistrée. Reprenez-la pour conserver son avancement ; lancer une autre copie remplacera son journal.", "An incomplete copy is saved. Resume it to keep its progress; starting another copy replaces its journal."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#E8C395"), Margin = new Thickness(0, 0, 0, 12) });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var export = new Button { Content = L.T("Exporter la liste…", "Export the list…"), IsEnabled = false, Margin = new Thickness(0, 0, 9, 0), ToolTip = L.T("Enregistre en CSV ce qui serait copié, sans rien copier.", "Saves what would be copied as CSV, without copying anything.") };
        var cancel = new Button { Content = L.T("Annuler", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 9, 0) };
        var start = new Button { Content = L.T("Lancer la copie", "Start copying"), IsEnabled = false, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        RegisterName("StartCopyButton", start); RegisterName("ExportPlanButton", export);
        footer.Children.Add(export); footer.Children.Add(cancel); footer.Children.Add(start); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var list = new ListBox { Background = RowView.Brush("#101010"), Foreground = Foreground, BorderBrush = RowView.Brush("#333333"), Padding = new Thickness(10) };
        RegisterName("PreviewList", list);
        panel.Children.Add(list); Content = panel;
        cancel.Click += (_, _) => Close();
        start.Click += (_, _) =>
        {
            if (Plan == null) return;
            // Another copy replaces the saved journal: its temporaries would stay behind in the destination, unknown to any journal.
            if (canResume && SelectedMode != Mode.Resume && !ConfirmReplacing(saved!)) return;
            DialogResult = true; Close();
        };
        export.Click += (_, _) =>
        {
            if (Plan is not { } plan) return;
            var save = new Microsoft.Win32.SaveFileDialog { Title = L.T("Exporter ce qui serait copié", "Export what would be copied"), FileName = $"copy preview {DateTime.Now:yyyy-MM-dd HHmm}.csv", Filter = "CSV (*.csv)|*.csv", AddExtension = true, OverwritePrompt = false };
            if (save.ShowDialog(this) != true) return;
            try { CopyPlanWriter.Write(save.FileName, plan); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { MessageBox.Show(this, ex.Message, "Checker San"); }
        };
        direction.SelectionChanged += async (_, _) =>
        {
            var version = ++_version; start.IsEnabled = export.IsEnabled = false; Plan = null; list.ItemsSource = null; summary.Text = L.T("Préparation du récapitulatif…", "Preparing preview…");
            if (direction.SelectedItem is not Choice choice) return;
            var gather = choice.Mode is Mode.IntoFolder1 or Mode.IntoFolder2;
            title.Text = gather ? L.T("Tout regrouper dans un dossier", "Gather everything into one folder") : L.T("Compléter les dossiers", "Complete folders");
            exclusions.Text = Exclusions(gather);
            hint.Visibility = aligned is not null && (aligned.InFolder2
                ? choice.Mode is Mode.IntoFolder2 or Mode.Forward
                : choice.Mode is Mode.IntoFolder1 or Mode.Backward) ? Visibility.Visible : Visibility.Collapsed;
            modeNote.Text = gather
                ? L.T("Un fichier dont le même contenu (SHA-256) se trouve déjà ailleurs dans le dossier de destination n’est pas recopié : seuls les contenus absents y sont ajoutés, à leur chemin relatif.", "A file whose identical contents (SHA-256) are already elsewhere in the destination folder is not copied again: only missing contents are added, at their relative path.")
                    + (result is { MovedFilesSearched: false } ? L.T(" La recherche des fichiers déplacés était désactivée : chaque fichier absent au même chemin sera copié, même s’il existe ailleurs.", " The search for moved files was turned off: every file missing at the same path will be copied, even if it exists elsewhere.") : "")
                : choice.Mode == Mode.Resume ? ""
                : L.T("Un contenu retrouvé sous un autre nom sera aussi recopié à son chemin manquant, pour compléter l’arborescence.", "Content found under a different name will also be copied to its missing path to complete the folder structure.");
            try
            {
                var (plan, needs, shortfall) = await Task.Run(() =>
                {
                    var prepared = choice.Mode switch
                    {
                        Mode.Resume => saved!,
                        Mode.Both => CopyEngine.CreateBidirectionalPlan(result!),
                        Mode.IntoFolder1 or Mode.IntoFolder2 => CopyEngine.CreateGatherPlan(result!, intoTarget: choice.Mode == Mode.IntoFolder2),
                        _ => CopyEngine.CreatePlan(result!, choice.Mode == Mode.Forward)
                    };
                    return (prepared, CopyEngine.SpaceNeeds(prepared), gather ? CopyEngine.GatherShortfall(result!, choice.Mode == Mode.IntoFolder2) : 0);
                });
                if (version != _version) return;
                Plan = plan;
                summary.Text = plan.Bidirectional
                    ? L.F($"Dossier 1 : {plan.SourceRoot}\nDossier 2 : {plan.TargetRoot}\n{DescribeDirection(plan, false)}\n{DescribeDirection(plan, true)}", $"Folder 1: {plan.SourceRoot}\nFolder 2: {plan.TargetRoot}\n{DescribeDirection(plan, false)}\n{DescribeDirection(plan, true)}")
                    : L.F($"Source : {plan.SourceRoot}\nDestination : {plan.TargetRoot}\n{plan.FileCount:N0} fichier(s) · {RowView.FormatSize(plan.TotalBytes)} · {plan.Items.Count(i => i.Kind == EntryKind.Directory):N0} dossier(s)", $"Source: {plan.SourceRoot}\nDestination: {plan.TargetRoot}\n{plan.FileCount:N0} file(s) · {RowView.FormatSize(plan.TotalBytes)} · {plan.Items.Count(i => i.Kind == EntryKind.Directory):N0} folder(s)");
                list.ItemsSource = plan.Items.Select(i => $"{(i.Completed ? "✓ " : "")}{(plan.Bidirectional ? i.ReverseDirection ? "[2 → 1] " : "[1 → 2] " : "")}[{(i.Kind == EntryKind.Directory ? L.T("Dossier", "Folder") : L.T("Fichier", "File"))}]  {i.RelativePath}").ToList();
                start.Content = choice.Mode switch
                {
                    Mode.Resume => L.T("Reprendre la copie", "Resume copying"),
                    Mode.Both => L.T("Compléter les deux dossiers", "Complete both folders"),
                    Mode.IntoFolder1 => L.T("Regrouper dans le dossier 1", "Gather into folder 1"),
                    Mode.IntoFolder2 => L.T("Regrouper dans le dossier 2", "Gather into folder 2"),
                    _ => L.T("Lancer la copie", "Start copying")
                };
                foreach (var need in needs.Where(need => need.Required > 0))
                    summary.Text += L.F($"\nEspace libre sur {need.Root} : {L.Size(need.Available)} · nécessaire : {L.Size(need.Required)}", $"\nFree space on {need.Root}: {L.Size(need.Available)} · needed: {L.Size(need.Required)}");
                var enough = needs.All(need => need.Sufficient);
                if (!enough) summary.Text += L.T("\nEspace insuffisant : libérez de la place avant de lancer la copie.", "\nNot enough space: free some space before starting the copy.");
                start.IsEnabled = plan.Items.Count > 0 && enough;
                export.IsEnabled = plan.Items.Count > 0;
                // "Every file is there" only on a proof from the comparison, never because nothing was left to copy:
                // other versions, incidents, links or odd names can leave files behind without anything to copy.
                var covered = choice.Mode == Mode.IntoFolder2 ? result!.ContentCovered : choice.Mode == Mode.IntoFolder1 && result!.TargetContentCovered;
                if (plan.Items.Count == 0)
                    summary.Text += !gather ? L.T("\nAucun chemin manquant à copier dans ce sens.", "\nNo missing paths to copy in this direction.")
                        : covered ? L.T("\nRien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.", "\nNothing to gather: this folder already has a copy of every file.")
                        : L.T("\nRien à copier automatiquement, mais ce dossier n’a pas d’exemplaire prouvé de chaque fichier.", "\nNothing can be copied automatically, but this folder does not have a proven copy of every file.");
                if (gather && shortfall > 0)
                    summary.Text += L.F($"\n{shortfall:N0} élément(s) resteront sans exemplaire prouvé dans ce dossier (autre version au même chemin, incident, lien, fichier en ligne, nom non portable) : ne supprimez pas l’autre dossier avant de les avoir traités.", $"\n{shortfall:N0} item(s) will still have no proven copy in this folder (another version at the same path, an issue, a link, an online-only file, an unsupported name): do not delete the other folder before dealing with them.");
                if (plan.UnsupportedNames > 0)
                    summary.Text += L.F($"\n{plan.UnsupportedNames:N0} chemin(s) au nom que Windows modifierait (point ou espace final, nom réservé ou mal formé) écarté(s) : à copier à la main.", $"\n{plan.UnsupportedNames:N0} path(s) with a name Windows would alter (final dot or space, reserved or malformed name) left out: copy them by hand.");
                if (plan.AbandonedTemporaries > 0)
                    summary.Text += L.F($"\n{plan.AbandonedTemporaries:N0} fichier(s) temporaire(s) d’une copie interrompue écarté(s) : ils ne sont jamais recopiés.", $"\n{plan.AbandonedTemporaries:N0} temporary file(s) from an interrupted copy left out: they are never copied over.");
                if (plan.UnreadableFiles > 0)
                    summary.Text += L.F($"\n{plan.UnreadableFiles:N0} fichier(s) disparu(s) ou illisible(s) depuis la comparaison écarté(s) : comparez à nouveau pour les retrouver.", $"\n{plan.UnreadableFiles:N0} file(s) gone or unreadable since the comparison left out: compare again to find them.");
            }
            catch (Exception ex) { if (version == _version) summary.Text = ex.Message; }
        };
        var first = resumeFirst ? null : direction.Items.Cast<Choice>().FirstOrDefault(choice => choice.Mode == initial);
        if (first is not null) direction.SelectedItem = first;
        else if (direction.Items.Count > 0) direction.SelectedIndex = canResume ? direction.Items.Count - 1 : 0;
        else summary.Text = L.T("Terminez une comparaison, sans l’arrêter et avec les deux dossiers lisibles, pour préparer une copie.", "Finish a comparison, without stopping it and with both folders readable, to prepare a copy.");
    }
    private bool ConfirmReplacing(CopyPlan saved)
    {
        var (count, bytes) = OrphanTemporaries(saved);
        var text = L.T("Une copie inachevée est enregistrée : la remplacer ? Son avancement sera perdu.", "An unfinished copy is saved: replace it? Its progress will be lost.")
            + (count > 0 ? L.F($"\n\nSes {count:N0} fichier(s) temporaire(s) ({RowView.FormatSize(bytes)}) resteront dans la destination, sous des noms « .checkersan-….partial » : vous pourrez les supprimer.", $"\n\nIts {count:N0} temporary file(s) ({RowView.FormatSize(bytes)}) will stay in the destination, named “.checkersan-….partial”: you can delete them.") : "");
        return MessageBox.Show(this, text, "Checker San", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    /// <summary>Temporaries the saved copy has started and not finished, which a new journal would no longer know.</summary>
    internal static (int Count, long Bytes) OrphanTemporaries(CopyPlan saved)
    {
        var count = 0; long bytes = 0;
        foreach (var item in saved.Items.Where(item => item is { Completed: false, Kind: EntryKind.File, PartialIdentity: not null }))
        {
            var root = item.ReverseDirection ? saved.SourceRoot : saved.TargetRoot;
            try
            {
                var partial = new FileInfo(Path.Combine(Path.GetDirectoryName(Path.Combine(root, item.RelativePath))!, item.PartialName));
                if (!partial.Exists) continue;
                count++; bytes += partial.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return (count, bytes);
    }

    internal static bool SamePair(CopyPlan plan, ComparisonResult result)
    {
        static string Key(string path)
        {
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch (ArgumentException) { return path; }
        }
        bool Same(string first, string second) => string.Equals(Key(first), Key(second), StringComparison.OrdinalIgnoreCase);
        return Same(plan.SourceRoot, result.SourceRoot) && Same(plan.TargetRoot, result.TargetRoot) ||
            Same(plan.SourceRoot, result.TargetRoot) && Same(plan.TargetRoot, result.SourceRoot);
    }
    /// <summary>For a copy into the folder holding the subfolder: what comparing with it gains, and what leaves the comparison.</summary>
    internal static string AlignmentText(FolderAlignment.Suggestion aligned) => aligned.InFolder2
        ? L.F($"{aligned.Files:N0} fichier(s) du dossier 1 sont rangés à l’identique dans « {aligned.Folder} ». Pour que les copies arrivent à côté d’eux, comparez le dossier 1 avec ce sous-dossier.", $"{aligned.Files:N0} file(s) of folder 1 are laid out the same way in ‘{aligned.Folder}’. For copies to land beside them, compare folder 1 with that subfolder.")
            + (aligned.Outside > 0 ? L.F($" Les {aligned.Outside:N0} fichier(s) du dossier 2 situés hors de ce sous-dossier sortiraient alors de la comparaison.", $" The {aligned.Outside:N0} file(s) of folder 2 outside that subfolder would then leave the comparison.") : "")
        : L.F($"{aligned.Files:N0} fichier(s) du dossier 2 sont rangés à l’identique dans « {aligned.Folder} ». Pour que les copies arrivent à côté d’eux, comparez ce sous-dossier avec le dossier 2.", $"{aligned.Files:N0} file(s) of folder 2 are laid out the same way in ‘{aligned.Folder}’. For copies to land beside them, compare that subfolder with folder 2.")
            + (aligned.Outside > 0 ? L.F($" Les {aligned.Outside:N0} fichier(s) du dossier 1 situés hors de ce sous-dossier sortiraient alors de la comparaison.", $" The {aligned.Outside:N0} file(s) of folder 1 outside that subfolder would then leave the comparison.") : "");
    /// <summary>For the verdict, which knows no copy direction: the subfolder helps one direction and harms the other.</summary>
    internal static string AlignmentVerdictText(FolderAlignment.Suggestion aligned) => aligned.InFolder2
        ? L.F($"{aligned.Files:N0} fichier(s) du dossier 1 sont rangés à l’identique dans « {aligned.Folder} ». Pour regrouper dans le dossier 2 à côté d’eux, comparez le dossier 1 avec ce sous-dossier.", $"{aligned.Files:N0} file(s) of folder 1 are laid out the same way in ‘{aligned.Folder}’. To gather into folder 2 beside them, compare folder 1 with that subfolder.")
            + (aligned.Outside > 0 ? L.F($" Pour regrouper dans le dossier 1, gardez cette comparaison : les {aligned.Outside:N0} fichier(s) du dossier 2 hors de ce sous-dossier en sortiraient.", $" To gather into folder 1, keep this comparison: the {aligned.Outside:N0} file(s) of folder 2 outside that subfolder would leave it.") : "")
        : L.F($"{aligned.Files:N0} fichier(s) du dossier 2 sont rangés à l’identique dans « {aligned.Folder} ». Pour regrouper dans le dossier 1 à côté d’eux, comparez ce sous-dossier avec le dossier 2.", $"{aligned.Files:N0} file(s) of folder 2 are laid out the same way in ‘{aligned.Folder}’. To gather into folder 1 beside them, compare that subfolder with folder 2.")
            + (aligned.Outside > 0 ? L.F($" Pour regrouper dans le dossier 2, gardez cette comparaison : les {aligned.Outside:N0} fichier(s) du dossier 1 hors de ce sous-dossier en sortiraient.", $" To gather into folder 2, keep this comparison: the {aligned.Outside:N0} file(s) of folder 1 outside that subfolder would leave it.") : "");
    private static string DescribeDirection(CopyPlan plan, bool reverse)
    {
        var items = plan.Items.Where(item => item.ReverseDirection == reverse).ToArray();
        var label = reverse ? "2 → 1" : "1 → 2";
        var files = items.Count(item => item.Kind == EntryKind.File);
        var size = RowView.FormatSize(items.Where(item => item.Kind == EntryKind.File).Sum(item => item.Size));
        var folders = items.Count(item => item.Kind == EntryKind.Directory);
        return L.F($"{label} : {files:N0} fichier(s) · {size} · {folders:N0} dossier(s)",
            $"{label}: {files:N0} file(s) · {size} · {folders:N0} folder(s)");
    }
}
