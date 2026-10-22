using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class DuplicateRemovalDialog : Window
{
    public DuplicateRemovalDialog(DuplicateResult scan, IReadOnlyCollection<string> paths, DuplicateRemovalMode mode)
    {
        Title = "Checker San"; Width = 850; Height = Math.Min(700, SystemParameters.WorkArea.Height - 35);
        MinWidth = 650; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#151515"); Foreground = RowView.Brush("#F0F0F0"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        NameScope.SetNameScope(this, new NameScope());
        var chosen = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permanent = mode == DuplicateRemovalMode.Permanent;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = permanent ? L.T("Supprimer les copies sélectionnées", "Delete selected copies") : L.T("Mettre les copies à l’écart", "Move copies aside"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        var selectedFiles = scan.Groups.SelectMany(group => group.Files).Where(file => chosen.Contains(file.FullPath)).ToArray();
        header.Children.Add(new TextBlock { Text = L.F($"{selectedFiles.Length:N0} fichier(s) · {RowView.FormatSize(selectedFiles.Sum(file => file.Size))}\nDossier : {scan.Root}", $"{selectedFiles.Length:N0} file(s) · {RowView.FormatSize(selectedFiles.Sum(file => file.Size))}\nFolder: {scan.Root}"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        header.Children.Add(new TextBlock { Text = permanent
            ? L.T("Les fichiers ci-dessous seront supprimés définitivement, sans Corbeille. Vérifiez les chemins des fichiers retirés et des exemplaires conservés.", "The files listed below will be permanently deleted without the Recycle Bin. Review the paths of both removed and retained copies.")
            : L.T("Les fichiers seront déplacés dans .CheckerSan-Quarantine, avec leurs chemins relatifs. Vous pourrez les remettre en place manuellement. Cette opération ne libère pas d’espace.", "Files will be moved into .CheckerSan-Quarantine with their relative paths preserved. You can restore them manually. This does not free any space."),
            Foreground = RowView.Brush("#B8B8B8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        header.Children.Add(new TextBlock { Text = L.T("Les doublons concernent le contenu principal des fichiers. Les noms, dates, autorisations et flux NTFS alternatifs peuvent différer.", "Duplicates refer to the main file contents. Names, dates, permissions and NTFS alternate streams may differ."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#B8B8B8"), Margin = new Thickness(0, 0, 0, 14) });
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var consent = new CheckBox { Content = L.T("Je confirme la suppression définitive des fichiers listés.", "I confirm permanent deletion of the listed files."), Margin = new Thickness(0, 0, 0, 14), Visibility = permanent ? Visibility.Visible : Visibility.Collapsed };
        RegisterName("PermanentConsent", consent); footer.Children.Add(consent);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.T("Annuler", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = permanent ? L.T("Supprimer définitivement", "Delete permanently") : L.T("Mettre à l’écart", "Move aside"), IsEnabled = false, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        RegisterName("ConfirmRemovalButton", confirm);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var lines = new List<string>();
        bool valid = chosen.Count > 0 && selectedFiles.Length == chosen.Count && scan.SnapshotVerified && !scan.Cancelled && scan.Errors.Count == 0;
        foreach (var group in scan.Groups.Where(group => group.Files.Any(file => chosen.Contains(file.FullPath))))
        {
            var kept = group.Files.Where(file => !chosen.Contains(file.FullPath)).ToArray();
            valid &= kept.Length > 0;
            lines.Add(L.T("SHA-256 : ", "SHA-256: ") + group.Hash);
            lines.AddRange(group.Files.Where(file => chosen.Contains(file.FullPath)).Select(file => L.T("RETIRER   ", "REMOVE   ") + file.FullPath));
            lines.AddRange(kept.Select(file => L.T("CONSERVER   ", "KEEP   ") + file.FullPath));
            lines.Add("");
        }
        var preview = new TextBox { IsReadOnly = true, Text = string.Join(Environment.NewLine, lines), TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RegisterName("RemovalPreview", preview); panel.Children.Add(preview);
        confirm.IsEnabled = valid && !permanent;
        consent.Checked += (_, _) => confirm.IsEnabled = valid;
        consent.Unchecked += (_, _) => confirm.IsEnabled = valid && !permanent;
        cancel.Click += (_, _) => Close();
        confirm.Click += (_, _) => { if (confirm.IsEnabled) DialogResult = true; };
        Content = panel;
    }
}
