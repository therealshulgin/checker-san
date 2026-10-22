using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task LocalizationComparisonEnglishPreservesPaths()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            using var w = new TestWorkspace();
            const string common = "Échantillons/été.wav";
            const string missing = "Archives/à conserver.txt";
            w.FileAt(w.Source, common, "matching contents");
            w.FileAt(w.Target, common, "matching contents");
            w.FileAt(w.Source, missing, "unique source contents");
            var phases = new List<string>();
            var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(),
                new InlineProgress(value => phases.Add(value.Phase)));
            Check(result.SnapshotVerified && result.Errors.Count == 0, string.Join("; ", result.Errors));
            Equal(w.Source, result.SourceRoot, "source root is never translated");
            var same = result.Rows.Single(row => Normalize(row.RelativePath) == common);
            Equal("Same path and identical contents (complete SHA-256).",
                same.Detail, "matching file detail is English");
            var absent = result.Rows.Single(row => Normalize(row.RelativePath) == missing);
            Equal("This path is missing from folder 2.", absent.Detail, "missing file detail is English");
            Equal(Path.GetFullPath(Path.Combine(w.Source, missing)), absent.SourcePath, "accented file path remains exact");
            Check(phases.Contains("Listing folder 1") && phases.Contains("Comparison completed"),
                "worker progress uses the selected English language");
        }
        finally { L.SetLanguage(previous); }
    }

    private static async Task LocalizationComparisonErrorsEnglish()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            using var w = new TestWorkspace();
            var thrown = false;
            try { await new ComparisonEngine().CompareAsync(w.Source, w.Source, new ComparisonOptions()); }
            catch (ArgumentException exception)
            {
                thrown = true;
                Check(exception.Message.Contains("Choose two separate folders", StringComparison.Ordinal),
                    "root validation error is English");
            }
            Check(thrown, "same roots are still rejected in English");
            const string name = "fichier-é.txt";
            var path = w.FileAt(w.Source, name, "same bytes");
            w.FileAt(w.Target, name, "same bytes");
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = await Compare(w);
            Check(!result.SnapshotVerified && result.Errors.Any(error => error.Contains("Unable to verify", StringComparison.Ordinal)),
                "read failure has an English application message");
            Check(result.Errors.Any(error => error.Contains(path, StringComparison.Ordinal)),
                "failure retains the exact original accented path");
        }
        finally { L.SetLanguage(previous); }
    }

    private static async Task LocalizationDuplicatesEnglish()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            using var w = new TestWorkspace();
            w.FileAt(w.Source, "été.bin", "byte-identical copies");
            w.FileAt(w.Source, "copie-été.bin", "byte-identical copies");
            var phases = new List<string>();
            var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(),
                new DuplicateInlineProgress(value => phases.Add(value.Phase)));
            Check(result.SnapshotVerified && result.Groups.Count == 1, string.Join("; ", result.Errors));
            Check(result.Groups[0].Files.Any(file => file.RelativePath == "été.bin"),
                "accented duplicate paths remain unchanged");
            Check(phases.Contains("Listing files") && phases.Contains("Complete SHA-256 verification")
                && phases.Contains("Duplicate search completed"), "duplicate progress is English");
            var path = Path.Combine(w.Source, "été.bin");
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var failed = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
            Check(!failed.SnapshotVerified && failed.Errors.Any(error => error.StartsWith("Unable to read (été.bin)", StringComparison.Ordinal)),
                "duplicate read errors are English and preserve the file name");
        }
        finally { L.SetLanguage(previous); }
    }

    private static async Task LocalizationCopyEnglishPreservesPaths()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            using var w = new TestWorkspace();
            const string name = "été-à-copier.bin";
            var bytes = TestWorkspace.Bytes(4096);
            w.FileAt(w.Source, name, bytes);
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            var progress = new List<CopyProgress>();
            var result = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"),
                new CopyObserver(value => progress.Add(value)));
            Check(result.Complete, string.Join("; ", result.Errors));
            Check(progress.Any(value => value.Phase == "Copying")
                && progress.Any(value => value.Phase == "Verifying the copy with SHA-256")
                && progress.Any(value => value.Phase == "Item completed"), "copy phases are English");
            Check(progress.All(value => value.CurrentPath == name), "copy progress never translates names");
            Check(File.ReadAllBytes(Path.Combine(w.Target, name)).SequenceEqual(bytes),
                "English mode copies original names and bytes");
            const string later = "autre-é.bin";
            w.FileAt(w.Source, later, "source original");
            var conflictPlan = CopyEngine.CreatePlan(await Compare(w), true);
            w.FileAt(w.Target, later, "preserve existing destination");
            var refused = await new CopyEngine().CopyAsync(conflictPlan, Path.Combine(w.Root, "conflict.json"));
            Check(!refused.Complete && refused.Errors.Any(error => error.Contains("The destination already exists", StringComparison.Ordinal)),
                "copy refusal is English");
            Equal("preserve existing destination", File.ReadAllText(Path.Combine(w.Target, later)),
                "localization leaves overwrite protection unchanged");
        }
        finally { L.SetLanguage(previous); }
    }

    private static async Task LocalizationRemovalErrorsEnglish()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            using var w = new TestWorkspace();
            w.FileAt(w.Source, "garder-é.bin", "duplicate bytes");
            w.FileAt(w.Source, "copie-é.bin", "duplicate bytes");
            var scan = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
            Check(scan.SnapshotVerified, string.Join("; ", scan.Errors));
            var paths = scan.Groups.Single().Files.Select(file => file.FullPath).ToArray();
            var result = await new DuplicateRemoval().DeleteAsync(scan, paths, DuplicateRemovalMode.Permanent);
            Check(!result.Complete && result.Errors.Contains("Keep at least one file in each duplicate group."),
                "removal safety refusal is English");
            Check(paths.All(File.Exists), "all copies remain after the localized refusal");
        }
        finally { L.SetLanguage(previous); }
    }

    private static async Task LocalizationFrenchCanBeRestored()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("en");
            Equal("1.5", L.F($"{1.5:0.0}", $"{1.5:0.0}"), "English numeric formatting uses a decimal point");
            L.SetLanguage("fr");
            Equal("1,5", L.F($"{1.5:0.0}", $"{1.5:0.0}"), "French numeric formatting uses a decimal comma");
            using var w = new TestWorkspace();
            w.FileAt(w.Source, "été.txt", "contents");
            w.FileAt(w.Target, "été.txt", "contents");
            var phases = new List<string>();
            var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(),
                new InlineProgress(value => phases.Add(value.Phase)));
            AssertVerifiedIdentical(result);
            Equal("Même chemin et contenu identique (SHA-256 complet).", result.Rows.Single().Detail,
                "switching back restores the existing French message exactly");
            Check(phases.Contains("Comparaison terminée"), "French worker progress is restored");
        }
        finally { L.SetLanguage(previous); }
    }
}
