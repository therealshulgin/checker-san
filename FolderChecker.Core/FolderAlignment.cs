namespace FolderChecker.Core;

/// <summary>
/// Most files of one folder found, unchanged, under a single subfolder of the other: the comparison faces a folder
/// and one of its parents. Compared with that subfolder, the files pair at the same paths, and a copy into it lands
/// beside them. The files of the other folder outside that subfolder then leave the comparison.
/// </summary>
public static class FolderAlignment
{
    /// <param name="InFolder2">The subfolder is in folder 2, which holds folder 1; otherwise it is in folder 1.</param>
    /// <param name="Files">Files found laid out the same way under the subfolder.</param>
    /// <param name="Outside">Files of the folder holding the subfolder that lie outside it, and would leave the comparison.</param>
    public sealed record Suggestion(bool InFolder2, string Folder, int Files, long Outside);

    /// <summary>
    /// The subfolder holding more than half of the files of the other folder, and of the files found on both sides, or
    /// null when the folders already line up. Small files and copies that prove nothing (temporaries, quarantine,
    /// recycle bins) do not count: a few empty files would otherwise point anywhere.
    /// </summary>
    public static Suggestion? Suggest(ComparisonResult result)
    {
        const long smallFile = 4096;
        var moved = result.Rows.Where(row => row is { Kind: EntryKind.File, Status: ComparisonStatus.Moved, TargetPath: not null }).ToArray();
        var identical = result.Rows.Count(row => row is { Kind: EntryKind.File, Status: ComparisonStatus.Identical });
        if (moved.Length < 3 || moved.Length <= identical) return null;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in moved)
        {
            var first = row.RelativePath.Replace('/', '\\');
            var second = Path.GetRelativePath(result.TargetRoot, row.TargetPath!);
            if (row.SourceSize < smallFile || SystemFolders.NeverEvidence(first) || SystemFolders.NeverEvidence(second)) continue;
            // "2|X" when folder 2 holds folder 1 under X, "1|X" when folder 1 holds folder 2 under X.
            string? key = second.Length > first.Length && second.EndsWith("\\" + first, StringComparison.OrdinalIgnoreCase)
                ? "2|" + second[..^(first.Length + 1)]
                : first.Length > second.Length && first.EndsWith("\\" + second, StringComparison.OrdinalIgnoreCase)
                    ? "1|" + first[..^(second.Length + 1)]
                    : null;
            if (key is not null) counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (counts.Count == 0) return null;
        var best = counts.MaxBy(pair => pair.Value);
        var inFolder2 = best.Key[0] == '2';
        var prefix = best.Key[2..];
        if (prefix.Length == 0 || prefix.Split('\\').Any(part => part is "" or "." or "..")) return null;
        // Most of the folder laid out elsewhere, not only most of the few files found on both sides.
        if (best.Value * 2 <= moved.Length + identical || best.Value * 2 <= (inFolder2 ? result.SourceFileCount : result.TargetFileCount)) return null;
        var under = prefix + "\\";
        // Files of the folder holding the subfolder, at their own path: rows of the other folder's moved files are not theirs.
        var inside = result.Rows.LongCount(row => row.Kind == EntryKind.File && row.RelativePath.Replace('/', '\\').StartsWith(under, StringComparison.OrdinalIgnoreCase) &&
            (inFolder2 ? row.Status is not (ComparisonStatus.OnlySource or ComparisonStatus.Moved) : row.Status != ComparisonStatus.OnlyTarget));
        var total = inFolder2 ? result.TargetFileCount : result.SourceFileCount;
        return new Suggestion(inFolder2, Path.Combine(inFolder2 ? result.TargetRoot : result.SourceRoot, prefix), best.Value, Math.Max(0, total - inside));
    }
}
