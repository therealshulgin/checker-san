using System.Security.Cryptography;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private sealed class RemovalObserver(Action<DuplicateRemovalProgress> action) : IProgress<DuplicateRemovalProgress>
    { public void Report(DuplicateRemovalProgress value) => action(value); }

    private static async Task<DuplicateResult> ScanRemovalFixture(TestWorkspace workspace)
    {
        var result = await new DuplicateEngine().ScanAsync(workspace.Source, new DuplicateScanOptions { MaxRetries = 0 });
        Check(result.SnapshotVerified, "Removal fixture scan was not verified: " + string.Join("; ", result.Errors));
        return result;
    }

    private static void AddRemovalPair(TestWorkspace workspace)
    {
        workspace.FileAt(workspace.Source, "keep.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        workspace.FileAt(workspace.Source, "nested/copy.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
    }

    private static async Task DuplicateRemovalRejectsEntireGroup()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            scan.Groups.Single().Files.Select(f => f.FullPath).ToArray(), DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0, "all-selected group rejected");
        Check(scan.Groups.Single().Files.All(f => File.Exists(f.FullPath)), "every original remains");
    }

    private static async Task DuplicateRemovalPermanentKeepsOne()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(result.Complete, string.Join("; ", result.Errors));
        Equal(1, result.DeletedPaths.Count, "one selected file removed");
        Equal(1024L * 1024 + 17, result.BytesRemoved, "removed file bytes reported");
        Check(!File.Exists(remove), "selected file no longer exists");
        Check(File.ReadAllBytes(Path.Combine(w.Source, "keep.bin")).SequenceEqual(TestWorkspace.Bytes(1024 * 1024 + 17)),
            "unselected verified copy remains unchanged");
    }

    private static async Task DuplicateRemovalQuarantineIsReversible()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Quarantine);
        Check(result.Complete, string.Join("; ", result.Errors));
        Check(result.QuarantinePath is not null, "quarantine path returned");
        var moved = Path.Combine(result.QuarantinePath!, "nested", "copy.bin");
        Check(File.Exists(moved) && !File.Exists(remove), "relative tree retained in quarantine");
        Check(File.ReadAllBytes(moved).SequenceEqual(File.ReadAllBytes(Path.Combine(w.Source, "keep.bin"))),
            "quarantined data remains recoverable");
        Equal(0L, result.BytesRemoved, "quarantine does not claim freed space");
        Equal(1024L * 1024 + 17, result.BytesMoved, "moved bytes reported");
        var next = await ScanRemovalFixture(w);
        Check(next.SnapshotVerified && next.Groups.Count == 0, "managed quarantine excluded from next duplicate search");
        Check(next.Warnings.Count > 0, "exclusion explicitly reported");
    }

    private static async Task DuplicateRemovalRejectsChangedKeeper()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var keeper = scan.Groups.Single().Files.Single(f => f.RelativePath == "keep.bin");
        using (var write = new FileStream(keeper.FullPath, FileMode.Open, FileAccess.ReadWrite))
        { var first = write.ReadByte(); write.Position = 0; write.WriteByte((byte)(first ^ 1)); }
        File.SetLastWriteTimeUtc(keeper.FullPath, keeper.LastWriteUtc);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && File.Exists(remove), "changed keeper prevents deleting another copy");
    }

    private static async Task DuplicateRemovalRejectsChangedTarget()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var remove = scan.Groups.Single().Files.Single(f => f.RelativePath != "keep.bin");
        using (var write = new FileStream(remove.FullPath, FileMode.Open, FileAccess.ReadWrite))
        { var first = write.ReadByte(); write.Position = 0; write.WriteByte((byte)(first ^ 1)); }
        File.SetLastWriteTimeUtc(remove.FullPath, remove.LastWriteUtc);
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove.FullPath], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && File.Exists(remove.FullPath), "changed selection cannot be deleted using stale scan");
        Check(File.Exists(Path.Combine(w.Source, "keep.bin")), "keeper still exists");
    }

    private static async Task DuplicateRemovalRehashesContent()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        // Metadata and native identities are genuine; a wrong digest must still fail.
        scan = scan with { Groups = [scan.Groups.Single() with
            { Hash = Convert.ToHexString(SHA256.HashData("not the file"u8)) }] };
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && File.Exists(remove), "full SHA-256 is required after validating identity");
    }

    private static async Task DuplicateRemovalRejectsTraversal()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var outside = w.FileAt(w.Target, "outside.bin", "unrelated");
        var scan = await ScanRemovalFixture(w);
        var group = scan.Groups.Single();
        scan = scan with { Groups = [group with { Files =
            [group.Files[0], group.Files[1] with { RelativePath = "../target/outside.bin", FullPath = outside }] }] };
        var result = await new DuplicateRemoval().DeleteAsync(scan, [outside], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0, "traversal rejected before mutations");
        Equal("unrelated", File.ReadAllText(outside), "unrelated file remains untouched");
    }

    private static async Task DuplicateRemovalRejectsNewJunction()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        // Both source and destination are generated subdirectories of this fixture.
        Directory.Move(Path.Combine(w.Source, "nested"), Path.Combine(w.Source, "original-nested"));
        var outside = w.FileAt(w.Target, "copy.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        await CreateJunction(Path.Combine(w.Source, "nested"), w.Target);
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "nested", "copy.bin")], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0, "replacement junction rejected");
        Check(File.Exists(outside) && File.Exists(Path.Combine(w.Source, "original-nested", "copy.bin")),
            "junction destination and original copy both untouched");
    }

    private static async Task DuplicateRemovalCancellationBeforeMutation()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        using var cancellation = new CancellationTokenSource();
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "nested", "copy.bin")], DuplicateRemovalMode.Permanent,
            new RemovalObserver(progress => { if (progress.BytesVerified > 0) cancellation.Cancel(); }), cancellation.Token);
        Check(result.Cancelled && result.DeletedPaths.Count == 0, "cancel during full verification precedes deletion");
        Check(scan.Groups.Single().Files.All(f => File.Exists(f.FullPath)), "every original preserved after cancellation");
    }

    private static async Task DuplicateRemovalCancellationKeepsRemaining()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        w.FileAt(w.Source, "third.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        var scan = await ScanRemovalFixture(w);
        using var cancellation = new CancellationTokenSource();
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "nested", "copy.bin"), Path.Combine(w.Source, "third.bin")], DuplicateRemovalMode.Permanent,
            new RemovalObserver(progress => { if (progress.CompletedFiles == 1) cancellation.Cancel(); }), cancellation.Token);
        Check(result.Cancelled && result.DeletedPaths.Count == 1, "cancel stops after one completed deletion");
        Check(File.Exists(Path.Combine(w.Source, "keep.bin")), "verified keeper remains after partial operation");
        Equal(2, scan.Groups.Single().Files.Count(f => File.Exists(f.FullPath)), "unprocessed copy remains too");
    }

    private static async Task DuplicateRemovalLocksKeeper()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var keeper = Path.Combine(w.Source, "keep.bin");
        var attempted = false; var writeBlocked = false; var deleteBlocked = false;
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "nested", "copy.bin")], DuplicateRemovalMode.Permanent,
            new RemovalObserver(progress =>
            {
                if (attempted || progress.CurrentPath == keeper || progress.BytesVerified == 0) return;
                attempted = true;
                try { using var unexpected = new FileStream(keeper, FileMode.Open, FileAccess.Write); }
                catch (IOException) { writeBlocked = true; }
                try { File.Delete(keeper); }
                catch (IOException) { deleteBlocked = true; }
            }));
        Check(result.Complete, string.Join("; ", result.Errors));
        Check(attempted && writeBlocked && deleteBlocked, "keeper protected against concurrent writes and deletion");
        Check(File.Exists(keeper), "keeper remains after deletion succeeds");
    }

    private static async Task DuplicateRemovalRefusesUnmanagedQuarantine()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        w.FileAt(w.Source, $"{DuplicateRemoval.QuarantineDirectoryName}/user.txt", "user data");
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Quarantine);
        Check(!result.Complete && File.Exists(remove), "unmanaged reserved folder never claimed or overwritten");
        Equal("user data", File.ReadAllText(Path.Combine(w.Source, DuplicateRemoval.QuarantineDirectoryName, "user.txt")),
            "pre-existing quarantine-name folder remains intact");
        Check(!File.Exists(Path.Combine(w.Source, DuplicateRemoval.QuarantineDirectoryName, "checker-san-quarantine.json")),
            "no marker placed in user's folder");
    }

    private static async Task DuplicateRemovalRejectsUnverifiedAndUnknown()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var unverified = await new DuplicateRemoval().DeleteAsync(scan with { SnapshotVerified = false },
            [remove], DuplicateRemovalMode.Permanent);
        var unknown = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "unlisted.bin")], DuplicateRemovalMode.Permanent);
        var canonicalChanged = await new DuplicateRemoval().DeleteAsync(scan with { CanonicalRoot = w.Target },
            [remove], DuplicateRemovalMode.Permanent);
        Check(!unverified.Complete && !unknown.Complete && !canonicalChanged.Complete,
            "unverified snapshot, unknown selection and changed root are refused");
        Check(File.Exists(remove), "original preserved after every invalid request");
    }

    private static async Task DuplicateRemovalRequiresNativeIdentity()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        scan = scan with { Groups = [scan.Groups.Single() with
            { Files = scan.Groups.Single().Files.Select(file => file with { Identity = null }).ToArray() }] };
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && File.Exists(remove), "unsupported native identity fails closed");
    }

    private static async Task DuplicateRemovalRejectsHardlinkReplacement()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var keeper = Path.Combine(w.Source, "keep.bin");
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        File.Delete(remove);
        if (!DuplicateCreateHardLink(remove, keeper, IntPtr.Zero))
            throw new SkipTestException("The fixture filesystem cannot create hard links.");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0, "replacement hard link is never deleted using a stale scan");
        Check(File.Exists(keeper) && File.Exists(remove), "both physical-link paths preserved");
    }

    private static async Task DuplicateRemovalMissingKeeperPreservesLastCopy()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        File.Delete(Path.Combine(w.Source, "keep.bin"));
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0 && File.Exists(remove),
            "a selected file that became the final surviving copy must remain");
    }

    private static async Task DuplicateRemovalLocksSelectedIdentity()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var attempted = false; var writeBlocked = false; var renameBlocked = false;
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Permanent,
            new RemovalObserver(progress =>
            {
                if (attempted || progress.CurrentPath != remove || progress.CompletedFiles != 0) return;
                attempted = true;
                try { using var unexpected = new FileStream(remove, FileMode.Open, FileAccess.Write); }
                catch (IOException) { writeBlocked = true; }
                try { File.Move(remove, Path.Combine(w.Source, "unexpected-move.bin")); }
                catch (IOException) { renameBlocked = true; }
            }));
        Check(result.Complete, string.Join("; ", result.Errors));
        Check(attempted && writeBlocked && renameBlocked, "selected native handle prevents content changes and path replacement");
        Check(!File.Exists(remove) && File.Exists(Path.Combine(w.Source, "keep.bin")), "only originally selected file removed");
    }

    private static async Task DuplicateRemovalValidatesWholeGroupBeforeMutation()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var later = w.FileAt(w.Source, "third.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        var scan = await ScanRemovalFixture(w);
        File.WriteAllText(later, "changed after the scan");
        var earlier = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [earlier, later], DuplicateRemovalMode.Permanent);
        Check(!result.Complete && result.DeletedPaths.Count == 0, "all selected group members revalidated before the first mutation");
        Check(File.Exists(earlier) && File.Exists(later) && File.Exists(Path.Combine(w.Source, "keep.bin")),
            "a stale member preserves the whole group");
    }

    private static async Task DuplicateRemovalDoesNotTrustEmptyQuarantineMarker()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        w.FileAt(w.Source, $"{DuplicateRemoval.QuarantineDirectoryName}/checker-san-quarantine.json", "{}");
        var extra = w.FileAt(w.Source, $"{DuplicateRemoval.QuarantineDirectoryName}/user-copy.bin",
            TestWorkspace.Bytes(1024 * 1024 + 17));
        var scan = await ScanRemovalFixture(w);
        Check(scan.Groups.SelectMany(group => group.Files).Any(file => file.FullPath == extra),
            "an empty marker cannot silently exclude user files from duplicate search");
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var result = await new DuplicateRemoval().DeleteAsync(scan, [remove], DuplicateRemovalMode.Quarantine);
        Check(!result.Complete && File.Exists(remove) && File.Exists(extra), "an incomplete marker cannot authorize managed quarantine");
    }

    private static async Task DuplicateRemovalQuarantineCancellationKeepsProgress()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        w.FileAt(w.Source, "third.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        var scan = await ScanRemovalFixture(w);
        using var cancellation = new CancellationTokenSource();
        var result = await new DuplicateRemoval().DeleteAsync(scan,
            [Path.Combine(w.Source, "nested", "copy.bin"), Path.Combine(w.Source, "third.bin")], DuplicateRemovalMode.Quarantine,
            new RemovalObserver(progress => { if (progress.CompletedFiles == 1) cancellation.Cancel(); }), cancellation.Token);
        Check(result.Cancelled && result.DeletedPaths.Count == 1 && result.QuarantinePath is not null,
            "cancel reports already quarantined files and their recovery directory");
        var movedRelative = Path.GetRelativePath(w.Source, result.DeletedPaths.Single());
        Check(File.Exists(Path.Combine(result.QuarantinePath!, movedRelative)), "completed quarantine remains recoverable after cancellation");
        Check(File.Exists(Path.Combine(w.Source, "keep.bin")), "verified keeper remains at its original location");
        Equal(2, scan.Groups.Single().Files.Count(file => File.Exists(file.FullPath)), "unprocessed original still present");
    }
}
