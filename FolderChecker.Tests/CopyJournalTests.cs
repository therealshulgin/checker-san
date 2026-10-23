using System.Diagnostics;
using System.Runtime.InteropServices;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task CopyJournalRecoversAppendedStates()
    {
        using var w = new TestWorkspace();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" }) w.FileAt(w.Source, name, name + " contents");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var journal = Path.Combine(w.Root, "copy.json");
        var crash = Path.Combine(w.Root, "crash.json");
        var copy = await new CopyEngine().CopyAsync(plan, journal, new CopyObserver(p =>
        {
            // The journal as an application crash right after the second commit would leave it.
            if (p.CompletedFiles != 2 || File.Exists(crash)) return;
            using var live = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var snapshot = new FileStream(crash, FileMode.CreateNew, FileAccess.Write);
            live.CopyTo(snapshot);
        }));
        Check(copy.Complete, string.Join(";", copy.Errors));
        Equal(1, File.ReadAllLines(journal).Length, "a stopped copy leaves a single plan line");
        Check(File.ReadAllLines(crash).Length > 1, "item states are appended instead of rewriting the plan");

        string NameOf(string relative) => plan.Items.Single(i => i.RelativePath == relative).PartialName;
        File.AppendAllText(crash, $"{{\"PartialName\":\"{NameOf("a.txt")}\",\"Completed\":false,\"VerifiedHash\":\"not-a-digest\"}}\n");
        File.AppendAllText(crash, "{\"PartialName\":\".checkersan-00000000000000000000000000000000.partial\",\"Completed\":true}\n");
        File.AppendAllText(crash, $"{{\"PartialName\":\"{NameOf("c.txt")}\",\"Completed\":tr");
        var restored = CopyEngine.ReadPlan(crash);
        Check(restored is not null, "invalid, unknown and torn state lines keep the journal readable");
        var items = restored!.Items.ToDictionary(i => i.RelativePath);
        Check(items["a.txt"].Completed && items["b.txt"].Completed &&
              items["a.txt"].VerifiedHash is not null && items["b.txt"].DestinationIdentity is not null,
            "committed files are restored from their latest valid states");
        Check(!items["c.txt"].Completed && items["c.txt"].VerifiedHash is null, "an unfinished file is never marked complete");
    }

    private static async Task CopyJournalWritesStayProportional()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows I/O counters");
        using var w = new TestWorkspace();
        const int count = 400;
        // One sector each, so uncached copies write exactly their data and the rest is journal.
        for (var i = 0; i < count; i++) w.FileAt(w.Source, $"photos/{i:D4}.jpg", TestWorkspace.Bytes(4096, i));
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var journal = Path.Combine(w.Root, "copy.json");
        var before = BytesWrittenByProcess();
        var timer = Stopwatch.StartNew();
        var copy = await new CopyEngine().CopyAsync(plan, journal);
        timer.Stop();
        var journalWrites = BytesWrittenByProcess() - before - copy.BytesWritten;
        var planBytes = new FileInfo(journal).Length;
        Check(copy.Complete && copy.CompletedFiles == count, string.Join(";", copy.Errors));
        // Rewriting the whole plan after every item state wrote it over a thousand times for this fixture.
        Check(journalWrites <= 16 * planBytes,
            $"journal writes stay proportional to the plan: {journalWrites:N0} bytes for a {planBytes:N0}-byte plan in {timer.ElapsedMilliseconds} ms");
    }

    private static async Task CopyProgressIsThrottledWithinFiles()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "video.bin", TestWorkspace.Bytes(24 * 1024 * 1024, 91));
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var reports = new List<CopyProgress>();
        var timer = Stopwatch.StartNew();
        var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"), new CopyObserver(reports.Add));
        timer.Stop();
        Check(copy.Complete, string.Join(";", copy.Errors));
        Equal(3, reports.Select(r => r.Phase).Distinct().Count(), "copying, verification and completion remain visible");
        // Each phase change is reported once; repeated byte counters wait 100 ms. One report per MiB used to flood the interface.
        Check(reports.Count <= 4 + timer.ElapsedMilliseconds / 100, $"{reports.Count} reports in {timer.ElapsedMilliseconds} ms");
    }

    private static long BytesWrittenByProcess()
    {
        using var process = Process.GetCurrentProcess();
        if (!GetProcessIoCounters(process.Handle, out var counters)) throw new SkipTestException($"I/O counters unavailable: {Marshal.GetLastWin32Error()}");
        return (long)counters.WriteTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
