using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FolderChecker.Core;

namespace FolderChecker.App;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        AppLanguage.Initialize();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(L.T("Une erreur est survenue. Consultez l’état de l’opération avant de continuer.\n\n",
                "An error occurred. Check the operation status before continuing.\n\n") + args.Exception.Message,
                "Checker San", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        string? Value(string name) => Array.IndexOf(e.Args, name) is var index and >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        var store = Value("--pairs");
        if (Value("--scheduled") is { } scheduled)
        {
            await RunScheduledAsync(scheduled, store);
            return;
        }
        // Windows starts scheduled tasks below normal priority and children inherit it: a window the user
        // works in, even one opened from a scheduled result, runs at normal priority.
        try
        {
            using var self = Process.GetCurrentProcess();
            if (self.PriorityClass is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle) self.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { }
        if (Value("--show-result") is { } shown)
        {
            ShowScheduledResult(shown, store);
            return;
        }
        // The main window itself reads --source, --target, --compare and --pair.
        new MainWindow().Show();
    }

    /// <summary>
    /// A check started by Windows Task Scheduler. No window opens while it runs, and the process always ends with
    /// the check and its exit code, the command-line one. When there is something to report, a separate process
    /// shows it: a window left open must never keep the task running, which would make Windows skip the next
    /// checks of the pair and end it at the task's time limit.
    /// </summary>
    private async Task RunScheduledAsync(string id, string? store)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SavedPair? pair = null;
        string? failure = null;
        try { pair = PairStore.Find(id, store); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { failure = exception.Message; }
        PairRun run;
        if (pair is not null) run = await PairCheck.RunAsync(pair, store);
        else
        {
            run = Failed(failure ?? Missing(id));
            ActivityLog.Write(L.T("Paire", "Pair"), run.Failure!);
        }
        if (run.ExitCode != ExitCodes.Clean) StartSelf(store is null ? ["--show-result", id] : ["--show-result", id, "--pairs", store]);
        Shutdown(run.ExitCode);
    }

    /// <summary>The result of the last scheduled check of a pair, in its own process: one window per pair.</summary>
    private void ShowScheduledResult(string id, string? store)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var single = new Mutex(true, @"Local\CheckerSan-result-" + id, out var first);
        if (!first)
        {
            single.Dispose();
            Shutdown(0);
            return;
        }
        SavedPair? pair = null;
        PairRun run;
        try
        {
            pair = PairStore.Find(id, store);
            run = pair?.LastRun ?? Failed(pair is null ? Missing(id) : L.T("le résultat de la vérification n’a pas pu être enregistré.", "the result of the check could not be recorded."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { run = Failed(exception.Message); }
        var window = new ScheduledResultWindow(pair, run, store);
        window.Closed += (_, _) =>
        {
            single.ReleaseMutex();
            single.Dispose();
            Shutdown(0);
        };
        window.Show();
    }

    /// <summary>Starts this program again with the given arguments, as a separate process at normal priority.</summary>
    internal static void StartSelf(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start);
        try { if (child is not null) child.PriorityClass = ProcessPriorityClass.Normal; }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { }
    }

    private static string Missing(string id) =>
        L.F($"la paire {id} n’existe plus. Supprimez sa tâche dans le Planificateur de tâches.", $"pair {id} no longer exists. Delete its task in Task Scheduler.");

    private static PairRun Failed(string message) => new(DateTimeOffset.Now, ExitCodes.Failure, 0, 0, 1, null, message);
}
