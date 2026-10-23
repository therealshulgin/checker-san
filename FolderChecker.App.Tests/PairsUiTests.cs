using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static void Click(Window window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void PairsWindowSavesAndSchedules()
    {
        using var workspace = new TestWorkspace();
        var store = Path.Combine(workspace.Root, "pairs.json");
        var calls = new List<string>();
        var queryCode = 1;
        (int, string) Scheduler(IReadOnlyList<string> args)
        {
            calls.Add(args[0]);
            return args[0] == "/Query" ? (queryCode, "") : (0, "");
        }
        var main = new MainWindow();
        var window = new PairsWindow(workspace.Source, workspace.Target, false, store, Environment.ProcessPath!, Scheduler);
        try
        {
            Equal("Paires…", Control<Button>(main, "PairsButton").Content, "the header offers saved pairs");
            Equal("source ⇄ target", ((TextBox)window.FindName("PairNameBox")).Text, "the name is proposed from both folders");
            Check(!((Button)window.FindName("OpenPairButton")).IsEnabled, "nothing to open before a pair is saved");
            Click(window, "SavePairButton");
            var pair = PairStore.Load(store).Single();
            Check(pair.Folder1 == workspace.Source && pair.Folder2 == workspace.Target && !pair.FindMovedFiles, "the current folders and option are saved");
            Equal(1, ((ListBox)window.FindName("PairList")).Items.Count, "the saved pair is listed");
            Check(((Button)window.FindName("UnschedulePairButton")).IsEnabled, "a task this list does not know about can still be removed");

            ((TextBox)window.FindName("TimeBox")).Text = "25:00";
            Click(window, "SchedulePairButton");
            Check(((TextBlock)window.FindName("PairStatus")).Text.Contains("21:30"), "an invalid time is refused");
            Equal(0, calls.Count, "nothing is scheduled with an invalid time");
            ((ComboBox)window.FindName("FrequencyBox")).SelectedIndex = 1;
            Check(!((ComboBox)window.FindName("DayBox")).IsEnabled, "a daily check has no day");
            ((TextBox)window.FindName("TimeBox")).Text = "7:15";
            // A scheduled check records its result while the window is open: scheduling must not erase it.
            PairStore.RecordRun(pair.Id, new PairRun(DateTimeOffset.Now, ExitCodes.Differences, 42, 0, 0, null), store);
            Click(window, "SchedulePairButton");
            Equal("/Create", string.Join(",", calls), "the task is created");
            Equal(42, PairStore.Load(store).Single().LastRun?.Differences, "the result recorded meanwhile is kept");
            Equal(new PairSchedule(ScheduleFrequency.Daily, pair.Schedule?.Day ?? DayOfWeek.Sunday, new TimeOnly(7, 15)), PairStore.Load(store).Single().Schedule, "the schedule is saved with the pair");
            Check(((ListBox)window.FindName("PairList")).Items.Cast<object>().Single().ToString()!.Contains("Tous les jours à 07:15"), "the list shows the schedule");
            Check(((Button)window.FindName("UnschedulePairButton")).IsEnabled, "a scheduled pair can be unscheduled");

            calls.Clear(); queryCode = 0;
            Click(window, "UnschedulePairButton");
            Equal("/Query,/Delete", string.Join(",", calls), "the task is deleted");
            Check(PairStore.Load(store).Single().Schedule is null, "the pair is no longer scheduled");

            main.LoadPair(pair);
            Check(Control<TextBox>(main, "SourceBox").Text == workspace.Source && Control<TextBox>(main, "TargetBox").Text == workspace.Target &&
                Control<CheckBox>(main, "MovedCheck").IsChecked == false, "opening a pair fills both folders and its option");
        }
        finally { window.Close(); main.Close(); }
    }

    private static void ScheduledResultExplainsTheCheck()
    {
        var pair = new SavedPair { Id = "0a1b2c3d", Name = "Photos", Folder1 = @"C:\Photos", Folder2 = @"G:\Backup\Photos" };
        IReadOnlyList<string>? launched = null;
        var found = new ScheduledResultWindow(pair, new PairRun(DateTimeOffset.Now, ExitCodes.Differences, 3, 0, 0, @"C:\reports\r.json"), @"C:\store\pairs.json", args => launched = args);
        var failed = new ScheduledResultWindow(null, new PairRun(DateTimeOffset.Now, ExitCodes.Failure, 0, 0, 1, null, "la paire 0a1b2c3d n’existe plus."));
        try
        {
            Check(((TextBlock)found.FindName("ScheduledTitle")).Text.Contains("écarts"), "differences are announced");
            var detail = ((TextBlock)found.FindName("ScheduledDetail")).Text;
            Check(detail.Contains("Photos") && detail.Contains(@"G:\Backup\Photos") && detail.Contains("3 écart(s)") && detail.Contains(@"C:\reports\r.json"), detail);
            Check(((Button)found.FindName("ScheduledReportButton")).IsEnabled && ((Button)found.FindName("ScheduledOpenButton")).IsEnabled, "the report and the pair can be opened");
            Check(!found.ShowActivated, "the window never takes the focus");
            Click(found, "ScheduledOpenButton");
            Equal("--pair 0a1b2c3d --pairs C:\\store\\pairs.json", string.Join(" ", launched ?? []), "opening starts a separate Checker San on the pair, never inside the task");
            Check(((TextBlock)failed.FindName("ScheduledTitle")).Text.Contains("impossible"), "a failure is announced");
            Check(((TextBlock)failed.FindName("ScheduledDetail")).Text.Contains("n’existe plus"), "with its reason");
            Check(!((Button)failed.FindName("ScheduledOpenButton")).IsEnabled && !((Button)failed.FindName("ScheduledReportButton")).IsEnabled, "nothing to open without a pair or report");
        }
        finally { found.Close(); failed.Close(); }
    }

    // COP-03: after a copy from folder 2 to folder 1, the boxes still show folder 1 and folder 2 where they were.
    private static void CopyKeepsFolderOneAndTwo()
    {
        var main = new MainWindow();
        try
        {
            Control<TextBox>(main, "SourceBox").Text = @"C:\Photos";
            Control<TextBox>(main, "TargetBox").Text = @"G:\Backup\Photos\";
            var shows = typeof(MainWindow).GetMethod("ShowsFolders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Check((bool)shows.Invoke(main, [@"C:\Photos", @"G:\Backup\Photos"])!, "the boxes already show the two folders of the plan");
            Check(!(bool)shows.Invoke(main, [@"G:\Backup\Photos", @"C:\Photos"])!, "a plan from 2 to 1 is recognized as the same pair the other way round");
        }
        finally { main.Close(); }
    }

    private static void CopyPreviewExportsWhatWouldBeCopied()
    {
        using var workspace = new TestWorkspace();
        var dialog = new CopyDialog(CopyPreviewFixture(workspace), null);
        try
        {
            AwaitPreview(dialog);
            Check(((Button)dialog.FindName("ExportPlanButton")).IsEnabled, "a nonempty preview can be exported");
            var path = Path.Combine(workspace.Root, "preview.csv");
            CopyPlanWriter.Write(path, dialog.Plan!);
            var rows = ReadCsv(path);
            Equal(dialog.Plan!.Items.Count + 1, rows.Count, "one row per planned item");
            var left = rows.Single(row => row[1] == "left.txt");
            Check(left[4] == Path.Combine(workspace.Source, "left.txt") && left[5] == Path.Combine(workspace.Target, "left.txt"), "each row has its source and destination");
            var right = rows.Single(row => row[1] == "right.txt");
            Check(right[4] == Path.Combine(workspace.Target, "right.txt") && right[5] == Path.Combine(workspace.Source, "right.txt"), "the reverse direction is exported too");
            Throws<IOException>(() => CopyPlanWriter.Write(path, dialog.Plan!));
            Throws<IOException>(() => CopyPlanWriter.Write(Path.Combine(workspace.Source, "inside.csv"), dialog.Plan!));
            Check(!File.Exists(Path.Combine(workspace.Target, "left.txt")) && !File.Exists(Path.Combine(workspace.Source, "right.txt")), "exporting copies nothing");
        }
        finally { dialog.Close(); }
    }
}
