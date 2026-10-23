namespace FolderChecker.Core;

/// <summary>
/// The one place where reports, exports, snapshots and copy previews are created. A new file goes outside every
/// checked folder, including through links, junctions and the \\?\ prefix, never into an NTFS stream of a
/// folder, and never over an existing file. Check before a long scan, then create once the result is known.
/// </summary>
public static class OutputFiles
{
    public static void EnsureOutside(string path, IEnumerable<string> roots, string? insideMessage = null) => Check(path, roots, insideMessage);

    public static FileStream CreateOutside(string path, IEnumerable<string> roots, string? insideMessage = null)
    {
        path = Check(path, roots, insideMessage);
        // CreateNew also rejects an existing file or link that appeared after the check.
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    private static string Check(string path, IEnumerable<string> roots, string? insideMessage)
    {
        path = Path.GetFullPath(path);
        var name = Path.GetFileName(path);
        if (name.Length == 0 || name.Contains(':'))
            throw new IOException(L.T("Nom de fichier invalide : « : » désignerait un flux NTFS caché, rattaché au dossier.", "Invalid file name: ':' would denote a hidden NTFS stream attached to the folder."));
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent))
            throw new IOException(L.F($"Le dossier de destination n’existe pas : {parent}", $"The destination folder does not exist: {parent}"));
        var canonicalParent = FileSafety.CanonicalDirectory(parent);
        foreach (var root in roots)
        {
            // A missing root fails its own scan with a clearer message; only its spelling can be compared here.
            if (FileSafety.IsWithin(root, path) || (Directory.Exists(root) && FileSafety.IsWithin(FileSafety.CanonicalDirectory(root), canonicalParent)))
                throw new IOException(insideMessage ?? L.T("Enregistrez le rapport à l’extérieur des dossiers analysés, y compris leurs liens et jonctions.",
                    "Save the report outside the scanned folders, including their links and junctions."));
        }
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException(L.T("Choisissez un nouveau nom : aucun fichier existant n’est remplacé.", "Choose a new name: existing files are never replaced."));
        return path;
    }
}
