using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using FolderChecker.App;
using FolderChecker.Core;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static void MainLanguagePreservesResults()
    {
        using var workspace = new TestWorkspace();
        var settings = Path.Combine(workspace.Root, "language.json");
        AppLanguage.Initialize(settings, CultureInfo.GetCultureInfo("fr-FR"));
        var window = CreateVerifiedWindow(workspace);
        try
        {
            var grid = Control<DataGrid>(window, "ResultsGrid");
            Control<ComboBox>(window, "FilterBox").SelectedIndex = 2;
            Control<TextBox>(window, "SearchBox").Text = "song";
            grid.SelectedItem = grid.Items[0];
            var selected = ((RowView)grid.SelectedItem).Row;
            var result = typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            Control<ComboBox>(window, "LanguageBox").SelectedValue = "en";
            Equal("en", L.Language, "selector switches to English");
            Equal("Compare", Control<Button>(window, "CompareButton").Content, "primary action updates");
            Equal("Both folders are identical", Control<TextBlock>(window, "VerdictTitle").Text, "completed verdict updates");
            Equal("Folder 1 contains all of folder 2: Yes", Control<TextBlock>(window, "ContainsTargetVerdict").Text, "directional verdict updates");
            Equal("Identical · SHA-256", ((RowView)grid.SelectedItem).StatusLabel, "row labels update");
            Equal(workspace.Source, Control<TextBox>(window, "SourceBox").Text, "source path unchanged");
            Equal(workspace.Target, Control<TextBox>(window, "TargetBox").Text, "target path unchanged");
            Equal(2, Control<ComboBox>(window, "FilterBox").SelectedIndex, "filter preserved");
            Equal("song", Control<TextBox>(window, "SearchBox").Text, "search preserved");
            Check(ReferenceEquals(selected, ((RowView)grid.SelectedItem).Row), "selected data row preserved");
            Check(ReferenceEquals(result, typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)), "comparison is not rerun or discarded");
            Check(Control<Button>(window, "ExportButton").IsEnabled, "export remains available");
            Check(File.ReadAllText(settings).Contains("en"), "choice persisted to isolated settings");
            Control<ComboBox>(window, "LanguageBox").SelectedValue = "fr";
            Equal("Les deux dossiers sont identiques", Control<TextBlock>(window, "VerdictTitle").Text, "French can be restored");
            AssertContainmentVerdicts(window, "Oui", "Oui");
        }
        finally { window.Close(); AppLanguage.Apply("fr", persist: false); }
    }

    private static void MainLanguageDisabledWhileBusy()
    {
        using var workspace = new TestWorkspace();
        AppLanguage.Initialize(Path.Combine(workspace.Root, "language.json"), CultureInfo.GetCultureInfo("fr-FR"));
        var window = CreateVerifiedWindow(workspace);
        try
        {
            Invoke(window, "SetBusy", true);
            Check(!Control<ComboBox>(window, "LanguageBox").IsEnabled, "language cannot change during scan or copy");
            // Even a programmatic selection event cannot change the operation's language.
            Control<ComboBox>(window, "LanguageBox").SelectedValue = "en";
            Equal("fr", L.Language, "busy handler rejects language change");
            Invoke(window, "SetBusy", false);
            Check(Control<ComboBox>(window, "LanguageBox").IsEnabled, "selector enabled when work ends");
            Equal("fr", Control<ComboBox>(window, "LanguageBox").SelectedValue, "selector reflects active language");
        }
        finally { window.Close(); AppLanguage.Apply("fr", persist: false); }
    }

    private static void MainLanguageKeepsInvalidatedVerdict()
    {
        using var workspace = new TestWorkspace();
        AppLanguage.Initialize(Path.Combine(workspace.Root, "language.json"), CultureInfo.GetCultureInfo("fr-FR"));
        var window = CreateVerifiedWindow(workspace);
        try
        {
            Control<TextBox>(window, "SourceBox").Text = Path.Combine(workspace.Root, "different");
            Control<ComboBox>(window, "LanguageBox").SelectedValue = "en";
            Equal("Folders changed — run the comparison again", Control<TextBlock>(window, "VerdictTitle").Text, "invalidated state updates without becoming a success");
            Equal("Folder 2 contains all of folder 1: Not verified", Control<TextBlock>(window, "ContainsSourceVerdict").Text, "invalidated containment stays inconclusive");
            Check(!Control<Button>(window, "ExportButton").IsEnabled, "invalidated report remains unavailable");
        }
        finally { window.Close(); AppLanguage.Apply("fr", persist: false); }
    }
}
