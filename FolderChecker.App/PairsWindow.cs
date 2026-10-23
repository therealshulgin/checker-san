using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderChecker.Core;

namespace FolderChecker.App;

/// <summary>Saved folder pairs: open one in the main window, or let Windows check it every day or every week.</summary>
public sealed class PairsWindow : Window
{
    private sealed record PairItem(SavedPair Pair, string Text);

    private readonly string _store, _executable;
    private readonly ScheduledTasks.Runner? _scheduler;
    private readonly TextBox _name = new() { Height = 38 };
    private readonly ListBox _list = new() { Background = RowView.Brush("#101010"), BorderBrush = RowView.Brush("#333333"), Padding = new Thickness(6), DisplayMemberPath = nameof(PairItem.Text) };
    private readonly ComboBox _frequency = new() { Width = 170, Height = 38, Margin = new Thickness(0, 0, 8, 8) };
    private readonly ComboBox _day = new() { Width = 140, Height = 38, Margin = new Thickness(0, 0, 8, 8) };
    private readonly TextBox _time = new() { Width = 70, Height = 38, Text = "12:00", Margin = new Thickness(0, 0, 8, 8), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = RowView.Brush("#C8C8C8") };
    private readonly Button _save, _open, _delete, _report, _schedule, _unschedule;

    public SavedPair? PairToOpen { get; private set; }

    public PairsWindow(string folder1, string folder2, bool findMovedFiles, string store, string executable, ScheduledTasks.Runner? scheduler = null)
    {
        _store = Path.GetFullPath(store); _executable = executable; _scheduler = scheduler;
        Title = "Checker San"; Width = 900; Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RowView.Brush("#181818"); Foreground = RowView.Brush("#F1F1F1"); FontSize = 13;
        SourceInitialized += (_, _) => MainWindow.ApplyDarkTitleBar(this);
        NameScope.SetNameScope(this, new NameScope());
        _list.Foreground = Foreground;
        var itemStyle = new Style(typeof(ListBoxItem), Application.Current.TryFindResource(typeof(ListBoxItem)) as Style);
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
        itemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 4)));
        _list.ItemContainerStyle = itemStyle;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = L.T("Paires de dossiers", "Folder pairs"), FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = L.T("Enregistrez deux dossiers que vous vérifiez souvent, par exemple un dossier et sa sauvegarde. Windows peut ensuite les comparer chaque jour ou chaque semaine, sans fenêtre : Checker San ne s’affiche que si un écart est prouvé ou si la vérification est incomplète. Rien n’est jamais copié ni supprimé.", "Save two folders you check often, such as a folder and its backup. Windows can then compare them every day or every week without a window: Checker San only shows up when a difference is proven or the check is incomplete. Nothing is ever copied or deleted."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#B8B8B8"), Margin = new Thickness(0, 10, 0, 14) });
        var hasFolders = folder1.Length > 0 && folder2.Length > 0;
        top.Children.Add(new TextBlock { Text = hasFolders ? L.F($"Dossiers actuels : {folder1}  ⇄  {folder2}", $"Current folders: {folder1}  ⇄  {folder2}") : L.T("Choisissez les dossiers 1 et 2 dans la fenêtre principale pour enregistrer une paire.", "Choose folders 1 and 2 in the main window to save a pair."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var saveRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _save = new Button { Content = L.T("Enregistrer la paire", "Save the pair"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = hasFolders };
        DockPanel.SetDock(_save, Dock.Right); saveRow.Children.Add(_save); saveRow.Children.Add(_name);
        _name.Text = hasFolders ? $"{Leaf(folder1)} ⇄ {Leaf(folder2)}" : "";
        _name.IsEnabled = hasFolders;
        _name.ToolTip = L.T("Nom de la paire", "Pair name");
        top.Children.Add(saveRow);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var actions = new WrapPanel();
        _open = new Button { Content = L.T("Ouvrir dans Checker San", "Open in Checker San"), Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 8) };
        _report = new Button { Content = L.T("Dernier rapport", "Latest report"), Margin = new Thickness(0, 0, 8, 8) };
        _delete = new Button { Content = L.T("Supprimer la paire", "Delete the pair"), Margin = new Thickness(0, 0, 8, 8) };
        foreach (var button in new[] { _open, _report, _delete }) actions.Children.Add(button);
        bottom.Children.Add(actions);
        bottom.Children.Add(new TextBlock { Text = L.T("Vérification automatique", "Automatic check"), FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 8, 0, 8) });
        var schedule = new WrapPanel();
        _frequency.Items.Add(new ComboBoxItem { Content = L.T("Chaque semaine", "Every week"), Tag = ScheduleFrequency.Weekly });
        _frequency.Items.Add(new ComboBoxItem { Content = L.T("Tous les jours", "Every day"), Tag = ScheduleFrequency.Daily });
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            _day.Items.Add(new ComboBoxItem { Content = L.Culture.DateTimeFormat.GetDayName(day), Tag = day });
        _schedule = new Button { Content = L.T("Planifier", "Schedule"), Margin = new Thickness(0, 0, 8, 8) };
        _unschedule = new Button { Content = L.T("Ne plus planifier", "Stop scheduling"), Margin = new Thickness(0, 0, 8, 8) };
        schedule.Children.Add(_frequency); schedule.Children.Add(_day);
        schedule.Children.Add(new TextBlock { Text = L.T("à", "at"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        foreach (var element in new UIElement[] { _time, _schedule, _unschedule }) schedule.Children.Add(element);
        bottom.Children.Add(schedule);
        bottom.Children.Add(new TextBlock { Text = L.T("La tâche s’exécute avec votre compte quand vous êtes connecté, et rattrape une heure manquée au prochain démarrage. Les rapports sont gardés dans %LOCALAPPDATA%\\CheckerSan\\Reports (les 20 derniers par paire). Si vous déplacez Checker San, planifiez de nouveau.", "The task runs under your account while you are signed in, and catches up a missed time at the next start. Reports are kept in %LOCALAPPDATA%\\CheckerSan\\Reports (the latest 20 per pair). If you move Checker San, schedule again."), TextWrapping = TextWrapping.Wrap, Foreground = RowView.Brush("#ACACAC"), FontSize = 12 });
        bottom.Children.Add(_status);
        var close = new Button { Content = L.T("Fermer", "Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        panel.Children.Add(_list);
        Content = panel;
        foreach (var (name, element) in new (string, FrameworkElement)[] { ("PairNameBox", _name), ("SavePairButton", _save), ("PairList", _list), ("OpenPairButton", _open),
            ("PairReportButton", _report), ("DeletePairButton", _delete), ("FrequencyBox", _frequency), ("DayBox", _day), ("TimeBox", _time),
            ("SchedulePairButton", _schedule), ("UnschedulePairButton", _unschedule), ("PairStatus", _status) })
            RegisterName(name, element);

        _save.Click += (_, _) => Act(() =>
        {
            var existing = PairStore.Load(_store).FirstOrDefault(pair => string.Equals(pair.Name, _name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null && MessageBox.Show(this, L.F($"La paire « {existing.Name} » existe déjà. Remplacer ses dossiers par les dossiers actuels ?", $"The pair “{existing.Name}” already exists. Replace its folders with the current ones?"), "Checker San", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var saved = PairStore.Save((existing ?? new SavedPair()) with { Name = _name.Text, Folder1 = folder1, Folder2 = folder2, FindMovedFiles = findMovedFiles }, _store);
            Refresh(saved.Id);
            _status.Text = L.F($"Paire « {saved.Name} » enregistrée.", $"Pair “{saved.Name}” saved.");
        });
        _open.Click += (_, _) => { if (Selected is { } pair) { PairToOpen = pair; DialogResult = true; } };
        _report.Click += (_, _) =>
        {
            if (Selected?.LastRun?.ReportPath is { } report && File.Exists(report))
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + report + "\"", UseShellExecute = true });
            else _status.Text = L.T("Aucun rapport pour cette paire : elle n’a pas encore été vérifiée automatiquement.", "No report for this pair: it has not been checked automatically yet.");
        };
        _delete.Click += (_, _) => Act(() =>
        {
            if (Selected is not { } pair || MessageBox.Show(this, L.F($"Supprimer la paire « {pair.Name} » ? Ses dossiers et ses rapports ne sont pas touchés.", $"Delete the pair “{pair.Name}”? Its folders and reports are left untouched."), "Checker San", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            // Whatever this window last read: another instance may have scheduled the pair meanwhile, and
            // Unregister first asks Windows whether the task exists.
            ScheduledTasks.Unregister(pair, _scheduler);
            PairStore.Remove(pair.Id, _store);
            Refresh(null);
            _status.Text = L.F($"Paire « {pair.Name} » supprimée.", $"Pair “{pair.Name}” deleted.");
        });
        _schedule.Click += (_, _) => Act(() =>
        {
            if (Selected is not { } pair) return;
            if (!TimeOnly.TryParseExact(_time.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            { _status.Text = L.T("Indiquez une heure au format 21:30.", "Enter a time such as 21:30."); return; }
            if (!File.Exists(_executable)) { _status.Text = L.T("Checker San est introuvable à son emplacement actuel : la tâche ne pourrait pas le lancer.", "Checker San cannot be found where it runs: the task could not start it."); return; }
            var scheduled = pair with { Schedule = new PairSchedule((ScheduleFrequency)((ComboBoxItem)_frequency.SelectedItem).Tag, (DayOfWeek)((ComboBoxItem)_day.SelectedItem).Tag, time) };
            var store = string.Equals(_store, Path.GetFullPath(PairStore.DefaultPath), StringComparison.OrdinalIgnoreCase) ? null : _store;
            ScheduledTasks.Register(scheduled, _executable, ScheduledTasks.Arguments(scheduled, store), _scheduler);
            // Only the schedule changes: a result recorded by a check since this window opened is kept.
            try
            {
                if (PairStore.Modify(pair.Id, current => current with { Schedule = scheduled.Schedule }, _store) is null)
                    throw new IOException(L.T("La paire a été supprimée entre-temps.", "The pair was deleted meanwhile."));
            }
            catch
            {
                // Never leave behind a task the pair list does not know about.
                ScheduledTasks.Unregister(scheduled, _scheduler);
                throw;
            }
            Refresh(pair.Id);
            _status.Text = L.F($"« {pair.Name} » : {scheduled.ScheduleText.ToLower(L.Culture)}. Tâche Windows : {ScheduledTasks.TaskName(scheduled)}", $"“{pair.Name}”: {scheduled.ScheduleText.ToLower(L.Culture)}. Windows task: {ScheduledTasks.TaskName(scheduled)}");
        });
        _unschedule.Click += (_, _) => Act(() =>
        {
            if (Selected is not { } pair) return;
            ScheduledTasks.Unregister(pair, _scheduler);
            PairStore.Modify(pair.Id, current => current with { Schedule = null }, _store);
            Refresh(pair.Id);
            _status.Text = L.F($"« {pair.Name} » n’est plus vérifiée automatiquement.", $"“{pair.Name}” is no longer checked automatically.");
        });
        _frequency.SelectionChanged += (_, _) => _day.IsEnabled = (_frequency.SelectedItem as ComboBoxItem)?.Tag is ScheduleFrequency.Weekly;
        _list.SelectionChanged += (_, _) => ShowSelection();
        close.Click += (_, _) => Close();
        Act(() => Refresh(null));
    }

    private SavedPair? Selected => (_list.SelectedItem as PairItem)?.Pair;

    private static string Leaf(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } leaf ? leaf : path;

    private void Refresh(string? select)
    {
        var items = PairStore.Load(_store).Select(pair => new PairItem(pair,
            $"{pair.Name}\n{pair.Folder1}  ⇄  {pair.Folder2}\n{pair.ScheduleText} · {L.T("dernière vérification", "last check")} : {pair.LastRunText}")).ToList();
        _list.ItemsSource = items;
        _list.SelectedItem = items.FirstOrDefault(item => item.Pair.Id == select) ?? items.FirstOrDefault();
        ShowSelection();
    }

    private void ShowSelection()
    {
        var pair = Selected;
        foreach (var button in new[] { _open, _report, _delete, _schedule }) button.IsEnabled = pair is not null;
        // Enabled for any pair: a task may exist that this list does not show, and Windows is asked first.
        _unschedule.IsEnabled = pair is not null;
        var schedule = pair?.Schedule ?? new PairSchedule(ScheduleFrequency.Weekly, DayOfWeek.Sunday, new TimeOnly(12, 0));
        _frequency.SelectedItem = _frequency.Items.Cast<ComboBoxItem>().First(item => (ScheduleFrequency)item.Tag == schedule.Frequency);
        _day.SelectedItem = _day.Items.Cast<ComboBoxItem>().First(item => (DayOfWeek)item.Tag == schedule.Day);
        _time.Text = schedule.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a store or scheduler change and shows its error instead of a crash: nothing is half done silently.</summary>
    private void Act(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or System.ComponentModel.Win32Exception)
        { _status.Text = L.T("Opération impossible : ", "Operation failed: ") + exception.Message; }
    }
}
