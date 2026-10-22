using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class CopyDialog : Window
{
    public CopyPlan? Plan { get; private set; }
    private enum CopyChoice { Both, Forward, Backward, Resume }
    private sealed record Choice(string Label, CopyChoice Mode);
    private int _version;
    public CopyDialog(ComparisonResult? result, CopyPlan? saved)
    {
        Title = "Checker San";
        Width = 880; Height = Math.Min(740, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        Closed += (_, _) => _version++;
        NameScope.SetNameScope(this, new NameScope());
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = L.T("Compléter les dossiers", "Complete folders"), FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = L.T("Ajoutez les fichiers et dossiers absents de chaque côté, ou choisissez un seul sens. Les originaux restent à leur place et les fichiers existants ne sont jamais écrasés.", "Add missing files and folders on both sides, or choose one direction. Originals stay in place and existing files are never overwritten."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16), Foreground = RowView.Brush("#ACACAC") });
        var direction = new ComboBox { Height = 40, DisplayMemberPath = nameof(Choice.Label) };
        RegisterName("DirectionBox", direction);
        bool canCreate = result is { Cancelled: false, SnapshotVerified: true } && result.Errors.Count == 0 &&
            result.Rows.All(row => row.Status != ComparisonStatus.Unverified);
        if (canCreate)
        {
            direction.Items.Add(new Choice(L.T("Compléter les deux dossiers (1 ⇄ 2)", "Complete both folders (1 ⇄ 2)"), CopyChoice.Both));
            direction.Items.Add(new Choice(L.T("Compléter le dossier 2 (1 → 2)", "Complete folder 2 (1 → 2)"), CopyChoice.Forward));
            direction.Items.Add(new Choice(L.T("Compléter le dossier 1 (2 → 1)", "Complete folder 1 (2 → 1)"), CopyChoice.Backward));
        }
        bool canResume = saved != null && saved.Items.Any(i => !i.Completed);
        if (canResume) direction.Items.Add(new Choice(L.T("Reprendre la copie enregistrée", "Resume saved copy"), CopyChoice.Resume));
        heading.Children.Add(direction);
        var summary = new TextBlock { Text = L.T("Choisissez le sens de la copie.", "Choose the copy direction."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) };
        RegisterName("SummaryText", summary);
        heading.Children.Add(summary);
        var conflictCount = result?.Rows.Count(row => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict) ?? 0;
        var exclusions = new TextBlock { Text = conflictCount > 0
            ? L.F($"{conflictCount:N0} contenu(s) différent(s) ou conflit(s) exclus : aucun remplacement automatique. Ces écarts resteront à résoudre après la copie.", $"{conflictCount:N0} different file(s) or conflict(s) excluded: no automatic replacement. These differences will still need resolving after copying.")
            : L.T("Seuls les chemins absents seront complétés. Un même chemin avec deux contenus différents doit être résolu séparément.", "Only missing paths will be filled. Different contents at the same path must be resolved separately."),
            TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush(conflictCount > 0 ? "#E8C395" : "#ACACAC"), Margin = new Thickness(0, 0, 0, 10) };
        RegisterName("ExclusionsText", exclusions);
        heading.Children.Add(exclusions);
        heading.Children.Add(new TextBlock { Text = L.T("Un contenu retrouvé sous un autre nom sera aussi recopié à son chemin manquant, pour compléter l’arborescence.", "Content found under a different name will also be copied to its missing path to complete the folder structure."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#ACACAC"), Margin = new Thickness(0, 0, 0, 12) });
        if (canResume) heading.Children.Add(new TextBlock { Text = L.T("Une copie incomplète est enregistrée. Reprenez-la pour conserver son avancement ; lancer une autre copie remplacera son journal.", "An incomplete copy is saved. Resume it to keep its progress; starting another copy replaces its journal."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#E8C395"), Margin = new Thickness(0, 0, 0, 12) });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = L.T("Annuler", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 9, 0) };
        var start = new Button { Content = L.T("Lancer la copie", "Start copying"), IsEnabled = false, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        RegisterName("StartCopyButton", start);
        footer.Children.Add(cancel); footer.Children.Add(start); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var list = new ListBox { Background = RowView.Brush("#101010"), Foreground = Foreground, BorderBrush = RowView.Brush("#333333"), Padding = new Thickness(10) };
        RegisterName("PreviewList", list);
        panel.Children.Add(list); Content = panel;
        cancel.Click += (_, _) => Close();
        start.Click += (_, _) => { if (Plan != null) { DialogResult = true; Close(); } };
        direction.SelectionChanged += async (_, _) =>
        {
            var version = ++_version; start.IsEnabled = false; Plan = null; list.ItemsSource = null; summary.Text = L.T("Préparation du récapitulatif…", "Preparing preview…");
            if (direction.SelectedItem is not Choice choice) return;
            try
            {
                bool resume = choice.Mode == CopyChoice.Resume;
                var plan = resume ? saved! : await Task.Run(() => choice.Mode == CopyChoice.Both
                    ? CopyEngine.CreateBidirectionalPlan(result!)
                    : CopyEngine.CreatePlan(result!, choice.Mode == CopyChoice.Forward));
                if (version != _version) return;
                Plan = plan;
                summary.Text = plan.Bidirectional
                    ? L.F($"Dossier 1 : {plan.SourceRoot}\nDossier 2 : {plan.TargetRoot}\n{DescribeDirection(plan, false)}\n{DescribeDirection(plan, true)}", $"Folder 1: {plan.SourceRoot}\nFolder 2: {plan.TargetRoot}\n{DescribeDirection(plan, false)}\n{DescribeDirection(plan, true)}")
                    : L.F($"Source : {plan.SourceRoot}\nDestination : {plan.TargetRoot}\n{plan.FileCount:N0} fichier(s) · {RowView.FormatSize(plan.TotalBytes)} · {plan.Items.Count(i => i.Kind == EntryKind.Directory):N0} dossier(s)", $"Source: {plan.SourceRoot}\nDestination: {plan.TargetRoot}\n{plan.FileCount:N0} file(s) · {RowView.FormatSize(plan.TotalBytes)} · {plan.Items.Count(i => i.Kind == EntryKind.Directory):N0} folder(s)");
                list.ItemsSource = plan.Items.Select(i => $"{(i.Completed ? "✓ " : "")}{(plan.Bidirectional ? i.ReverseDirection ? "[2 → 1] " : "[1 → 2] " : "")}[{(i.Kind == EntryKind.Directory ? L.T("Dossier", "Folder") : L.T("Fichier", "File"))}]  {i.RelativePath}").ToList();
                start.Content = resume ? L.T("Reprendre la copie", "Resume copying") : plan.Bidirectional ? L.T("Compléter les deux dossiers", "Complete both folders") : L.T("Lancer la copie", "Start copying");
                start.IsEnabled = plan.Items.Count > 0;
                if (plan.Items.Count == 0) summary.Text += L.T("\nAucun chemin manquant à copier dans ce sens.", "\nNo missing paths to copy in this direction.");
            }
            catch (Exception ex) { if (version == _version) summary.Text = ex.Message; }
        };
        if (direction.Items.Count > 0) direction.SelectedIndex = canResume ? direction.Items.Count - 1 : 0;
        else summary.Text = L.T("Effectuez une comparaison complète sans erreur pour préparer une copie.", "Finish a comparison without errors before preparing a copy.");
    }
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
