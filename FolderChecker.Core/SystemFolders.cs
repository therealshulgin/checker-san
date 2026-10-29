namespace FolderChecker.Core;

/// <summary>
/// Folders a volume or a NAS keeps for itself (recycle bins, restore points) and files Checker San leaves behind (copy
/// temporaries, the duplicates quarantine). None of them holds a copy anyone can count on.
/// </summary>
public static class SystemFolders
{
    private static readonly string[] Names = ["$RECYCLE.BIN", "RECYCLER", "System Volume Information", ".Trashes", "#recycle", "@Recycle"];

    /// <summary>The root of a drive (G:\), a mount point or a share, whose system folders a scan leaves out.</summary>
    public static bool IsVolumeRoot(string path) => FileSafety.IsVolumeRoot(path);

    /// <summary>A folder that Windows, macOS, Linux or a NAS keeps at the root of a volume or share: left out of a scan of that root.</summary>
    public static bool IsVolumeSystemFolder(string name) =>
        Names.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith(".Trash-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Never a proof that contents are kept: a temporary of an interrupted copy, a duplicate in quarantine, or a file in a
    /// recycle bin or system folder at any depth. Such files stay listed; a gathering just never counts on them.
    /// </summary>
    public static bool NeverEvidence(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && (CopyEngine.IsCopyTemporary(parts[^1]) || parts[..^1].Any(part => IsVolumeSystemFolder(part) ||
            string.Equals(part, DuplicateRemoval.QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase)));
    }
}
