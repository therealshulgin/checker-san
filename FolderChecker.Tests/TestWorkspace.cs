using System.Text;

namespace FolderChecker.Tests;

/// <summary>Every destructive test operation stays within one generated test directory.</summary>
internal sealed class TestWorkspace : IDisposable
{
    private static readonly string ArtifactsRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, ".test-data"));

    public string Root { get; }
    public string Source { get; }
    public string Target { get; }

    public TestWorkspace()
    {
        Directory.CreateDirectory(ArtifactsRoot);
        Root = Path.Combine(ArtifactsRoot, Guid.NewGuid().ToString("N"));
        Source = Path.Combine(Root, "source");
        Target = Path.Combine(Root, "target");
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
    }

    public string FileAt(string root, string relativePath, byte[] content)
    {
        var path = SafeChildPath(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string FileAt(string root, string relativePath, string content) =>
        FileAt(root, relativePath, Encoding.UTF8.GetBytes(content));

    public string DirectoryAt(string root, string relativePath)
    {
        var path = SafeChildPath(root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public static byte[] Bytes(int count, int seed = 2718)
    {
        var data = new byte[count];
        new Random(seed).NextBytes(data);
        return data;
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        if (!resolved.StartsWith(ArtifactsRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(resolved) != ArtifactsRoot
            || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
        {
            throw new InvalidOperationException("Refusing to clean a path outside the test workspace.");
        }

        // Junctions/symbolic links must be removed without traversing their targets.
        RemoveDirectoryWithoutFollowingLinks(resolved);
    }

    private string SafeChildPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Test path must remain inside the generated workspace.", nameof(relativePath));
        return path;
    }

    private static void RemoveDirectoryWithoutFollowingLinks(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
            var isReparsePoint = (item.Attributes & FileAttributes.ReparsePoint) != 0;
            if (isDirectory && !isReparsePoint)
                RemoveDirectoryWithoutFollowingLinks(item.FullName);
            else if (isDirectory)
                Directory.Delete(item.FullName);
            else
                File.Delete(item.FullName);
        }
        Directory.Delete(directory);
    }
}
