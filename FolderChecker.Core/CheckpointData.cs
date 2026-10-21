using System.Text.Json;

namespace FolderChecker.Core;

internal sealed record CheckpointData
{
    public int Schema { get; init; } = 1;
    public string Algorithm { get; init; } = "SHA256";
    public string SourceRoot { get; init; } = "";
    public string TargetRoot { get; init; } = "";
    public string SourceCanonicalRoot { get; init; } = "";
    public string TargetCanonicalRoot { get; init; } = "";
    public bool FindMovedFiles { get; init; }
    public DateTimeOffset SavedAt { get; init; }
    public List<CheckpointFile> Files { get; init; } = [];

    public static CheckpointData? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 128L * 1024 * 1024) return null;
            var data = JsonSerializer.Deserialize<CheckpointData>(stream);
            return data is { Schema: 1, Algorithm: "SHA256", Files: not null } &&
                !string.IsNullOrWhiteSpace(data.SourceRoot) && !string.IsNullOrWhiteSpace(data.TargetRoot) &&
                data.Files.All(file => file is not null && !string.IsNullOrWhiteSpace(file.FullPath) &&
                    file.Hash is { Length: 64 } && file.Hash.All(Uri.IsHexDigit) && file.Size >= 0) ? data : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

internal sealed record CheckpointFile
{
    public string FullPath { get; init; } = "";
    public long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public DateTime CreationUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    public string Hash { get; init; } = "";
    public FileIdentity? Identity { get; init; }
}

internal sealed record FileIdentity(ulong Volume, ulong IdLow, ulong IdHigh, long ChangeTime);
