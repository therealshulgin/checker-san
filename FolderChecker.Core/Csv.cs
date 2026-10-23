namespace FolderChecker.Core;

/// <summary>Semicolon-separated values for spreadsheets, as written by every Checker San report and journal.</summary>
public static class Csv
{
    public static string Line(IEnumerable<string> fields) => string.Join(";", fields.Select(Field));

    public static string Field(string field)
    {
        // Prevent spreadsheet formula execution from an untrusted filename. JSON retains exact text.
        if (field.Length > 0 && "=+-@\t\r\n".Contains(field[0])) field = "'" + field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
