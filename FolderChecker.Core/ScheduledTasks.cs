using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FolderChecker.Core;

/// <summary>
/// Windows Task Scheduler entries that check a saved pair. A task runs as the current user while they are signed
/// in, so no password is stored. It starts late if the computer was off, and never runs twice at once. The
/// scheduled program only reads: it compares, writes a report and records the result.
/// </summary>
public static class ScheduledTasks
{
    /// <summary>Runs schtasks.exe with these arguments and returns its exit code and messages; replaceable in tests.</summary>
    public delegate (int ExitCode, string Output) Runner(IReadOnlyList<string> arguments);

    static ScheduledTasks() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string TaskName(SavedPair pair) =>
        $"Checker San - {new string(pair.Name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_').ToArray()).Trim()} ({pair.Id})";

    /// <summary>Arguments of the scheduled program: the pair id, and the store when it is not the default one.</summary>
    public static string Arguments(SavedPair pair, string? storePath = null) =>
        $"--scheduled {pair.Id}" + (storePath is null ? "" : $" --pairs \"{Path.GetFullPath(storePath)}\"");

    public static string Xml(SavedPair pair, string executable, string arguments, DateTime? now = null)
    {
        if (pair.Schedule is not { } schedule) throw new ArgumentException(L.T("Cette paire n’est pas planifiée.", "This pair is not scheduled."));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, params object[] content) => new(ns + name, content);
        var start = (now ?? DateTime.Now).Date + schedule.Time.ToTimeSpan();
        var calendar = schedule.Frequency == ScheduleFrequency.Daily
            ? E("ScheduleByDay", E("DaysInterval", 1))
            : E("ScheduleByWeek", E("DaysOfWeek", E(schedule.Day.ToString())), E("WeeksInterval", 1));
        var task = E("Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Author", "Checker San"),
                E("Description", L.F($"Vérification en lecture seule de la paire « {pair.Name} » : {pair.Folder1} ⇄ {pair.Folder2}",
                    $"Read-only check of the pair “{pair.Name}”: {pair.Folder1} ⇄ {pair.Folder2}"))),
            E("Triggers", E("CalendarTrigger", E("StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)), E("Enabled", true), calendar)),
            E("Principals", E("Principal", new XAttribute("id", "Author"),
                E("UserId", Environment.UserDomainName + "\\" + Environment.UserName), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings",
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false), E("StopIfGoingOnBatteries", false),
                E("StartWhenAvailable", true), E("RunOnlyIfNetworkAvailable", false),
                E("IdleSettings", E("StopOnIdleEnd", false), E("RestartOnIdle", false)),
                E("AllowStartOnDemand", true), E("Enabled", true), E("RunOnlyIfIdle", false), E("WakeToRun", false),
                E("ExecutionTimeLimit", "PT72H"), E("Priority", 7)),
            E("Actions", new XAttribute("Context", "Author"), E("Exec", E("Command", executable), E("Arguments", arguments))));
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true }))
            new XDocument(new XDeclaration("1.0", "UTF-16", null), task).Save(writer);
        return text.ToString();
    }

    /// <summary>Creates the task of a scheduled pair, or replaces it with the new schedule.</summary>
    public static void Register(SavedPair pair, string executable, string arguments, Runner? run = null)
    {
        run ??= Schtasks;
        var file = Path.Combine(Path.GetTempPath(), $"checkersan-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, Xml(pair, executable, arguments), Encoding.Unicode);
            Check(run(["/Create", "/TN", TaskName(pair), "/XML", file, "/F"]));
        }
        finally { File.Delete(file); }
    }

    public static bool Exists(SavedPair pair, Runner? run = null) => (run ?? Schtasks)(["/Query", "/TN", TaskName(pair)]).ExitCode == 0;

    /// <summary>Deletes the task of a pair if there is one.</summary>
    public static void Unregister(SavedPair pair, Runner? run = null)
    {
        run ??= Schtasks;
        if (Exists(pair, run)) Check(run(["/Delete", "/TN", TaskName(pair), "/F"]));
    }

    private static void Check((int ExitCode, string Output) result)
    {
        if (result.ExitCode != 0)
            throw new IOException(L.T("Le Planificateur de tâches a refusé l’opération : ", "Task Scheduler refused the operation: ") + result.Output.Trim());
    }

    private static (int ExitCode, string Output) Schtasks(IReadOnlyList<string> arguments)
    {
        // schtasks writes in the console code page of the system language.
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("schtasks.exe could not start.");
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.Result);
    }
}
