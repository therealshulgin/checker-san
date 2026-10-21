using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private sealed class CopyObserver(Action<CopyProgress> action) : IProgress<CopyProgress> { public void Report(CopyProgress value) => action(value); }
    private static async Task CopyMissingBothDirections()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "documents/file.custom", TestWorkspace.Bytes(1024 * 1024 + 3));
        w.DirectoryAt(w.Source, "empty"); w.FileAt(w.Target, "extra.txt", "target only");
        w.FileAt(w.Source, "conflict.txt", "old"); w.FileAt(w.Target, "conflict.txt", "new");
        var comparison = await Compare(w);
        var forward = CopyEngine.CreatePlan(comparison, true);
        Equal(1, forward.FileCount, "only missing source file is planned");
        var copied = await new CopyEngine().CopyAsync(forward, Path.Combine(w.Root, "copy.json"));
        Check(copied.Complete, string.Join(";", copied.Errors));
        Check(Directory.Exists(Path.Combine(w.Target, "empty")), "empty directory is copied");
        Check(File.ReadAllBytes(Path.Combine(w.Source, "documents/file.custom")).SequenceEqual(File.ReadAllBytes(Path.Combine(w.Target, "documents/file.custom"))), "copied bytes identical");
        Equal("new", File.ReadAllText(Path.Combine(w.Target, "conflict.txt")), "existing conflict not overwritten");
        var backward = CopyEngine.CreatePlan(await Compare(w), false);
        var reverse = await new CopyEngine().CopyAsync(backward, Path.Combine(w.Root, "reverse.json"));
        Check(reverse.Complete && File.Exists(Path.Combine(w.Source, "extra.txt")), "reverse direction copies target-only file");
    }
    private static async Task CopyResumePartial()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(5 * 1024 * 1024 + 137);
        w.FileAt(w.Source, "large.bin", data);
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var journal = Path.Combine(w.Root, "copy.json");
        using var stop = new CancellationTokenSource();
        var result = await new CopyEngine().CopyAsync(plan, journal, new CopyObserver(p => { if (p.BytesWritten >= 1024 * 1024) stop.Cancel(); }), stop.Token);
        Check(result.Cancelled, "copy interrupted during file");
        Check(!File.Exists(Path.Combine(w.Target, "large.bin")), "partial file never exposed under final name");
        var restored = CopyEngine.ReadPlan(journal);
        Check(restored != null, "copy journal is readable after process restart");
        var resumed = await new CopyEngine().CopyAsync(restored!, journal);
        Check(resumed.Complete, string.Join(";", resumed.Errors));
        Check(resumed.BytesResumed > 0 && resumed.BytesWritten < data.Length, "copy resumes bytes, not whole file");
        Check(File.ReadAllBytes(Path.Combine(w.Target, "large.bin")).SequenceEqual(data), "full resumed file matches");
        Check(!Directory.EnumerateFiles(w.Target, "*.partial").Any(), "successful copy commits temporary file");
    }
    private static async Task CopyNeverOverwrites()
    {
        using var w = new TestWorkspace(); w.FileAt(w.Source, "file.bin", "source");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        w.FileAt(w.Target, "file.bin", "arrived after scan");
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
        Check(!result.Complete && result.Errors.Count > 0, "appearing destination is not overwritten");
        Equal("arrived after scan", File.ReadAllText(Path.Combine(w.Target, "file.bin")), "pre-existing bytes preserved");
    }
    private static async Task CopyRejectsChangedSource()
    {
        using var w = new TestWorkspace(); var path = w.FileAt(w.Source, "file.bin", "before");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        File.WriteAllText(path, "after!"); File.SetLastWriteTimeUtc(path, plan.Items[0].LastWriteUtc);
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
        Check(!result.Complete && !File.Exists(Path.Combine(w.Target, "file.bin")), "changed source rejected even with restored write time");
    }
    private static async Task CopyChecksExistingPrefix()
    {
        using var w = new TestWorkspace(); w.FileAt(w.Source, "file.bin", TestWorkspace.Bytes(3 * 1024 * 1024));
        var plan = CopyEngine.CreatePlan(await Compare(w), true); var journal = Path.Combine(w.Root, "copy.json");
        using var stop = new CancellationTokenSource();
        await new CopyEngine().CopyAsync(plan, journal, new CopyObserver(p => { if (p.BytesWritten > 0) stop.Cancel(); }), stop.Token);
        var partial = Path.Combine(w.Target, plan.Items[0].PartialName);
        using (var file = new FileStream(partial, FileMode.Open, FileAccess.Write)) file.WriteByte(0xFA);
        var result = await new CopyEngine().CopyAsync(CopyEngine.ReadPlan(journal)!, journal);
        Check(!result.Complete && !File.Exists(Path.Combine(w.Target, "file.bin")), "tampered prefix never committed");
    }
    private static async Task CopyJournalOutsideTrees()
    {
        using var w = new TestWorkspace(); w.FileAt(w.Source, "file.bin", "data");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Target, "journal.json"));
        Check(!result.Complete && !Directory.EnumerateFileSystemEntries(w.Target).Any(), "unsafe journal rejected before writes");
    }
    private static async Task CopyRejectsTraversal()
    {
        using var w = new TestWorkspace(); w.FileAt(w.Source, "file.bin", "data");
        var plan = CopyEngine.CreatePlan(await Compare(w), true); plan.Items[0].RelativePath = "../escape.bin";
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
        Check(!result.Complete && !File.Exists(Path.Combine(w.Root, "escape.bin")), "path traversal rejected");
    }
    private static async Task CopyKeepsCompletedFiles()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "a.txt", "first complete file");
        w.FileAt(w.Source, "long-name.bin", TestWorkspace.Bytes(2 * 1024 * 1024));
        var plan = CopyEngine.CreatePlan(await Compare(w), true); var journal = Path.Combine(w.Root, "copy.json");
        using var stop = new CancellationTokenSource();
        var partial = await new CopyEngine().CopyAsync(plan, journal, new CopyObserver(p => { if (p.CompletedFiles == 1) stop.Cancel(); }), stop.Token);
        Check(partial.Cancelled && partial.CompletedFiles == 1, "first file fully committed before cancellation");
        var first = Path.Combine(w.Target, "a.txt"); var timestamp = File.GetLastWriteTimeUtc(first);
        var result = await new CopyEngine().CopyAsync(CopyEngine.ReadPlan(journal)!, journal);
        Check(result.Complete && result.BytesWritten == 2 * 1024 * 1024, "only remaining file transferred");
        Equal(timestamp, File.GetLastWriteTimeUtc(first), "completed copy not rewritten");
        AssertVerifiedIdentical(await Compare(w));
    }
}
