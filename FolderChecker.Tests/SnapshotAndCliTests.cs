using FolderChecker.Cli;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task SnapshotDetectsEveryChange()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "a.txt", "first");
        var large = TestWorkspace.Bytes(5 * 1024 * 1024 + 3, 8);
        var changed = w.FileAt(w.Source, "sub/b.bin", large);
        w.DirectoryAt(w.Source, "empty");
        var outside = w.DirectoryAt(w.Root, "elsewhere");
        w.FileAt(outside, "never-listed.txt", "outside the folder");
        await CreateJunction(Path.Combine(w.Source, "link"), outside);

        var before = await new SnapshotEngine().CreateAsync(w.Source);
        Check(before.Complete && before.Errors.Count == 0, string.Join("; ", before.Errors));
        Equal("a.txt|" + Path.Combine("sub", "b.bin"), string.Join("|", before.Files.Select(file => file.Path)), "files are listed with their paths");
        Equal("link", before.Links.Single(), "a junction is reported, not followed");
        var saved = Path.Combine(w.Root, "snapshot.json");
        SnapshotEngine.Save(before, saved);
        Throws<IOException>(() => SnapshotEngine.Save(before, saved), "an existing snapshot is never replaced");
        Throws<IOException>(() => SnapshotEngine.Save(before, Path.Combine(w.Source, "inside.json")), "a snapshot is never written inside its folder");
        var loaded = SnapshotEngine.Load(saved);
        Check(SnapshotEngine.Compare(loaded, await new SnapshotEngine().CreateAsync(w.Source)).Identical, "an unchanged folder matches its snapshot");

        File.Delete(Path.Combine(w.Source, "a.txt"));
        large[2 * 1024 * 1024] ^= 0xFF;
        var time = File.GetLastWriteTimeUtc(changed);
        File.WriteAllBytes(changed, large);
        File.SetLastWriteTimeUtc(changed, time);
        w.FileAt(w.Source, "c.txt", "new");
        Directory.Delete(Path.Combine(w.Source, "empty"));
        w.DirectoryAt(w.Source, "newdir");
        var after = SnapshotEngine.Compare(loaded, await new SnapshotEngine().CreateAsync(w.Source));
        Check(after.Certain && !after.Identical, "differences are proven");
        Equal("a.txt:Missing|c.txt:Added|empty:MissingFolder|newdir:AddedFolder|" + Path.Combine("sub", "b.bin") + ":Changed",
            string.Join("|", after.Differences.Select(d => $"{d.Path}:{d.Change}")), "each change is detected, even with a restored date");

        File.WriteAllText(saved + ".bad", File.ReadAllText(saved).Replace(loaded.Files[0].Sha256, "not-a-digest"));
        Throws<InvalidDataException>(() => SnapshotEngine.Load(saved + ".bad"), "a damaged snapshot is rejected");
    }

    private static async Task CliExitCodesReportResults()
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
        w.FileAt(w.Source, "same.bin", TestWorkspace.Bytes(70_000, 1));
        w.FileAt(w.Target, "same.bin", TestWorkspace.Bytes(70_000, 1));
        Equal(CheckerSanCli.Clean, (await Run("compare", w.Source, w.Target)).Code, "identical folders exit with 0");
        w.FileAt(w.Source, "extra.txt", "only in folder 1");
        var report = Path.Combine(w.Root, "compare.json");
        var differ = await Run("compare", w.Source, w.Target, "--json", report);
        Equal(CheckerSanCli.Differences, differ.Code, "proven differences exit with 1");
        Check(differ.Output.Contains("missing from 2: 1", StringComparison.Ordinal) && File.ReadAllText(report).Contains("\"OnlySource\"", StringComparison.Ordinal), "summary and JSON report describe the difference");
        MarkOnlineOnly(w.FileAt(w.Target, "cloud.bin", "online"));
        w.FileAt(w.Source, "cloud.bin", "online");
        File.Delete(Path.Combine(w.Source, "extra.txt"));
        Equal(CheckerSanCli.Incomplete, (await Run("compare", w.Source, w.Target)).Code, "an unread online-only file exits with 2");

        var snapshot = Path.Combine(w.Root, "source.snapshot.json");
        Equal(CheckerSanCli.Clean, (await Run("snapshot", w.Source, snapshot)).Code, "a complete snapshot exits with 0");
        Equal(CheckerSanCli.Clean, (await Run("verify", w.Source, snapshot)).Code, "an unchanged folder verifies with 0");
        File.Delete(Path.Combine(w.Source, "same.bin"));
        var verify = await Run("verify", w.Source, snapshot);
        Check(verify.Code == CheckerSanCli.Differences && verify.Output.Contains("MISSING", StringComparison.Ordinal), "a missing file verifies with 1");

        w.FileAt(w.Target, "copy.bin", TestWorkspace.Bytes(70_000, 1));
        Equal(CheckerSanCli.Differences, (await Run("duplicates", w.Target)).Code, "confirmed duplicates exit with 1");
        Check(File.Exists(Path.Combine(w.Target, "copy.bin")) && File.Exists(Path.Combine(w.Target, "same.bin")), "the command line never removes anything");
        Equal(CheckerSanCli.Failure, (await Run("unknown", w.Source)).Code, "an invalid command exits with 3");
        Check(File.ReadAllLines(log).Length >= 7 && File.ReadAllText(log).Contains("CLI verify", StringComparison.Ordinal), "every command is written to the activity log");
    }

    private static T Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Assertion failed: {message}. Expected {typeof(T).Name}.");
    }
}
