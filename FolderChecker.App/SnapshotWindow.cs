using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;
using Microsoft.Win32;

namespace FolderChecker.App;

/// <summary>Creates a snapshot of a folder, or checks a folder against one, for example a backup disk plugged in later.</summary>
public sealed class SnapshotWindow : Window
{
    private readonly TextBox _folder = new() { Height = 38 };
    private readonly TextBlock _status = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 6) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#B8B8B8") };
    private readonly ListBox _results = new() { Background = RowView.Brush("#101010"), BorderBrush = RowView.Brush("#333333"), Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _create, _verify, _stop;
    private CancellationTokenSource? _cancellation;

    public SnapshotWindow(string initialFolder)
    {
        Title = "Checker San"; Width = 860; Height = Math.Min(680, SystemParameters.WorkArea.Height - 40);
        MinWidth = 620; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        NameScope.SetNameScope(this, new NameScope());
        _results.Foreground = Foreground;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = L.T("Instantanés", "Snapshots"), FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = L.T("Un instantané enregistre les chemins, les tailles et le SHA-256 complet de chaque fichier d’un dossier. Vous pourrez ensuite vérifier ce dossier, ou une sauvegarde débranchée, sans avoir l’original sous la main. Rien n’est modifié.", "A snapshot records the paths, sizes and complete SHA-256 of every file in a folder. You can then check that folder, or a disconnected backup, without the original at hand. Nothing is changed."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#B8B8B8"), Margin = new Thickness(0, 10, 0, 14) });
        var folderRow = new DockPanel();
        var browse = new Button { Content = L.T("Parcourir…", "Browse…"), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(_folder);
        _folder.Text = initialFolder;
        _status.Text = L.T("Choisissez un dossier, puis une action.", "Choose a folder, then an action.");
        RegisterName("SnapshotFolderBox", _folder);
        top.Children.Add(folderRow);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _create = new Button { Content = L.T("Créer un instantané…", "Create a snapshot…"), Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 8) };
        _verify = new Button { Content = L.T("Vérifier avec un instantané…", "Check against a snapshot…"), Margin = new Thickness(0, 0, 8, 8) };
        _stop = new Button { Content = L.T("Arrêter", "Stop"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 8) };
        var journal = new Button { Content = L.T("Journal d’activité", "Activity log"), Margin = new Thickness(0, 0, 0, 8) };
        foreach (var button in new[] { _create, _verify, _stop, journal }) actions.Children.Add(button);
        top.Children.Add(actions);
        top.Children.Add(_status); top.Children.Add(_detail);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var close = new Button { Content = L.T("Fermer", "Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(_results);
        RegisterName("SnapshotResults", _results);
        Content = panel;
        browse.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = L.T("Dossier de l’instantané", "Snapshot folder"), Multiselect = false };
            if (!string.IsNullOrWhiteSpace(_folder.Text)) picker.InitialDirectory = _folder.Text.Trim().Trim('"');
            if (picker.ShowDialog(this) == true) _folder.Text = picker.FolderName;
        };
        _create.Click += async (_, _) => await CreateAsync();
        _verify.Click += async (_, _) => await VerifyAsync();
        _stop.Click += (_, _) => { _cancellation?.Cancel(); _stop.IsEnabled = false; };
        journal.Click += (_, _) =>
        {
            if (File.Exists(ActivityLog.DefaultPath)) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + ActivityLog.DefaultPath + "\"", UseShellExecute = true });
            else MessageBox.Show(this, L.T("Aucune activité n’a encore été enregistrée.", "No activity has been recorded yet."), "Checker San");
        };
        close.Click += (_, _) => Close();
        Closing += (_, e) => { if (_cancellation is not null) { e.Cancel = true; _cancellation.Cancel(); } };
    }

    private string Folder => _folder.Text.Trim().Trim('"');

    private async Task CreateAsync()
    {
        var save = new SaveFileDialog
        {
            Title = L.T("Enregistrer l’instantané hors du dossier", "Save the snapshot outside the folder"),
            FileName = $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder))} snapshot {DateTime.Now:yyyy-MM-dd}.json",
            Filter = L.T("Instantané Checker San (*.json)|*.json", "Checker San snapshot (*.json)|*.json"), AddExtension = true, OverwritePrompt = false
        };
        if (string.IsNullOrWhiteSpace(Folder) || save.ShowDialog(this) != true) return;
        // Refuse the destination now rather than after reading the whole folder.
        try { SnapshotEngine.ValidateDestination(Folder, save.FileName); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            _status.Text = L.T("Opération impossible", "Operation failed");
            _detail.Text = exception.Message;
            return;
        }
        await RunAsync(async token =>
        {
            var snapshot = await new SnapshotEngine().CreateAsync(Folder, null, Progress(), token);
            SnapshotEngine.Save(snapshot, save.FileName);
            _status.Text = snapshot.Complete ? L.T("Instantané complet enregistré", "Complete snapshot saved") : L.T("Instantané enregistré, mais incomplet", "Snapshot saved, but incomplete");
            _detail.Text = L.F($"{snapshot.Files.Count:N0} fichier(s) · {snapshot.Directories.Count:N0} dossier(s) · {snapshot.Links.Count:N0} lien(s) non suivi(s) · {snapshot.Unverified.Count:N0} non vérifié(s)\n{save.FileName}",
                $"{snapshot.Files.Count:N0} file(s) · {snapshot.Directories.Count:N0} folder(s) · {snapshot.Links.Count:N0} link(s) skipped · {snapshot.Unverified.Count:N0} not verified\n{save.FileName}");
            _results.ItemsSource = snapshot.Unverified.Select(path => L.T("Non vérifié : ", "Not verified: ") + path).Concat(snapshot.Errors).ToList();
            ActivityLog.Write(L.T("Instantané", "Snapshot"), $"{snapshot.Root} → {save.FileName} · {snapshot.Files.Count} file(s) · complete={snapshot.Complete}");
        });
    }

    private async Task VerifyAsync()
    {
        var open = new OpenFileDialog { Title = L.T("Instantané à comparer", "Snapshot to compare"), Filter = L.T("Instantané Checker San (*.json)|*.json", "Checker San snapshot (*.json)|*.json") };
        if (string.IsNullOrWhiteSpace(Folder) || open.ShowDialog(this) != true) return;
        await RunAsync(async token =>
        {
            var expected = SnapshotEngine.Load(open.FileName);
            var actual = await new SnapshotEngine().CreateAsync(Folder, null, Progress(), token);
            var result = SnapshotEngine.Compare(expected, actual);
            // As on the command line, an error leaves the result unproven, even next to differences that were found.
            var errors = expected.Errors.Select(error => L.T("Instantané : ", "Snapshot: ") + error).Concat(actual.Errors).ToArray();
            _status.Text = result.Identical ? L.T("Le dossier correspond exactement à l’instantané", "The folder matches the snapshot exactly")
                : errors.Length > 0 ? L.F($"Vérification incomplète — {errors.Length:N0} erreur(s), {result.Differences.Count:N0} différence(s) relevée(s)", $"Incomplete check — {errors.Length:N0} error(s), {result.Differences.Count:N0} difference(s) found")
                : result.Differences.Count > 0 ? L.F($"{result.Differences.Count:N0} différence(s) avec l’instantané", $"{result.Differences.Count:N0} difference(s) from the snapshot")
                : L.T("Vérification incomplète — des fichiers n’ont pas pu être lus", "Incomplete check — some files could not be read");
            _detail.Text = L.F($"Instantané du {expected.CreatedAt.ToLocalTime():g} · {expected.Files.Count:N0} fichier(s) · dossier vérifié : {actual.Root}", $"Snapshot of {expected.CreatedAt.ToLocalTime():g} · {expected.Files.Count:N0} file(s) · folder checked: {actual.Root}");
            _results.ItemsSource = errors.Select(error => "! " + error).Concat(result.Differences.Select(difference => difference.Change switch
            {
                SnapshotChange.Missing => L.T("Absent   ", "Missing   "),
                SnapshotChange.Changed => L.T("Modifié   ", "Changed   "),
                SnapshotChange.Added => L.T("Ajouté   ", "Added   "),
                SnapshotChange.MissingFolder => L.T("Dossier absent   ", "Folder missing   "),
                _ => L.T("Dossier ajouté   ", "Folder added   ")
            } + difference.Path)).Concat(actual.Unverified.Select(path => L.T("Non vérifié   ", "Not verified   ") + path)).ToList();
            ActivityLog.Write(L.T("Vérification d’instantané", "Snapshot check"), $"{actual.Root} vs {open.FileName} · {result.Differences.Count} difference(s) · certain={result.Certain}");
        });
    }

    private Progress<SnapshotProgress> Progress() => new(p =>
        _detail.Text = L.F($"{p.FilesHashed:N0} / {p.FilesTotal:N0} fichier(s) · {L.Size(p.BytesRead)} lus · {p.CurrentPath}", $"{p.FilesHashed:N0} / {p.FilesTotal:N0} file(s) · {L.Size(p.BytesRead)} read · {p.CurrentPath}"));

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _cancellation = new CancellationTokenSource();
        _create.IsEnabled = _verify.IsEnabled = _folder.IsEnabled = false; _stop.IsEnabled = true;
        _status.Text = L.T("Lecture complète des fichiers…", "Reading every file…"); _results.ItemsSource = null;
        try { await work(_cancellation.Token); }
        catch (OperationCanceledException) { _status.Text = L.T("Arrêté — rien n’a été enregistré ni modifié", "Stopped — nothing was saved or changed"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        { _status.Text = L.T("Opération impossible", "Operation failed"); _detail.Text = exception.Message; }
        finally
        {
            _cancellation.Dispose(); _cancellation = null;
            _create.IsEnabled = _verify.IsEnabled = _folder.IsEnabled = true; _stop.IsEnabled = false;
        }
    }
}
