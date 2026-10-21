using System.Text.Json;
using System.Text.Json.Nodes;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task SyncCompletesBothTrees()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var firstBytes = TestWorkspace.Bytes(1301, 11);
        var secondBytes = TestWorkspace.Bytes(2197, 22);
        w.FileAt(w.Source, "first/nested/report.pdf", firstBytes);
        w.FileAt(w.Target, "second/notes.custom", secondBytes);
        w.DirectoryAt(w.Source, "first/empty");
        w.DirectoryAt(w.Target, "second/empty");

        var before = await Compare(w);
        Check(!before.SourceFullyContained && !before.TargetFullyContained,
            "each folder initially lacks entries from the other");
        var plan = CopyEngine.CreateBidirectionalPlan(before);
        Check(plan.Bidirectional, "one journal describes both directions");
        Equal(2, plan.FileCount, "only the two absent file paths need copying");
        Equal(1, plan.Items.Count(i => i.Kind == EntryKind.File && !i.ReverseDirection),
            "one file is copied from folder 1 to folder 2");
        Equal(1, plan.Items.Count(i => i.Kind == EntryKind.File && i.ReverseDirection),
            "one file is copied from folder 2 to folder 1");

        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "sync.json"));
        Check(result.Complete, string.Join(";", result.Errors));
        Check(File.ReadAllBytes(Path.Combine(w.Target, "first/nested/report.pdf")).SequenceEqual(firstBytes),
            "forward copy preserves every byte");
        Check(File.ReadAllBytes(Path.Combine(w.Source, "second/notes.custom")).SequenceEqual(secondBytes),
            "reverse copy preserves every byte");
        Check(Directory.Exists(Path.Combine(w.Target, "first/empty")), "forward empty directory is restored");
        Check(Directory.Exists(Path.Combine(w.Source, "second/empty")), "reverse empty directory is restored");
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task SyncRestoresMovedPaths()
    {
        using var w = new TestWorkspace();
        var content = TestWorkspace.Bytes(3209, 33);
        w.FileAt(w.Source, "alpha/original.bin", content);
        w.FileAt(w.Target, "beta/renamed.bin", content);
        var comparison = await Compare(w);
        Check(comparison.Rows.Any(r => r.Status == ComparisonStatus.Moved),
            "fixture exercises moved-content detection");

        var plan = CopyEngine.CreateBidirectionalPlan(comparison);
        Equal(2, plan.FileCount, "identical bytes at another path do not suppress missing paths");
        Check(plan.Items.Any(i => Normalize(i.RelativePath) == "alpha/original.bin" && !i.ReverseDirection),
            "original source path must also exist in folder 2");
        Check(plan.Items.Any(i => Normalize(i.RelativePath) == "beta/renamed.bin" && i.ReverseDirection),
            "renamed target path must also exist in folder 1");
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "sync.json"));
        Check(result.Complete, string.Join(";", result.Errors));
        AssertVerifiedIdentical(await Compare(w));
        Equal(2, Directory.EnumerateFiles(w.Source, "*", SearchOption.AllDirectories).Count(),
            "both distinct names remain in folder 1");
        Equal(2, Directory.EnumerateFiles(w.Target, "*", SearchOption.AllDirectories).Count(),
            "both distinct names remain in folder 2");
    }

    private static async Task SyncOneWayMovedPaths()
    {
        foreach (var forward in new[] { true, false })
        {
            using var w = new TestWorkspace();
            w.FileAt(w.Source, "original.bin", "same bytes, different relative path");
            w.FileAt(w.Target, "renamed.bin", "same bytes, different relative path");
            var plan = CopyEngine.CreatePlan(await Compare(w), forward);
            Check(!plan.Bidirectional && plan.Items.All(i => !i.ReverseDirection),
                "one-way plans keep the existing oriented-root representation");
            Equal(1, plan.FileCount, "one missing path is planned in the chosen direction");
            var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
            Check(result.Complete, string.Join(";", result.Errors));
            Check(File.Exists(Path.Combine(forward ? w.Target : w.Source,
                forward ? "original.bin" : "renamed.bin")), "chosen missing path is restored");
            Check(!File.Exists(Path.Combine(forward ? w.Source : w.Target,
                forward ? "renamed.bin" : "original.bin")), "opposite direction is not copied");
            var after = await Compare(w);
            Check(forward ? after.SourceFullyContained : after.TargetFullyContained,
                "the requested containment is now verified");
        }
    }

    private static async Task SyncPreservesConflicts()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "content.txt", "version one");
        w.FileAt(w.Target, "content.txt", "version two");
        w.FileAt(w.Source, "left-clash", "left original file");
        w.FileAt(w.Target, "left-clash/child.txt", "right original child");
        w.FileAt(w.Source, "right-clash/child.txt", "left original child");
        w.FileAt(w.Target, "right-clash", "right original file");
        w.FileAt(w.Source, "safe-left.txt", "copy toward folder 2");
        w.FileAt(w.Target, "safe-right.txt", "copy toward folder 1");

        var comparison = await Compare(w);
        Check(comparison.Rows.Any(r => r.Status == ComparisonStatus.Different),
            "fixture includes a content conflict");
        Check(comparison.Rows.Count(r => r.Status == ComparisonStatus.Conflict) >= 2,
            "fixture includes type conflicts in both orientations");
        var plan = CopyEngine.CreateBidirectionalPlan(comparison);
        Equal(2, plan.FileCount, "only unrelated missing files are safe to copy");
        Check(plan.Items.All(i => !Normalize(i.RelativePath).StartsWith("left-clash", StringComparison.Ordinal)
                                 && !Normalize(i.RelativePath).StartsWith("right-clash", StringComparison.Ordinal)
                                 && i.RelativePath != "content.txt"),
            "conflicting paths and their descendants are excluded from synchronization");
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "sync.json"));
        Check(result.Complete, string.Join(";", result.Errors));
        Equal("version one", File.ReadAllText(Path.Combine(w.Source, "content.txt")), "first content version preserved");
        Equal("version two", File.ReadAllText(Path.Combine(w.Target, "content.txt")), "second content version preserved");
        Equal("left original file", File.ReadAllText(Path.Combine(w.Source, "left-clash")), "first conflicting file preserved");
        Equal("right original child", File.ReadAllText(Path.Combine(w.Target, "left-clash/child.txt")), "second conflicting child preserved");
        Equal("left original child", File.ReadAllText(Path.Combine(w.Source, "right-clash/child.txt")), "first conflicting child preserved");
        Equal("right original file", File.ReadAllText(Path.Combine(w.Target, "right-clash")), "second conflicting file preserved");
        var after = await Compare(w);
        Check(!after.TreesIdentical && !after.SourceFullyContained && !after.TargetFullyContained,
            "copy completion never implies unresolved conflicts disappeared");
    }

    private static async Task SyncResumesReverseFile()
    {
        using var w = new TestWorkspace();
        const string firstName = "a.txt";
        const string reverseName = "z-large.bin";
        w.FileAt(w.Source, firstName, "a completed forward transfer");
        var reverseBytes = TestWorkspace.Bytes(5 * 1024 * 1024 + 113, 44);
        w.FileAt(w.Target, reverseName, reverseBytes);
        var plan = CopyEngine.CreateBidirectionalPlan(await Compare(w));
        var journal = Path.Combine(w.Root, "sync.json");
        using var stop = new CancellationTokenSource();
        var interrupted = await new CopyEngine().CopyAsync(plan, journal, new CopyObserver(p =>
        {
            if (p.CurrentPath.EndsWith(reverseName, StringComparison.Ordinal) && p.BytesWritten >= 1024 * 1024)
                stop.Cancel();
        }), stop.Token);

        Check(interrupted.Cancelled && interrupted.CompletedFiles == 1, "reverse file interrupts after forward file is committed");
        var completedPath = Path.Combine(w.Target, firstName);
        var completedTimestamp = File.GetLastWriteTimeUtc(completedPath);
        var completedBytes = File.ReadAllBytes(completedPath);
        Check(!File.Exists(Path.Combine(w.Source, reverseName)), "unfinished reverse file is not visible under its final name");
        var reverseItem = plan.Items.Single(i => i.Kind == EntryKind.File && i.ReverseDirection);
        var partialPath = Path.Combine(w.Source, reverseItem.PartialName);
        Check(File.Exists(partialPath), "reverse temporary file is stored under folder 1");
        var retainedLength = new FileInfo(partialPath).Length;
        Check(retainedLength > 0 && retainedLength < reverseBytes.Length, "partial prefix is nonempty and incomplete");

        var restored = CopyEngine.ReadPlan(journal);
        Check(restored is not null && restored.Bidirectional, "restart restores the synchronization plan");
        Equal(2, restored!.Schema, "new journal has explicit direction schema");
        Check(restored.Items.Single(i => i.RelativePath == reverseName).ReverseDirection, "restart retains reverse item direction");
        var resumed = await new CopyEngine().CopyAsync(restored, journal);
        Check(resumed.Complete, string.Join(";", resumed.Errors));
        Equal(retainedLength, resumed.BytesResumed, "only the interrupted reverse prefix is reused");
        Equal(reverseBytes.LongLength - retainedLength, resumed.BytesWritten, "only uncopied bytes are transferred");
        Equal(completedTimestamp, File.GetLastWriteTimeUtc(completedPath), "completed forward file is not rewritten");
        Check(File.ReadAllBytes(completedPath).SequenceEqual(completedBytes), "completed forward file remains byte-identical");
        Check(File.ReadAllBytes(Path.Combine(w.Source, reverseName)).SequenceEqual(reverseBytes), "reverse file resumes into folder 1");
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task SyncResumesLegacyJournal()
    {
        foreach (var forward in new[] { true, false })
        {
            using var w = new TestWorkspace();
            var origin = forward ? w.Source : w.Target;
            var destination = forward ? w.Target : w.Source;
            var content = TestWorkspace.Bytes(3 * 1024 * 1024 + 17, forward ? 55 : 56);
            w.FileAt(origin, "legacy.bin", content);
            var plan = CopyEngine.CreatePlan(await Compare(w), forward);
            var journal = Path.Combine(w.Root, "copy.json");
            using var stop = new CancellationTokenSource();
            var interrupted = await new CopyEngine().CopyAsync(plan, journal,
                new CopyObserver(p => { if (p.BytesWritten >= 1024 * 1024) stop.Cancel(); }), stop.Token);
            Check(interrupted.Cancelled, "legacy fixture has an unfinished partial transfer");
            var json = JsonNode.Parse(File.ReadAllText(journal))!.AsObject();
            json["Schema"] = 1;
            json.Remove("Bidirectional");
            foreach (var item in json["Items"]!.AsArray()) item!.AsObject().Remove("ReverseDirection");
            File.WriteAllText(journal, json.ToJsonString());

            var restored = CopyEngine.ReadPlan(journal);
            Check(restored is not null && !restored.Bidirectional && restored.Items.All(i => !i.ReverseDirection),
                "schema-one journals without direction flags retain their oriented roots");
            Equal(origin, restored!.SourceRoot, "legacy source remains the original actual source");
            Equal(destination, restored.TargetRoot, "legacy destination remains the original actual destination");
            var resumed = await new CopyEngine().CopyAsync(restored, journal);
            Check(resumed.Complete && resumed.BytesResumed > 0, string.Join(";", resumed.Errors));
            Check(File.ReadAllBytes(Path.Combine(destination, "legacy.bin")).SequenceEqual(content),
                "legacy resume copies into its original destination");
            AssertVerifiedIdentical(await Compare(w));
        }
    }

    private static async Task SyncRejectsAmbiguousJournal()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "item.txt", "original");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var journal = Path.Combine(w.Root, "invalid.json");
        foreach (var (schema, bidirectional, reverse) in new[]
                 { (1, true, false), (1, false, true), (2, false, true) })
        {
            plan.Schema = schema;
            plan.Bidirectional = bidirectional;
            plan.Items[0].ReverseDirection = reverse;
            File.WriteAllText(journal, JsonSerializer.Serialize(plan));
            Check(CopyEngine.ReadPlan(journal) is null,
                $"ambiguous schema={schema}, bidirectional={bidirectional}, reverse={reverse} is rejected");
        }
        Check(!Directory.EnumerateFileSystemEntries(w.Target).Any(), "invalid journals never create destination entries");
        Equal("original", File.ReadAllText(Path.Combine(w.Source, "item.txt")), "original source remains intact");
    }

    private static async Task SyncSwappedRoots()
    {
        using var w = new TestWorkspace();
        var firstPath = w.FileAt(w.Source, "first.txt", "from original folder 1");
        var secondPath = w.FileAt(w.Target, "second.txt", "from original folder 2");
        var firstTimestamp = File.GetLastWriteTimeUtc(firstPath);
        var secondTimestamp = File.GetLastWriteTimeUtc(secondPath);
        var comparison = await new ComparisonEngine().CompareAsync(w.Target, w.Source, new ComparisonOptions());
        var plan = CopyEngine.CreateBidirectionalPlan(comparison);
        Equal(w.Target, plan.SourceRoot, "plan keeps the newly selected folder 1");
        Equal(w.Source, plan.TargetRoot, "plan keeps the newly selected folder 2");
        Check(plan.Items.Single(i => i.RelativePath == "first.txt").ReverseDirection,
            "original folder-1 file is now the reverse transfer");
        Check(!plan.Items.Single(i => i.RelativePath == "second.txt").ReverseDirection,
            "original folder-2 file is now the forward transfer");
        var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "sync.json"));
        Check(result.Complete, string.Join(";", result.Errors));
        Equal(firstTimestamp, File.GetLastWriteTimeUtc(firstPath), "original folder-1 file was not rewritten");
        Equal(secondTimestamp, File.GetLastWriteTimeUtc(secondPath), "original folder-2 file was not rewritten");
        Equal("from original folder 1", File.ReadAllText(firstPath), "original first contents preserved");
        Equal("from original folder 2", File.ReadAllText(secondPath), "original second contents preserved");
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task SyncRejectsCorruptedReversePartial()
    {
        using var w = new TestWorkspace();
        var content = TestWorkspace.Bytes(3 * 1024 * 1024 + 71, 66);
        w.FileAt(w.Target, "reverse.bin", content);
        var plan = CopyEngine.CreateBidirectionalPlan(await Compare(w));
        var journal = Path.Combine(w.Root, "sync.json");
        using var stop = new CancellationTokenSource();
        var interrupted = await new CopyEngine().CopyAsync(plan, journal,
            new CopyObserver(p => { if (p.BytesWritten >= 1024 * 1024) stop.Cancel(); }), stop.Token);
        Check(interrupted.Cancelled, "reverse prefix exists after interruption");
        var partial = Path.Combine(w.Source, plan.Items.Single(i => i.Kind == EntryKind.File).PartialName);
        using (var file = new FileStream(partial, FileMode.Open, FileAccess.ReadWrite))
        {
            var prior = file.ReadByte();
            Check(prior >= 0, "partial file has a first byte to corrupt");
            file.Position = 0;
            file.WriteByte((byte)(prior ^ 0xFF));
        }
        var resumed = await new CopyEngine().CopyAsync(CopyEngine.ReadPlan(journal)!, journal);
        Check(!resumed.Complete && resumed.Errors.Count > 0, "invalid reverse prefix fails verification");
        Check(!File.Exists(Path.Combine(w.Source, "reverse.bin")), "corrupt reverse partial is never committed");
        Check(File.ReadAllBytes(Path.Combine(w.Target, "reverse.bin")).SequenceEqual(content), "reverse source remains intact");
    }

    private static async Task SyncRejectsAppearingReverseDestination()
    {
        using var w = new TestWorkspace();
        var content = TestWorkspace.Bytes(3 * 1024 * 1024 + 91, 77);
        w.FileAt(w.Target, "reverse.bin", content);
        var plan = CopyEngine.CreateBidirectionalPlan(await Compare(w));
        var journal = Path.Combine(w.Root, "sync.json");
        using var stop = new CancellationTokenSource();
        var interrupted = await new CopyEngine().CopyAsync(plan, journal,
            new CopyObserver(p => { if (p.BytesWritten >= 1024 * 1024) stop.Cancel(); }), stop.Token);
        Check(interrupted.Cancelled, "copy is paused before a final destination exists");
        var appearing = w.FileAt(w.Source, "reverse.bin", "arrived while transfer was paused");
        var timestamp = File.GetLastWriteTimeUtc(appearing);
        var resumed = await new CopyEngine().CopyAsync(CopyEngine.ReadPlan(journal)!, journal);
        Check(!resumed.Complete && resumed.Errors.Count > 0, "new reverse destination blocks commit");
        Equal("arrived while transfer was paused", File.ReadAllText(appearing), "new destination is not overwritten");
        Equal(timestamp, File.GetLastWriteTimeUtc(appearing), "new destination is not modified");
        Check(File.ReadAllBytes(Path.Combine(w.Target, "reverse.bin")).SequenceEqual(content), "reverse source remains intact");
    }
}
