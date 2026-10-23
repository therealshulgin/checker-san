using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FolderChecker.Cli;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static Task SavedPairsAreStoredSafely()
    {
        using var w = new TestWorkspace();
        var store = Path.Combine(w.Root, "store", "pairs.json");
        Equal(0, PairStore.Load(store).Count, "a missing store has no pairs");
        var saved = PairStore.Save(new SavedPair { Name = "  Photos  ", Folder1 = "\"" + w.Source + "\"", Folder2 = w.Target }, store);
        Check(PairStore.IsId(saved.Id), "a new pair gets an id");
        Equal("Photos", saved.Name, "the name is trimmed");
        Equal(w.Source, saved.Folder1, "pasted quotes are removed");
        Throws<ArgumentException>(() => PairStore.Save(new SavedPair { Name = "PHOTOS", Folder1 = w.Source, Folder2 = w.Target }, store), "names are unique, ignoring case");
        Throws<ArgumentException>(() => PairStore.Save(new SavedPair { Name = "No folder", Folder1 = w.Source }, store), "both folders are required");
        PairStore.Save(saved with { Folder2 = w.Root, FindMovedFiles = false }, store);
        var reloaded = PairStore.Load(store).Single();
        Check(reloaded.Folder2 == w.Root && !reloaded.FindMovedFiles, "saving with the same id replaces the pair");
        Equal(saved.Id, PairStore.Find("photos", store)?.Id, "a pair is found by name, ignoring case");
        Equal("Photos", PairStore.Find(saved.Id, store)?.Name, "a pair is found by id");
        PairStore.Save(reloaded with { Schedule = new PairSchedule(ScheduleFrequency.Weekly, DayOfWeek.Wednesday, new TimeOnly(21, 30)) }, store);
        PairStore.RecordRun(saved.Id, new PairRun(DateTimeOffset.Now, ExitCodes.Differences, 3, 0, 0, null), store);
        var recorded = PairStore.Load(store).Single();
        Check(recorded.LastRun is { ExitCode: ExitCodes.Differences, Differences: 3 } && recorded.Schedule is not null, "a run is recorded without losing the schedule");
        L.SetLanguage("en");
        try
        {
            Equal("Every Wednesday at 21:30", recorded.ScheduleText, "the schedule reads in English");
            Check(recorded.LastRunText.EndsWith("— 3 difference(s)", StringComparison.Ordinal), recorded.LastRunText);
        }
        finally { L.SetLanguage("fr"); }
        Equal("Chaque mercredi à 21:30", recorded.ScheduleText, "the schedule reads in French");
        Equal(0, Directory.GetFiles(Path.GetDirectoryName(store)!, "*.tmp").Length, "no temporary file is left");
        PairStore.Remove(saved.Id, store);
        Equal(0, PairStore.Load(store).Count, "a removed pair is gone");

        File.WriteAllText(store, "{ \"Schema\": 1, \"Pairs\": [ { \"Id\": \"../../x\", \"Name\": \"bad\" } ] }");
        var damaged = File.ReadAllText(store);
        Throws<InvalidDataException>(() => PairStore.Load(store), "a damaged store is reported");
        Throws<InvalidDataException>(() => PairStore.Save(new SavedPair { Name = "New", Folder1 = w.Source, Folder2 = w.Target }, store), "a damaged store is never overwritten");
        Equal(damaged, File.ReadAllText(store), "the damaged store is left as it was");
        return Task.CompletedTask;
    }

    private static Task ScheduledTaskIsReadOnlyAndValid()
    {
        using var w = new TestWorkspace();
        var store = Path.Combine(w.Root, "pairs.json");
        var pair = new SavedPair
        {
            Id = "0a1b2c3d", Name = "Photos & <Backup> / \"2026\": *?", Folder1 = w.Source, Folder2 = w.Target,
            Schedule = new PairSchedule(ScheduleFrequency.Weekly, DayOfWeek.Wednesday, new TimeOnly(21, 30))
        };
        var name = ScheduledTasks.TaskName(pair);
        Equal("Checker San - Photos _ _Backup_ _ _2026__ __ (0a1b2c3d)", name, "the task name keeps the pair name readable and its id");
        Check(name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0, "the task name has no reserved character");
        var executable = Environment.ProcessPath!;
        var arguments = ScheduledTasks.Arguments(pair, store);
        Equal($"--scheduled 0a1b2c3d --pairs \"{store}\"", arguments, "the task passes the pair id and its store");
        Equal("--scheduled 0a1b2c3d", ScheduledTasks.Arguments(pair), "the default store is implied");
        var xml = ScheduledTasks.Xml(pair, executable, arguments, new DateTime(2026, 10, 23, 8, 0, 0));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = XDocument.Parse(xml).Root!;
        string Value(params string[] path) => path.Aggregate(task, (element, child) => element.Element(ns + child)!).Value;
        Equal("2026-10-23T21:30:00", Value("Triggers", "CalendarTrigger", "StartBoundary"), "the first run is today at the chosen time");
        Check(task.Element(ns + "Triggers")!.Element(ns + "CalendarTrigger")!.Element(ns + "ScheduleByWeek")!.Element(ns + "DaysOfWeek")!.Element(ns + "Wednesday") is not null, "weekly on the chosen day");
        Equal("InteractiveToken", Value("Principals", "Principal", "LogonType"), "no password is stored");
        Equal("LeastPrivilege", Value("Principals", "Principal", "RunLevel"), "the task never runs elevated");
        Equal("true", Value("Settings", "StartWhenAvailable"), "a missed time is caught up");
        Equal("IgnoreNew", Value("Settings", "MultipleInstancesPolicy"), "a check never runs twice at once");
        Equal("false", Value("Settings", "DisallowStartIfOnBatteries"), "a laptop on battery is still checked");
        Equal(executable, Value("Actions", "Exec", "Command"), "the task starts Checker San");
        Equal(arguments, Value("Actions", "Exec", "Arguments"), "with the pair arguments");
        Check(Value("RegistrationInfo", "Description").Contains(pair.Name, StringComparison.Ordinal), "the description keeps the exact name, escaped");
        var daily = XDocument.Parse(ScheduledTasks.Xml(pair with { Schedule = pair.Schedule! with { Frequency = ScheduleFrequency.Daily } }, executable, arguments)).Root!;
        Equal("1", daily.Element(ns + "Triggers")!.Element(ns + "CalendarTrigger")!.Element(ns + "ScheduleByDay")!.Element(ns + "DaysInterval")!.Value, "daily runs every day");

        var calls = new List<string[]>();
        string? registeredXml = null;
        var queryCode = 1;
        (int, string) Fake(IReadOnlyList<string> args)
        {
            calls.Add([.. args]);
            if (args[0] == "/Create") registeredXml = File.ReadAllText(args[4], Encoding.Unicode);
            return args[0] == "/Query" ? (queryCode, "") : (0, "OK");
        }
        ScheduledTasks.Register(pair, executable, arguments, Fake);
        Equal($"/Create|/TN|{name}|/XML|/F", string.Join("|", calls[0].Where((_, index) => index != 4)), "schtasks creates or replaces the task from XML");
        Check(registeredXml?.StartsWith("<?xml version=\"1.0\" encoding=\"utf-16\"", StringComparison.OrdinalIgnoreCase) == true &&
            registeredXml.Contains(executable, StringComparison.Ordinal), "the XML file is written in UTF-16 with the program to start");
        Check(!File.Exists(calls[0][4]), "the temporary XML file is removed");
        calls.Clear();
        ScheduledTasks.Unregister(pair, Fake);
        Equal("/Query", string.Join(",", calls.Select(call => call[0])), "a missing task is not deleted");
        calls.Clear(); queryCode = 0;
        ScheduledTasks.Unregister(pair, Fake);
        Equal("/Query,/Delete", string.Join(",", calls.Select(call => call[0])), "an existing task is deleted");
        var refused = Throws<IOException>(() => ScheduledTasks.Register(pair, executable, arguments, _ => (1, "ERROR: Access is denied.")), "a refusal is reported");
        Check(refused.Message.Contains("Access is denied", StringComparison.Ordinal), refused.Message);

        // Ask Windows to check the definition without registering anything (TASK_VALIDATE_ONLY).
        if (OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Schedule.Service") is { } type)
        {
            var probe = pair with { Id = "fedcba98", Name = "validation only" };
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            folder.RegisterTask(ScheduledTasks.TaskName(probe), ScheduledTasks.Xml(probe, executable, arguments), 1, null, null, 3, null);
            Check(!ScheduledTasks.Exists(probe), "validation registers nothing");
        }
        return Task.CompletedTask;
    }

    private static async Task CliRunsSavedPairs()
    {
        using var w = new TestWorkspace();
        var store = Path.Combine(w.Root, "store", "pairs.json");
        var log = Path.Combine(w.Root, "activity.log");
        async Task<(int Code, string Output)> Run(params string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CheckerSanCli.RunAsync([.. args, "--lang", "en", "--log", log, "--pairs", store], output, error);
            return (code, output + error.ToString());
        }
        w.FileAt(w.Source, "same.bin", TestWorkspace.Bytes(70_000, 1));
        w.FileAt(w.Target, "same.bin", TestWorkspace.Bytes(70_000, 1));
        var pair = PairStore.Save(new SavedPair { Name = "Backup", Folder1 = w.Source, Folder2 = w.Target }, store);
        var clean = await Run("run", "Backup");
        Equal(CheckerSanCli.Clean, clean.Code, clean.Output);
        var reports = Path.Combine(PairStore.ReportsFolder(store), pair.Id);
        var first = Directory.GetFiles(reports).Single();
        using (var json = JsonDocument.Parse(File.ReadAllText(first)))
        {
            Equal(0, json.RootElement.GetProperty("ExitCode").GetInt32(), "the report has the exit code");
            Equal(1, json.RootElement.GetProperty("Identical").GetInt32(), "identical files are counted");
            Equal(0, json.RootElement.GetProperty("Rows").GetArrayLength(), "identical files are not listed, so reports stay small");
        }
        Equal(ExitCodes.Clean, PairStore.Find("Backup", store)!.LastRun!.ExitCode, "the run is recorded with the pair");

        w.FileAt(w.Source, "new.txt", "only in folder 1");
        await Task.Delay(1100);
        var different = await Run("run", "backup");
        Equal(CheckerSanCli.Differences, different.Code, different.Output);
        Check(different.Output.Contains("1 difference(s)", StringComparison.Ordinal) && different.Output.Contains("Report: ", StringComparison.Ordinal), different.Output);
        Equal(1, PairStore.Find(pair.Id, store)!.LastRun!.Differences, "the difference count is recorded");
        var listing = await Run("pairs");
        Check(listing.Code == CheckerSanCli.Clean && listing.Output.Contains("Backup  [" + pair.Id + "]", StringComparison.Ordinal) &&
            listing.Output.Contains("Not scheduled", StringComparison.Ordinal) && listing.Output.Contains("1 difference(s)", StringComparison.Ordinal), listing.Output);
        Equal(CheckerSanCli.Failure, (await Run("run", "Unknown")).Code, "an unknown pair is an error");

        for (var i = 0; i < PairCheck.ReportsKept + 5; i++) File.WriteAllText(Path.Combine(reports, $"2020-01-01 0000{i:D2}.json"), "{}");
        await Task.Delay(1100);
        var latest = await Run("run", "Backup");
        var kept = Directory.GetFiles(reports);
        Check(kept.Length == PairCheck.ReportsKept && kept.Contains(PairStore.Find(pair.Id, store)!.LastRun!.ReportPath!), latest.Output);

        var gone = PairStore.Save(new SavedPair { Name = "Unplugged", Folder1 = w.Source, Folder2 = Path.Combine(w.Root, "unplugged") }, store);
        var unplugged = await Run("run", "Unplugged");
        Check(unplugged.Code is CheckerSanCli.Incomplete or CheckerSanCli.Failure, unplugged.Output);
        Check(PairStore.Find(gone.Id, store)!.LastRun is { ExitCode: not ExitCodes.Clean }, "a failed check is recorded too");

        var insideStore = Path.Combine(w.Source, "settings", "pairs.json");
        var inside = PairStore.Save(new SavedPair { Name = "Inside", Folder1 = w.Source, Folder2 = w.Target }, insideStore);
        var run = await PairCheck.RunAsync(inside, insideStore, log: log);
        Check(run.ReportPath is null && run.Failure is not null && !Directory.Exists(Path.Combine(w.Source, "settings", "Reports")), "a report is never written inside a compared folder");
        Check(File.ReadAllText(log).Contains("\tPair\tBackup", StringComparison.Ordinal), "every run is in the activity log");
    }

    private static async Task CliSimulationsChangeNothing()
    {
        using var w = new TestWorkspace();
        var log = Path.Combine(w.Root, "activity.log");
        async Task<(int Code, string Output)> Run(params string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CheckerSanCli.RunAsync([.. args, "--lang", "en", "--log", log], output, error);
            return (code, output + error.ToString());
        }
        w.FileAt(w.Source, "left.txt", "only in 1");
        w.FileAt(w.Target, "right.txt", "only in 2");
        var report = Path.Combine(w.Root, "copy.json");
        var both = await Run("compare", w.Source, w.Target, "--simulate-copy", "both", "--json", report);
        Equal(CheckerSanCli.Differences, both.Code, both.Output);
        Check(both.Output.Contains("SIMULATION — nothing is copied.", StringComparison.Ordinal) &&
            both.Output.Contains("WOULD COPY 1 → 2", StringComparison.Ordinal) && both.Output.Contains("left.txt", StringComparison.Ordinal) &&
            both.Output.Contains("WOULD COPY 2 → 1", StringComparison.Ordinal) && both.Output.Contains("right.txt", StringComparison.Ordinal), both.Output);
        Check(!File.Exists(Path.Combine(w.Target, "left.txt")) && !File.Exists(Path.Combine(w.Source, "right.txt")), "the simulation copies nothing");
        using (var json = JsonDocument.Parse(File.ReadAllText(report)))
            Equal(2, json.RootElement.GetProperty("CopySimulation").GetProperty("Items").GetArrayLength(), "the JSON report lists the simulated copies");
        var forward = await Run("compare", w.Source, w.Target, "--simulate-copy", "1to2");
        Check(forward.Output.Contains("WOULD COPY 1 → 2", StringComparison.Ordinal) && !forward.Output.Contains("right.txt", StringComparison.Ordinal), forward.Output);
        Equal(CheckerSanCli.Failure, (await Run("compare", w.Source, w.Target, "--simulate-copy", "sideways")).Code, "an unknown direction is refused");

        var older = w.FileAt(w.Source, "older.bin", TestWorkspace.Bytes(9_000, 4));
        var newer = w.FileAt(w.Source, "sub/newer.bin", TestWorkspace.Bytes(9_000, 4));
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var removal = Path.Combine(w.Root, "removal.json");
        var oldest = await Run("duplicates", w.Source, "--simulate-keep", "oldest", "--json", removal);
        Equal(CheckerSanCli.Differences, oldest.Code, oldest.Output);
        Check(oldest.Output.Contains("WOULD KEEP    older.bin", StringComparison.Ordinal) &&
            oldest.Output.Contains("WOULD REMOVE  " + Path.Combine("sub", "newer.bin"), StringComparison.Ordinal), oldest.Output);
        Check(File.Exists(older) && File.Exists(newer), "the simulation removes nothing");
        using (var json = JsonDocument.Parse(File.ReadAllText(removal)))
            Equal(Path.GetFullPath(newer), json.RootElement.GetProperty("RemovalSimulation").GetProperty("Remove")[0].GetString(), "the JSON report lists the copy a rule would remove");
        var inFolder = await Run("duplicates", w.Source, "--simulate-keep", "folder", "--keep-in", Path.Combine(w.Source, "sub"));
        Check(inFolder.Output.Contains("WOULD KEEP    " + Path.Combine("sub", "newer.bin"), StringComparison.Ordinal), inFolder.Output);
        Equal(CheckerSanCli.Failure, (await Run("duplicates", w.Source, "--simulate-keep", "folder")).Code, "a folder rule needs its folder");
    }
}
