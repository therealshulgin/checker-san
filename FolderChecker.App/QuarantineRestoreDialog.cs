using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class QuarantineRestoreDialog : Window
{
    public QuarantineSession? Session { get; private set; }
    private sealed record Choice(QuarantineSession Session, string Label);

    public QuarantineRestoreDialog(IReadOnlyList<QuarantineSession> sessions)
    {
        Title = "Checker San"; Width = 640; Height = Math.Min(440, SystemParameters.WorkArea.Height - 40);
        MinWidth = 480; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        NameScope.SetNameScope(this, new NameScope());
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = L.T("Restaurer des fichiers mis à l’écart", "Restore files moved aside"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = L.T("Chaque fichier retourne à son chemin d’origine si son contenu n’a pas changé et si rien n’occupe ce chemin. Aucun fichier existant n’est remplacé.", "Each file goes back to its original path if its contents are unchanged and nothing occupies that path. No existing file is replaced."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#B8B8B8"), Margin = new Thickness(0, 10, 0, 12) });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = L.T("Annuler", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 9, 0) };
        var restore = new Button { Content = L.T("Restaurer", "Restore"), Style = (Style)Application.Current.FindResource("PrimaryButton") };
        RegisterName("RestoreSessionButton", restore);
        footer.Children.Add(cancel); footer.Children.Add(restore); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var list = new ListBox
        {
            ItemsSource = sessions.Select(session => new Choice(session, L.F(
                $"Mis à l’écart le {session.CreatedAt.ToLocalTime():g} · {session.Waiting:N0} fichier(s) à restaurer",
                $"Moved aside on {session.CreatedAt.ToLocalTime():g} · {session.Waiting:N0} file(s) to restore"))).ToList(),
            DisplayMemberPath = nameof(Choice.Label), SelectedIndex = 0,
            Background = RowView.Brush("#101010"), Foreground = Foreground, BorderBrush = RowView.Brush("#333333"), Padding = new Thickness(8)
        };
        RegisterName("SessionList", list);
        panel.Children.Add(list); Content = panel;
        cancel.Click += (_, _) => Close();
        restore.Click += (_, _) =>
        {
            Session = (list.SelectedItem as Choice)?.Session;
            if (Session is not null) DialogResult = true;
        };
    }
}
