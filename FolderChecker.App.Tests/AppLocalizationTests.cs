using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    // These tests never read or write the real user's language preference.
    private sealed class AppLanguageScope : IDisposable
    {
        private readonly string _language = L.Language;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _threadCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _threadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly FieldInfo _pathField = typeof(AppLanguage).GetField("_settingsPath", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _settingsPath;
        internal AppLanguageScope() => _settingsPath = _pathField.GetValue(null);
        public void Dispose()
        {
            AppLanguage.Apply(_language, persist: false);
            _pathField.SetValue(null, _settingsPath);
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = _threadCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _threadUiCulture;
        }
    }

    private static void LanguagePreferenceRoundTrip()
    {
        using var language = new AppLanguageScope();
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "preferences", "settings.json");
        AppLanguage.Initialize(path, CultureInfo.GetCultureInfo("fr-CA"));
        Equal("fr", L.Language, "missing preference follows a French system culture");
        Check(!File.Exists(path), "startup does not create a preference file");
        AppLanguage.Apply("en");
        Check(AppLanguage.LastSaveError is null && File.Exists(path), "chosen language saved at the injected path");
        using (var json = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Equal(1, json.RootElement.GetProperty("Version").GetInt32(), "preference schema remains stable");
            Equal("en", json.RootElement.GetProperty("Language").GetString(), "persisted identifier is en");
        }
        AppLanguage.Initialize(path, CultureInfo.GetCultureInfo("fr-FR"));
        Equal("en", L.Language, "saved preference overrides the system language");
        AppLanguage.Apply("fr");
        AppLanguage.Initialize(path, CultureInfo.GetCultureInfo("en-GB"));
        Equal("fr", L.Language, "switching back persists for the next startup");
        Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length, "atomic save leaves only the settings file");
    }

    private static void InvalidLanguagePreferencesUseSystemFallback()
    {
        using var language = new AppLanguageScope();
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "invalid-settings.json");
        string[] invalid = ["{", "null", "{}", "{\"Version\":2,\"Language\":\"en\"}",
            "{\"Version\":1,\"Language\":\"de\"}", new string(' ', 4097)];
        foreach (var contents in invalid)
        {
            File.WriteAllText(path, contents);
            AppLanguage.Initialize(path, CultureInfo.GetCultureInfo("fr-FR"));
            Equal("fr", L.Language, "invalid settings fall back to French for a French system");
            AppLanguage.Initialize(path, CultureInfo.GetCultureInfo("de-DE"));
            Equal("en", L.Language, "other system cultures fall back to English");
            Equal(contents, File.ReadAllText(path), "initialization does not overwrite invalid settings");
        }
    }

    private static void LanguagePreferenceWriteFailureKeepsDisplayChoice()
    {
        using var language = new AppLanguageScope();
        using var workspace = new TestWorkspace();
        var blocked = Directory.CreateDirectory(Path.Combine(workspace.Root, "settings-is-a-directory")).FullName;
        AppLanguage.Apply("en", settingsPath: blocked);
        Equal("en", L.Language, "display still changes when saving preferences fails");
        Check(!string.IsNullOrWhiteSpace(AppLanguage.LastSaveError), "preference write failure remains observable");
        Check(Directory.Exists(blocked), "existing directory is not replaced");
        Equal(0, Directory.GetFiles(workspace.Root, "*.tmp").Length, "failed save cleans its own temporary file");
    }

    private static void EnglishReportsPreservePathsHashesAndStableJson()
    {
        using var language = new AppLanguageScope();
        using var workspace = new TestWorkspace();
        AppLanguage.Apply("en", persist: false);
        const string relative = "Médias\\été; prise.bin";
        var result = VerifiedResult(workspace);
        result = result with { Rows = [result.Rows[0] with
        {
            RelativePath = relative,
            SourcePath = Path.Combine(workspace.Source, relative),
            TargetPath = Path.Combine(workspace.Target, relative),
            Detail = "Commentaire fourni : à conserver."
        }] };
        var csv = Path.Combine(workspace.Root, "english.csv");
        ReportWriter.Write(csv, result);
        var rows = ReadCsv(csv);
        Equal("Status", rows[0][0], "CSV headers use English");
        Equal("Folder 2 contains all of folder 1", rows[0][16], "forward direction is explicit in English");
        Equal("Folder 1 contains all of folder 2", rows[0][18], "reverse direction is explicit in English");
        Equal("Summary", rows[1][0], "summary is translated");
        Equal("Yes", rows[1][16], "positive verdict is translated");
        Equal("Identical · SHA-256", rows[2][0], "row status uses English");
        Equal("File", rows[2][1], "file kind uses English");
        Equal(relative, rows[2][2], "relative filename remains byte-for-byte textually unchanged");
        Equal(result.Rows[0].SourcePath, rows[2][3], "source path is not translated");
        Equal(result.Rows[0].TargetPath, rows[2][4], "target path is not translated");
        Equal(result.Rows[0].SourceHash, rows[2][7], "SHA-256 is not translated");
        Equal(result.Rows[0].Detail, rows[2][9], "stored detail text is preserved");
        var englishJson = Path.Combine(workspace.Root, "english.json");
        ReportWriter.Write(englishJson, result);
        using var english = JsonDocument.Parse(File.ReadAllText(englishJson));
        Equal("SHA-256 (complete contents)", english.RootElement.GetProperty("Algorithm").GetString(), "JSON explanation is translated");
        Equal("Identical", english.RootElement.GetProperty("result").GetProperty("Rows")[0].GetProperty("Status").GetString(),
            "JSON status identifiers stay stable");
        AppLanguage.Apply("fr", persist: false);
        var frenchJson = Path.Combine(workspace.Root, "french.json");
        ReportWriter.Write(frenchJson, result);
        using var french = JsonDocument.Parse(File.ReadAllText(frenchJson));
        Equal(english.RootElement.GetProperty("result").GetRawText(), french.RootElement.GetProperty("result").GetRawText(),
            "switching language does not mutate serialized comparison data");
    }

    private static void EnglishDuplicateWindowAndSelection()
    {
        using var language = new AppLanguageScope();
        AppLanguage.Apply("en", persist: false);
        var window = DuplicateUiWindow();
        try
        {
            Equal("Scan / resume", DupControl<Button>(window, "ScanButton").Content, "XAML scan action is English");
            Equal("Folder to scan", AutomationProperties.GetName(DupControl<TextBox>(window, "FolderBox")), "accessible label is English");
            Equal("REMOVE", DupControl<DataGrid>(window, "DuplicateGrid").Columns[0].Header, "column header is English");
            Check(DupControl<TextBlock>(window, "StatusText").Text.Contains("group(s) of identical files"), "dynamic scan result is English");
            Check(DupControl<TextBlock>(window, "StatsText").Text.Contains("extra copies"), "dynamic statistics are English");
            var rows = DuplicateUiRows(window);
            Check(rows.All(row => !row.IsSelected && row.ActionLabel == "Kept"), "translated rows do not preselect removal");
            var selected = rows[1];
            var originalPath = selected.File.FullPath;
            selected.IsSelected = true;
            Equal("To remove", selected.ActionLabel, "selected row action is translated");
            Equal("Remove " + selected.RelativePath, selected.SelectionLabel, "accessible selection label preserves filename");
            Equal(originalPath, selected.File.FullPath, "translation never changes the selected path");
            Check(DupControl<Button>(window, "RemoveButton").IsEnabled, "valid English selection can be previewed");
            Check(DupControl<TextBlock>(window, "SelectionText").Text.Contains("Unchecked files stay in place."), "selection guidance is translated");
        }
        finally { window.Close(); }
    }

    private static void EnglishRemovalPreviewRequiresExplicitConsent()
    {
        using var language = new AppLanguageScope();
        AppLanguage.Apply("en", persist: false);
        var scan = DuplicateUiFixture();
        var window = new DuplicateRemovalDialog(scan, [@"C:\fixture\b.bin"], DuplicateRemovalMode.Permanent);
        try
        {
            var confirm = DupControl<Button>(window, "ConfirmRemovalButton");
            var consent = DupControl<CheckBox>(window, "PermanentConsent");
            Equal("Delete permanently", confirm.Content, "destructive action is clearly named in English");
            Equal("I confirm permanent deletion of the listed files.", consent.Content, "English acknowledgement describes permanent deletion");
            Check(!confirm.IsEnabled && consent.IsChecked != true, "language change never grants deletion consent");
            var preview = DupControl<TextBox>(window, "RemovalPreview").Text;
            Check(preview.Contains(@"REMOVE   C:\fixture\b.bin") && preview.Contains(@"KEEP   C:\fixture\a.txt"),
                "English preview retains exact removed and kept paths");
            Check(preview.Contains(scan.Groups[0].Hash), "full SHA-256 remains visible");
            consent.IsChecked = true;
            Check(confirm.IsEnabled, "explicit acknowledgement enables an otherwise valid selection");
            consent.IsChecked = false;
            Check(!confirm.IsEnabled, "withdrawing acknowledgement disables deletion again");
        }
        finally { window.Close(); }
    }

    private static void EnglishCopyPreviewPreservesDirectionsAndResume()
    {
        using var language = new AppLanguageScope();
        using var workspace = new TestWorkspace();
        AppLanguage.Apply("en", persist: false);
        var comparison = CopyPreviewFixture(workspace);
        var saved = CopyEngine.CreateBidirectionalPlan(comparison);
        var dialog = new CopyDialog(comparison, saved);
        try
        {
            AwaitPreview(dialog);
            Equal("Resume copying", ((Button)dialog.FindName("StartCopyButton")).Content, "saved-copy action is English");
            Check(ReferenceEquals(saved, dialog.Plan), "localization retains the exact saved plan");
            var direction = (ComboBox)dialog.FindName("DirectionBox");
            direction.SelectedIndex = 0;
            AwaitPreview(dialog);
            Check(dialog.Plan!.Bidirectional, "first choice still completes both directions");
            Equal("Complete both folders", ((Button)dialog.FindName("StartCopyButton")).Content, "bidirectional action is English");
            var summary = ((TextBlock)dialog.FindName("SummaryText")).Text;
            Check(summary.Contains("Folder 1: " + workspace.Source) && summary.Contains("Folder 2: " + workspace.Target),
                "English preview labels preserve the actual roots");
            var lines = ((ListBox)dialog.FindName("PreviewList")).Items.Cast<string>().ToArray();
            Check(lines.Any(line => line.Contains("[1 → 2] [File]  left.txt")) &&
                lines.Any(line => line.Contains("[2 → 1] [File]  right.txt")), "English file labels do not alter direction or path");
            Check(((TextBlock)dialog.FindName("ExclusionsText")).Text.Contains("no automatic replacement"), "conflict exclusion is translated");
            direction.SelectedIndex = 2;
            AwaitPreview(dialog);
            Equal(workspace.Target, dialog.Plan!.SourceRoot, "third choice retains reverse source");
            Equal(workspace.Source, dialog.Plan.TargetRoot, "third choice retains reverse destination");
            Check(!File.Exists(Path.Combine(workspace.Source, "right.txt")), "preview never copies a file");
        }
        finally { dialog.Close(); }
    }
}
