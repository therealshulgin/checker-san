using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FolderChecker.App;
using FolderChecker.Core;
using Microsoft.VisualBasic.FileIO;

namespace FolderChecker.App.Tests;

internal static partial class Program
{
    private static readonly (string Name, Action Run)[] Tests =
    [
        ("CSV preserves every result, error, hash and report verdict", CsvCompleteness),
        ("CSV neutralizes spreadsheet formulas and retains quoted multiline text", CsvEscaping),
        ("JSON retains the complete comparison model without CSV escaping", JsonCompleteness),
        ("Empty-tree reports still contain their verification verdict", EmptyReport),
        ("CSV and JSON preserve both distinct containment directions", DirectionalReports),
        ("An unverified entry keeps both CSV containment directions inconclusive", UnverifiedReport),
        ("Exports into either compared root or its descendants are rejected", ComparedRootsRejected),
        ("An entire-volume root also rejects exports into its descendants", VolumeRootRejected),
        ("Outside junction aliases into either compared root are rejected", JunctionAliasesRejected),
        ("Existing CSV and JSON output files are never overwritten", ExistingOutputPreserved),
        ("A similarly prefixed sibling directory remains a valid output location", SiblingPrefixAllowed),
        ("Editing either folder invalidates the displayed verdict and report", ChangedPathsInvalidateVerdict),
        ("Swapping folders invalidates the previous directional verdict", SwappedPathsInvalidateVerdict),
        ("The interface shows the two containment directions independently", DirectionalVerdicts),
        ("Incomplete results never show a definitive containment verdict", IncompleteDirectionalVerdicts),
        ("Starting an operation clears the previous containment verdicts", StartingOperationInvalidatesContainment),
        ("The copy preview routes both directions and each single direction correctly", CopyPreviewDirections),
        ("An unfinished copy is selected for resumption without losing its directions", CopyPreviewResume),
        ("A conflicts-only comparison cannot launch an empty completion", CopyPreviewConflictsOnly),
        ("Duplicate results start with no files selected", DuplicateUiStartsUnselected),
        ("Selecting extra copies keeps one file per group and blocks all-selected groups", DuplicateUiKeepsOne),
        ("Changing the duplicate folder invalidates earlier results", DuplicateUiChangedPath),
        ("Incomplete duplicate scans cannot remove files", DuplicateUiIncomplete),
        ("Permanent deletion requires acknowledgement and previews exact kept and removed paths", DuplicateUiPermanentPreview),
        ("Quarantine preview rejects removal of an entire group", DuplicateUiRejectsWholeGroup),
    ];

    [STAThread]
    public static int Main(string[] args)
    {
        var application = new FolderChecker.App.App();
        application.InitializeComponent();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        if (args is ["--render", var directory]) { RenderPreviews(directory); return 0; }
        var failures = 0;
        var timer = Stopwatch.StartNew();
        foreach (var (name, run) in Tests)
        {
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception exception) { failures++; Console.WriteLine($"FAIL {name}\n{exception}"); }
        }
        Console.WriteLine($"\n{Tests.Length - failures} passed; {failures} failed; {timer.Elapsed.TotalSeconds:F2}s.");
        return failures == 0 ? 0 : 1;
    }

    private static void CsvCompleteness()
    {
        using var workspace = new TestWorkspace();
        var result = Sample(workspace);
        var output = Path.Combine(workspace.Root, "full.csv");
        ReportWriter.Write(output, result);
        var bytes = File.ReadAllBytes(output);
        Check(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV has an Excel-friendly UTF-8 BOM");
        var records = ReadCsv(output);
        Equal(2 + result.Rows.Count + result.Errors.Count, records.Count, "header, summary, every row and every error");
        foreach (var record in records) Equal(19, record.Length, "rectangular CSV table");
        Equal("Dossier 2 contient tout le dossier 1", records[0][16], "forward containment has an explicit direction");
        Equal("Arborescences identiques", records[0][17], "tree identity retains its existing column");
        Equal("Dossier 1 contient tout le dossier 2", records[0][18], "reverse containment has an explicit direction");
        Equal("Résumé", records[1][0], "summary exists independently of data rows");
        for (var index = 0; index < result.Rows.Count; index++)
        {
            var expected = result.Rows[index];
            var actual = records[index + 2];
            Equal(new RowView(expected).StatusLabel, actual[0], "status label");
            Equal(expected.RelativePath, actual[2], "relative path");
            Equal(expected.SourcePath ?? "", actual[3], "source path");
            Equal(expected.TargetPath ?? "", actual[4], "target path");
            Equal(expected.SourceHash ?? "", actual[7], "complete source SHA-256");
            Equal(expected.TargetHash ?? "", actual[8], "complete target SHA-256");
            Equal(expected.Detail.ReplaceLineEndings("\n"), actual[9].ReplaceLineEndings("\n"), "detail with punctuation and line breaks");
        }
        foreach (var record in records.Skip(1))
        {
            Equal(result.SourceRoot, record[10], "source root attached to every record");
            Equal(result.TargetRoot, record[11], "target root attached to every record");
            Equal(result.StartedAt.ToString("O"), record[12], "start time with offset");
            Equal(result.CompletedAt.ToString("O"), record[13], "completion time with offset");
            Equal("Non", record[14], "cancelled verdict");
            Equal("Non", record[15], "incomplete snapshot verdict");
            Equal("Non vérifié", record[16], "incomplete forward containment is inconclusive");
            Equal("Non", record[17], "tree identity verdict");
            Equal("Non vérifié", record[18], "incomplete reverse containment is inconclusive");
        }
        Equal(result.Errors[0].ReplaceLineEndings("\n"), records[^1][9].ReplaceLineEndings("\n"), "error details retained");
    }

    private static void CsvEscaping()
    {
        using var workspace = new TestWorkspace();
        string[] dangerous = ["=1+2.wav", "+SUM(1;2).mp4", "-10.wav", "@audio.wav", "\t=1+2", "\r=1+2", "\n=1+2"];
        var result = Sample(workspace) with
        {
            Rows = dangerous.Select(value => new ComparisonRow
            {
                Kind = EntryKind.File, Status = ComparisonStatus.OnlySource,
                RelativePath = value, Detail = value,
            }).ToArray(),
            Errors = [],
        };
        var output = Path.Combine(workspace.Root, "escaped.csv");
        ReportWriter.Write(output, result);
        var rows = ReadCsv(output).Skip(2).ToArray();
        Equal(dangerous.Length, rows.Length, "all formula-like rows remain in report");
        for (var index = 0; index < dangerous.Length; index++)
        {
            Equal(("'" + dangerous[index]).ReplaceLineEndings("\n"), rows[index][2].ReplaceLineEndings("\n"), "formula-like relative path is inert");
            Equal(("'" + dangerous[index]).ReplaceLineEndings("\n"), rows[index][9].ReplaceLineEndings("\n"), "formula-like detail is inert");
        }
    }

    private static void JsonCompleteness()
    {
        using var workspace = new TestWorkspace();
        var result = Sample(workspace);
        var exactName = "=SUM(1;2).wav";
        result = result with { Rows = result.Rows.Append(new ComparisonRow
        {
            Kind = EntryKind.File, Status = ComparisonStatus.Moved, RelativePath = exactName,
            SourcePath = Path.Combine(workspace.Source, exactName), TargetPath = Path.Combine(workspace.Target, "renamed.wav"),
            SourceSize = 100, TargetSize = 100, SourceHash = Hash("content"), TargetHash = Hash("content"),
            Detail = "Même contenu retrouvé ailleurs.",
        }).ToArray() };
        var output = Path.Combine(workspace.Root, "complete.JSON");
        ReportWriter.Write(output, result);
        using var json = JsonDocument.Parse(File.ReadAllText(output));
        Check(json.RootElement.GetProperty("Algorithm").GetString()!.Contains("SHA-256"), "algorithm documented");
        Check(json.RootElement.GetProperty("Scope").GetString()!.Contains("flux alternatifs"), "scope documented");
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var actual = json.RootElement.GetProperty("result").Deserialize<ComparisonResult>(options)!;
        Equal(result.Rows.Count, actual.Rows.Count, "every row survives JSON roundtrip");
        for (var index = 0; index < result.Rows.Count; index++) Equal(result.Rows[index], actual.Rows[index], "all row fields roundtrip exactly");
        Check(result.Errors.SequenceEqual(actual.Errors), "all errors roundtrip exactly");
        Equal(result.SourceRoot, actual.SourceRoot, "source root");
        Equal(result.TargetRoot, actual.TargetRoot, "target root");
        Equal(result.StartedAt, actual.StartedAt, "start time");
        Equal(result.CompletedAt, actual.CompletedAt, "completion time");
        Equal(result.Cancelled, actual.Cancelled, "cancelled flag");
        Equal(result.SnapshotVerified, actual.SnapshotVerified, "snapshot flag");
        Equal(result.SourceFullyContained, actual.SourceFullyContained, "containment flag");
        Equal(result.TargetFullyContained, actual.TargetFullyContained, "reverse containment flag");
        Equal(result.TreesIdentical, actual.TreesIdentical, "tree identity flag");
        Equal(result.ContentCovered, actual.ContentCovered, "content coverage flag");
        Equal(result.SourceFileCount, actual.SourceFileCount, "source count");
        Equal(result.TargetFileCount, actual.TargetFileCount, "target count");
        Equal(result.FilesVerified, actual.FilesVerified, "verified count");
        Equal(result.BytesRead, actual.BytesRead, "bytes read");
        Equal(exactName, actual.Rows[^1].RelativePath, "JSON preserves exact text without CSV apostrophe");
        Equal("Moved", json.RootElement.GetProperty("result").GetProperty("Rows")[result.Rows.Count - 1].GetProperty("Status").GetString(), "status has a readable stable name");
    }

    private static void EmptyReport()
    {
        using var workspace = new TestWorkspace();
        var result = VerifiedResult(workspace) with { Rows = [], SourceFileCount = 0, TargetFileCount = 0, FilesVerified = 0, BytesRead = 0 };
        var output = Path.Combine(workspace.Root, "empty.csv");
        ReportWriter.Write(output, result);
        var records = ReadCsv(output);
        Equal(2, records.Count, "header and summary survive empty trees");
        Equal("Oui", records[1][15], "snapshot verdict retained");
        Equal("Oui", records[1][16], "containment verdict retained");
        Equal("Oui", records[1][17], "tree identity verdict retained");
        Equal("Oui", records[1][18], "reverse containment verdict retained");
    }

    private static void DirectionalReports()
    {
        using var workspace = new TestWorkspace();
        foreach (var sourceContained in new[] { true, false })
        {
            var result = VerifiedResult(workspace) with
            {
                SourceFullyContained = sourceContained, TargetFullyContained = !sourceContained,
                TreesIdentical = false,
            };
            var csvOutput = Path.Combine(workspace.Root, $"direction-{sourceContained}.csv");
            ReportWriter.Write(csvOutput, result);
            foreach (var record in ReadCsv(csvOutput).Skip(1))
            {
                Equal(sourceContained ? "Oui" : "Non", record[16], "CSV preserves the forward verdict");
                Equal(sourceContained ? "Non" : "Oui", record[18], "CSV preserves the distinct reverse verdict");
            }
            var jsonOutput = Path.Combine(workspace.Root, $"direction-{sourceContained}.json");
            ReportWriter.Write(jsonOutput, result);
            using var json = JsonDocument.Parse(File.ReadAllText(jsonOutput));
            var exported = json.RootElement.GetProperty("result");
            Equal(sourceContained, exported.GetProperty("SourceFullyContained").GetBoolean(), "JSON preserves the forward verdict");
            Equal(!sourceContained, exported.GetProperty("TargetFullyContained").GetBoolean(), "JSON preserves the distinct reverse verdict");
        }
    }

    private static void UnverifiedReport()
    {
        using var workspace = new TestWorkspace();
        var result = VerifiedResult(workspace);
        result = result with { Rows = [result.Rows[0] with { Status = ComparisonStatus.Unverified }] };
        var output = Path.Combine(workspace.Root, "unverified.csv");
        ReportWriter.Write(output, result);
        foreach (var record in ReadCsv(output).Skip(1))
        {
            Equal("Non vérifié", record[16], "an unverified entry prevents a definitive forward verdict");
            Equal("Non vérifié", record[18], "an unverified entry prevents a definitive reverse verdict");
        }
    }

    private static void ComparedRootsRejected()
    {
        using var workspace = new TestWorkspace();
        var result = Sample(workspace);
        foreach (var root in new[] { workspace.Source, workspace.Target })
        {
            var child = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            foreach (var parent in new[] { root, child })
            foreach (var extension in new[] { ".csv", ".json" })
            {
                var output = Path.Combine(parent, "report" + extension);
                Throws<IOException>(() => ReportWriter.Write(output, result));
                Check(!File.Exists(output), "rejected export never creates a file in a compared tree");
            }
        }
    }

    private static void JunctionAliasesRejected()
    {
        using var workspace = new TestWorkspace();
        var result = Sample(workspace);
        foreach (var root in new[] { workspace.Source, workspace.Target })
        {
            var child = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var alias = workspace.Junction(root);
            foreach (var parent in new[] { alias, Path.Combine(alias, "nested") })
            {
                var output = Path.Combine(parent, "aliased-report.csv");
                Throws<IOException>(() => ReportWriter.Write(output, result));
                Check(!File.Exists(output), "junction alias cannot bypass output guard");
            }
            Check(!File.Exists(Path.Combine(child, "aliased-report.csv")), "actual compared directory unchanged");
        }
    }

    private static void VolumeRootRejected()
    {
        using var workspace = new TestWorkspace();
        // Exercise volume-root path handling without scanning or writing outside this fixture.
        var result = Sample(workspace) with { SourceRoot = Path.GetPathRoot(workspace.Root)! };
        var output = Path.Combine(workspace.Root, "inside-compared-volume.csv");
        Throws<IOException>(() => ReportWriter.Write(output, result));
        Check(!File.Exists(output), "volume-root descendants are never valid report locations");
    }

    private static void ExistingOutputPreserved()
    {
        using var workspace = new TestWorkspace();
        var result = Sample(workspace);
        foreach (var extension in new[] { ".csv", ".json" })
        {
            var output = Path.Combine(workspace.Root, "existing" + extension);
            byte[] original = [0, 1, 255, 127, 13, 10, 89];
            File.WriteAllBytes(output, original);
            Throws<IOException>(() => ReportWriter.Write(output, result));
            Check(original.SequenceEqual(File.ReadAllBytes(output)), "existing output remains byte-identical");
        }
    }

    private static void SiblingPrefixAllowed()
    {
        using var workspace = new TestWorkspace();
        var sibling = Directory.CreateDirectory(workspace.Source + "-reports").FullName;
        var output = Path.Combine(sibling, "safe.csv");
        ReportWriter.Write(output, Sample(workspace));
        Check(File.Exists(output), "similar name outside source is allowed");
    }

    private static void ChangedPathsInvalidateVerdict()
    {
        using var workspace = new TestWorkspace();
        foreach (var field in new[] { "SourceBox", "TargetBox" })
        {
            var window = CreateVerifiedWindow(workspace);
            try
            {
                Control<TextBox>(window, field).Text = Path.Combine(workspace.Root, "new-selection");
                AssertInvalidated(window);
            }
            finally { window.Close(); }
        }
    }

    private static void SwappedPathsInvalidateVerdict()
    {
        using var workspace = new TestWorkspace();
        var window = CreateVerifiedWindow(workspace);
        try
        {
            Invoke(window, "Swap_Click", window, new RoutedEventArgs());
            Equal(workspace.Target, Control<TextBox>(window, "SourceBox").Text, "source and target are swapped");
            Equal(workspace.Source, Control<TextBox>(window, "TargetBox").Text, "source and target are swapped");
            AssertInvalidated(window);
        }
        finally { window.Close(); }
    }

    private static void DirectionalVerdicts()
    {
        using var workspace = new TestWorkspace();
        foreach (var (sourceContained, targetContained) in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            var window = new MainWindow();
            try
            {
                var result = VerifiedResult(workspace) with
                {
                    SourceFullyContained = sourceContained, TargetFullyContained = targetContained,
                    TreesIdentical = sourceContained && targetContained,
                };
                Invoke(window, "ShowResult", result);
                AssertContainmentVerdicts(window, sourceContained ? "Oui" : "Non", targetContained ? "Oui" : "Non");
            }
            finally { window.Close(); }
        }
    }

    private static void IncompleteDirectionalVerdicts()
    {
        using var workspace = new TestWorkspace();
        var verified = VerifiedResult(workspace);
        foreach (var result in new[]
        {
            verified with { Cancelled = true },
            verified with { SnapshotVerified = false },
            verified with { Errors = ["Un dossier est devenu inaccessible."] },
            verified with { Rows = [verified.Rows[0] with { Status = ComparisonStatus.Unverified }] },
        })
        {
            var window = new MainWindow();
            try
            {
                Invoke(window, "ShowResult", result);
                AssertContainmentVerdicts(window, "Non vérifié", "Non vérifié");
            }
            finally { window.Close(); }
        }
    }

    private static void StartingOperationInvalidatesContainment()
    {
        using var workspace = new TestWorkspace();
        var window = CreateVerifiedWindow(workspace);
        try
        {
            // Both scanning and copying enter this common busy state before filesystem work.
            Invoke(window, "SetBusy", true);
            AssertContainmentVerdicts(window, "Non vérifié", "Non vérifié");
        }
        finally { window.Close(); }
    }

    private static void AssertContainmentVerdicts(MainWindow window, string sourceContained, string targetContained)
    {
        Equal($"Le dossier 2 contient tout le dossier 1 : {sourceContained}", Control<TextBlock>(window, "ContainsSourceVerdict").Text, "forward containment is explicit and current");
        Equal($"Le dossier 1 contient tout le dossier 2 : {targetContained}", Control<TextBlock>(window, "ContainsTargetVerdict").Text, "reverse containment is explicit and current");
    }

    private static ComparisonResult CopyPreviewFixture(TestWorkspace workspace)
    {
        File.WriteAllText(Path.Combine(workspace.Source, "left.txt"), "only left");
        File.WriteAllText(Path.Combine(workspace.Target, "right.txt"), "only right");
        File.WriteAllText(Path.Combine(workspace.Source, "conflict.txt"), "first version");
        File.WriteAllText(Path.Combine(workspace.Target, "conflict.txt"), "second version");
        return new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
    }

    private static void CopyPreviewDirections()
    {
        using var workspace = new TestWorkspace();
        var comparison = CopyPreviewFixture(workspace);
        var dialog = new CopyDialog(comparison, null);
        try
        {
            AwaitPreview(dialog);
            Check(dialog.Plan!.Bidirectional, "default preview completes both folders");
            Equal(2, dialog.Plan.FileCount, "both missing files are planned");
            Check(dialog.Plan.Items.Any(item => item.ReverseDirection) && dialog.Plan.Items.Any(item => !item.ReverseDirection), "both directions survive the preview");
            var preview = (ListBox)dialog.FindName("PreviewList");
            Check(preview.Items.Cast<string>().Any(line => line.Contains("[1 → 2]")) &&
                preview.Items.Cast<string>().Any(line => line.Contains("[2 → 1]")), "each preview row identifies its destination");
            Check(((TextBlock)dialog.FindName("ExclusionsText")).Text.StartsWith("1 "), "excluded conflict is reported");
            Check(((Button)dialog.FindName("StartCopyButton")).IsEnabled, "complete nonempty plan can start");
            var direction = (ComboBox)dialog.FindName("DirectionBox");
            direction.SelectedIndex = 1;
            AwaitPreview(dialog);
            Check(!dialog.Plan!.Bidirectional, "forward choice is one-way");
            Equal(workspace.Source, dialog.Plan.SourceRoot, "forward source");
            Equal(workspace.Target, dialog.Plan.TargetRoot, "forward target");
            Equal("left.txt", dialog.Plan.Items.Single().RelativePath, "only forward missing file");
            direction.SelectedIndex = 2;
            AwaitPreview(dialog);
            Equal(workspace.Target, dialog.Plan!.SourceRoot, "reverse source");
            Equal(workspace.Source, dialog.Plan.TargetRoot, "reverse target");
            Equal("right.txt", dialog.Plan.Items.Single().RelativePath, "only reverse missing file");
            Check(!File.Exists(Path.Combine(workspace.Source, "right.txt")) &&
                !File.Exists(Path.Combine(workspace.Target, "left.txt")), "preview never copies files");
        }
        finally { dialog.Close(); }
    }

    private static void CopyPreviewResume()
    {
        using var workspace = new TestWorkspace();
        var comparison = CopyPreviewFixture(workspace);
        var saved = CopyEngine.CreateBidirectionalPlan(comparison);
        var dialog = new CopyDialog(comparison, saved);
        try
        {
            AwaitPreview(dialog);
            Check(ReferenceEquals(saved, dialog.Plan), "unfinished saved plan is selected before a new plan");
            Check(dialog.Plan!.Bidirectional && dialog.Plan.Items.Any(item => item.ReverseDirection), "resumption retains both directions");
            Equal("Reprendre la copie", ((Button)dialog.FindName("StartCopyButton")).Content, "resumption action is explicit");
            ((ComboBox)dialog.FindName("DirectionBox")).SelectedIndex = 0;
            AwaitPreview(dialog);
            Check(!ReferenceEquals(saved, dialog.Plan) && dialog.Plan!.Bidirectional, "new bidirectional preview is separate from saved plan");
        }
        finally { dialog.Close(); }
    }

    private static void CopyPreviewConflictsOnly()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.Source, "same.txt"), "AAAA");
        File.WriteAllText(Path.Combine(workspace.Target, "same.txt"), "BBBB");
        var comparison = new ComparisonEngine().CompareAsync(workspace.Source, workspace.Target, new ComparisonOptions()).GetAwaiter().GetResult();
        var dialog = new CopyDialog(comparison, null);
        try
        {
            AwaitPreview(dialog);
            Equal(0, dialog.Plan!.Items.Count, "conflicts are never proposed for overwrite");
            Check(!((Button)dialog.FindName("StartCopyButton")).IsEnabled, "empty plan cannot start");
            Check(((TextBlock)dialog.FindName("ExclusionsText")).Text.StartsWith("1 "), "remaining conflict remains explicit");
        }
        finally { dialog.Close(); }
    }

    private static void AwaitPreview(CopyDialog dialog)
    {
        if (dialog.Plan != null) return;
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (dialog.Plan != null || elapsed.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check(dialog.Plan != null, "preview completes: " + ((TextBlock)dialog.FindName("SummaryText")).Text);
    }

    private static MainWindow CreateVerifiedWindow(TestWorkspace workspace)
    {
        var window = new MainWindow();
        Control<TextBox>(window, "SourceBox").Text = workspace.Source;
        Control<TextBox>(window, "TargetBox").Text = workspace.Target;
        var result = VerifiedResult(workspace);
        typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, result);
        Invoke(window, "ShowResult", result);
        Control<DataGrid>(window, "ResultsGrid").SelectedIndex = 0;
        Check(Control<TextBlock>(window, "VerdictTitle").Text.Contains("identiques"), "verified fixture starts with a positive verdict");
        Check(Control<Button>(window, "ExportButton").IsEnabled, "verified fixture can export");
        AssertContainmentVerdicts(window, "Oui", "Oui");
        return window;
    }

    private static void AssertInvalidated(MainWindow window)
    {
        Check(typeof(MainWindow).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null, "stored comparison result is invalidated");
        Check(Control<DataGrid>(window, "ResultsGrid").ItemsSource is null, "old rows are removed");
        Check(!Control<Button>(window, "ExportButton").IsEnabled, "stale report cannot be exported");
        Check(!Control<Button>(window, "OpenSourceButton").IsEnabled, "stale source selection is cleared");
        Check(!Control<Button>(window, "OpenTargetButton").IsEnabled, "stale target selection is cleared");
        Check(Control<TextBlock>(window, "VerdictTitle").Text.Contains("relancez"), "user is told to compare again");
        Equal("—", Control<TextBlock>(window, "IdenticalCount").Text, "stale counts are cleared");
        AssertContainmentVerdicts(window, "Non vérifié", "Non vérifié");
    }

    private static T Control<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void Invoke(MainWindow window, string name, params object[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static ComparisonResult VerifiedResult(TestWorkspace workspace) => Sample(workspace) with
    {
        Rows = [new ComparisonRow
        {
            Kind = EntryKind.File, Status = ComparisonStatus.Identical, RelativePath = "song.wav",
            SourcePath = Path.Combine(workspace.Source, "song.wav"), TargetPath = Path.Combine(workspace.Target, "song.wav"),
            SourceHash = Hash("audio"), TargetHash = Hash("audio"), SourceSize = 5, TargetSize = 5,
        }],
        Errors = [], SnapshotVerified = true, SourceFullyContained = true, TargetFullyContained = true, TreesIdentical = true,
        ContentCovered = true, SourceFileCount = 1, TargetFileCount = 1, FilesVerified = 2, BytesRead = 10,
    };

    private static ComparisonResult Sample(TestWorkspace workspace)
    {
        var rows = Enum.GetValues<ComparisonStatus>().Select((status, index) => new ComparisonRow
        {
            Kind = status == ComparisonStatus.CommonDirectory ? EntryKind.Directory : EntryKind.File,
            Status = status, RelativePath = $"audio\\été; {index}.wav",
            SourcePath = status == ComparisonStatus.OnlyTarget ? null : Path.Combine(workspace.Source, $"sample-{index}.wav"),
            TargetPath = status == ComparisonStatus.OnlySource ? null : Path.Combine(workspace.Target, $"sample-{index}.wav"),
            SourceSize = status == ComparisonStatus.OnlyTarget ? null : 12345678901,
            TargetSize = status == ComparisonStatus.OnlySource ? null : 12345678901,
            SourceHash = status is ComparisonStatus.Identical or ComparisonStatus.Different ? Hash("source") : null,
            TargetHash = status == ComparisonStatus.Identical ? Hash("source") : status == ComparisonStatus.Different ? Hash("target") : null,
            Detail = $"État {status}; détail \"entre guillemets\"\nDeuxième ligne.",
        }).ToArray();
        return new ComparisonResult
        {
            SourceRoot = workspace.Source, TargetRoot = workspace.Target, Rows = rows,
            Errors = ["Erreur d’accès; détail \"précis\"\nChemin indisponible."],
            StartedAt = new DateTimeOffset(2026, 10, 19, 10, 15, 0, TimeSpan.FromHours(2)),
            CompletedAt = new DateTimeOffset(2026, 10, 19, 10, 20, 42, TimeSpan.FromHours(2)),
            SourceFileCount = 7, TargetFileCount = 8, FilesVerified = 5, BytesRead = 24691357802,
        };
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static List<string[]> ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(";");
        var records = new List<string[]>();
        while (!parser.EndOfData) records.Add(parser.ReadFields()!);
        return records;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + description);
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assertion failed: {description}. Expected [{expected}], got [{actual}].");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected exception: " + typeof(T).Name);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _base = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-runs"));
        private readonly List<string> _junctions = [];
        public string Root { get; }
        public string Source { get; }
        public string Target { get; }

        public TestWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(_base, Guid.NewGuid().ToString("N"))).FullName;
            Source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
            Target = Directory.CreateDirectory(Path.Combine(Root, "target")).FullName;
        }

        public string Junction(string destination)
        {
            var link = Path.Combine(Root, "alias-" + _junctions.Count);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "New-Item -ItemType Junction -Path $env:FOLDERCHECK_TEST_LINK -Target $env:FOLDERCHECK_TEST_TARGET -ErrorAction Stop | Out-Null" })
                start.ArgumentList.Add(argument);
            start.Environment["FOLDERCHECK_TEST_LINK"] = link;
            start.Environment["FOLDERCHECK_TEST_TARGET"] = destination;
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();
            Check(process.ExitCode == 0, "junction fixture could be created: " + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
            _junctions.Add(link);
            return link;
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            Check(resolved.StartsWith(_base + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "test cleanup stays inside owned test-runs directory");
            foreach (var junction in _junctions)
            {
                Check(Path.GetFullPath(junction).StartsWith(resolved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "junction cleanup stays inside fixture");
                if (Directory.Exists(junction)) Directory.Delete(junction, false);
            }
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
