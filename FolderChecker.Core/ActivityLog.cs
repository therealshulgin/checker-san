using System.Text;

namespace FolderChecker.Core;

/// <summary>
/// One tab-separated line per operation (comparison, copy, duplicate search, snapshot, command line), for a
/// later look at what was checked and when. Unlike the removal journal it is best effort: an operation never
/// fails because its activity line could not be written, whatever the path given.
/// </summary>
public static class ActivityLog
{
    private static readonly object Gate = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "Logs", "activity.log");

    public static void Write(string action, string detail, string? path = null)
    {
        try
        {
            path = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultPath : path);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{action}\t{detail.Replace('\t', ' ').ReplaceLineEndings(" ")}{Environment.NewLine}";
            lock (Gate)
            {
                if (Path.GetDirectoryName(path) is { } folder) Directory.CreateDirectory(folder);
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
