using System.Text;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;

var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
string Touch(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "test");
    return path;
}
string FaroPackage(string parent, string name)
{
    var path = Path.Combine(parent, name);
    Touch(Path.Combine(path, "Main"));
    Touch(Path.Combine(path, "Scans", "Scan000", "ScanDataStream"));
    return path;
}
string TaskFile(string name, params string[] inputs)
{
    var path = Path.Combine(testRoot, name + ".txt");
    File.WriteAllLines(path, inputs.Select(input => input + "\t" + Path.Combine(testRoot, "output")), new UTF8Encoding(true));
    return path;
}
IReadOnlyList<ConversionOptions> Build(BatchConversionTask task, E57OutputMode mode = E57OutputMode.SingleFile) =>
    ConversionTaskList.BuildOptions(task, mode, MileageDirection.Descending, MobileScanDirection.Reverse, 10_000);

var faroRoot = Path.Combine(testRoot, "faro");
var faro002 = FaroPackage(faroRoot, "Scan002.fls");
var faro001 = FaroPackage(faroRoot, "Scan001.fls");
var zfsRoot = Path.Combine(testRoot, "zfs");
var primary = Touch(Path.Combine(zfsRoot, "tlsd.zfs"));
Touch(Path.Combine(zfsRoot, "tlsd.part2.zfs"));
Touch(Path.Combine(zfsRoot, "tlsd.part3.zfs"));
Touch(Path.Combine(zfsRoot, "tlsd.zfi"));
var listPath = TaskFile("mixed-vendors", faroRoot, zfsRoot);
var tasks = ConversionTaskList.Load(listPath);
Check(tasks.Count == 2, "Both vendor directory rows must load.");
Check(tasks[0].InputPaths.Count == 2, "FARO packages must remain separate in standard mode.");
Check(tasks[1].InputPaths.SequenceEqual(new[] { primary }), "ZFS continuation volumes must collapse to the primary exactly once.");
var options = tasks.SelectMany(task => Build(task)).ToArray();
Check(options.Count(o => o.Vendor == ScannerVendor.Faro) == 2 && options.Count(o => o.Vendor == ScannerVendor.ZPlusF) == 1,
    "Options must dispatch both vendor types correctly.");
Check(options.All(o => o.MileageDirection == MileageDirection.Descending && o.ScanDirection == MobileScanDirection.Reverse && o.SegmentScanColumns == 10_000),
    "User conversion settings must be preserved.");
Check(ZfsVolumeSet.Resolve(options.Single(o => o.Vendor == ScannerVendor.ZPlusF).InputFilePath).VolumePaths.Count == 3,
    "The actual ZFS reader must receive the logical primary and resolve all volumes.");
Check(Build(tasks[1], E57OutputMode.SegmentedDataset).Single().OutputMode == E57OutputMode.SegmentedDataset,
    "ZFS segmented output must remain supported.");
var continuous = Build(tasks[0], E57OutputMode.AutoByScanColumnThreshold).Single();
Check(continuous.Vendor == ScannerVendor.Faro && continuous.ContinuousInputPaths.SequenceEqual(new[] { faro001, faro002 }),
    "FARO continuous ordering and grouping must remain intact.");
try
{
    Build(tasks[1], E57OutputMode.AutoByScanColumnThreshold);
    throw new Exception("ZFS must not silently enter FARO continuous mode.");
}
catch (InvalidOperationException ex)
{
    Check(ex.Message.Contains("Z+F") && ex.Message.Contains("单个 E57"), "Unsupported modes must give actionable guidance.");
}
var mixedRoot = Path.Combine(testRoot, "same-directory");
Touch(Path.Combine(mixedRoot, "Scan001.fls"));
Touch(Path.Combine(mixedRoot, "tlsd.zfs"));
var mixedTask = ConversionTaskList.Load(TaskFile("same-directory", mixedRoot)).Single();
Check(mixedTask.InputPaths.Count == 2 && Build(mixedTask).Select(o => o.Vendor).Distinct().Count() == 2,
    "A row containing both formats must not silently omit either format.");
var otherZfsRoot = Path.Combine(testRoot, "other-zfs");
Touch(Path.Combine(otherZfsRoot, "tlsd.zfs"));
var sameNames = ConversionTaskList.Load(TaskFile("same-names", zfsRoot, otherZfsRoot)).SelectMany(task => Build(task)).ToArray();
Check(sameNames.Select(o => o.OutputFilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
    "Repeated ZFS names in different directories must not overwrite each other.");
Check(sameNames[0].OutputFilePath == Build(tasks[1]).Single().OutputFilePath,
    "Output identities must remain stable across repeated imports.");
Check(ConversionTaskList.Load(TaskFile("direct-fls", faro001)).Single().InputPaths.Single() == faro001,
    "Direct FARO scan-package roots must remain supported.");
var emptyRoot = Path.Combine(testRoot, "empty");
Directory.CreateDirectory(emptyRoot);
try
{
    ConversionTaskList.Load(TaskFile("empty", emptyRoot));
    throw new Exception("Empty rows must fail validation.");
}
catch (InvalidDataException ex)
{
    Check(ex.Message.Contains("第 1 行") && ex.Message.Contains(".zfs"), "Empty-row errors must describe both supported formats.");
}
var gapRoot = Path.Combine(testRoot, "missing-part2");
Touch(Path.Combine(gapRoot, "tlsd.zfs"));
Touch(Path.Combine(gapRoot, "tlsd.part3.zfs"));
var gap = Build(ConversionTaskList.Load(TaskFile("gap", gapRoot)).Single()).Single();
Check(new ConversionPrecheckService().Check(gap).Errors.Any(e => e.Contains("part2")),
    "Missing ZFS volumes must still be reported in precheck.");
var orphanRoot = Path.Combine(testRoot, "orphan");
var orphan = Touch(Path.Combine(orphanRoot, "orphan.part3.zfs"));
var orphanOption = Build(ConversionTaskList.Load(TaskFile("orphan", orphanRoot)).Single()).Single();
Check(orphanOption.InputFilePath == orphan && new ConversionPrecheckService().Check(orphanOption).Warnings.Any(w => w.Contains("续卷")),
    "Existing orphan-volume recovery and its warning must be retained.");
Check(ConversionTaskList.RemoveTasks(listPath, new[] { tasks[1] }) == 1 && ConversionTaskList.Load(listPath).Count == 1,
    "Completion removal must remove only the selected directory row.");

Exception? completionError = null;
var completionThread = new Thread(() =>
{
    try
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        string[] LogFiles() => Directory.Exists(logDirectory)
            ? Directory.GetFiles(logDirectory, "*", SearchOption.AllDirectories)
                .Select(path => $"{path}:{File.GetLastWriteTimeUtc(path).Ticks}:{new FileInfo(path).Length}")
                .OrderBy(path => path).ToArray()
            : [];
        var logFilesBefore = LogFiles();
        using var form = new EasyTry.Conversion.UI.PointCloudConversionForm();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        T Field<T>(string name) => (T)form.GetType().GetField(name, flags)!.GetValue(form)!;
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(control => new[] { control }.Concat(Descendants(control)));
        var log = Field<RichTextBox>("logTextBox");
        Check(log.ReadOnly && Descendants(form).Any(control => control.Text == "运行日志"),
            "The conversion window must expose a read-only log panel.");
        var updateProgress = form.GetType().GetMethod("UpdateProgress", flags)!;
        updateProgress.Invoke(form, new object[] { new ConversionProgress(10, "first stage") });
        updateProgress.Invoke(form, new object[] { new ConversionProgress(60, "second stage") });
        Check(log.Text.Contains("first stage") && log.Text.Contains("second stage") &&
              Field<ProgressBar>("conversionProgressBar").Value == 60,
            "The log panel must retain prior progress messages while updating current progress.");
        form.Size = form.MinimumSize;
        form.PerformLayout();
        var layout = Field<TableLayoutPanel>("rootLayout");
        var actions = layout.GetControlFromPosition(0, 3)!;
        Check(actions.Bottom <= layout.ClientSize.Height - layout.Padding.Bottom,
            "All conversion actions must fit at the minimum window size.");
        Check(log.Height >= 40 && log.Parent!.Bottom <= layout.ClientSize.Height - layout.Padding.Bottom,
            "The log panel must remain readable at the minimum window size.");

        var parallelTasks = Field<NumericUpDown>("parallelTasksNumeric");
        Check(parallelTasks.Value == 2 && parallelTasks.Minimum == 1 && parallelTasks.Maximum == 4,
            "The conversion UI must default to two tasks and limit concurrency to 1–4.");
        var setRunning = form.GetType().GetMethod("SetRunningState", flags)!;
        setRunning.Invoke(form, new object[] { true });
        Check(!parallelTasks.Enabled, "Concurrency must not change while a batch is running.");
        setRunning.Invoke(form, new object[] { false });
        var batchProgress = new ConversionProgress?[]
        {
            new(50, "file A", 10, 20, 100, 2),
            new(25, "file B", 5, 20, 50, 1), null
        };
        var updateBatch = form.GetType().GetMethod("UpdateBatchProgress", flags)!;
        updateBatch.Invoke(form, new object[] { batchProgress, new[] { false, false, false }, 2 });
        Check(Field<ProgressBar>("conversionProgressBar").Value == 25 &&
              Field<Label>("statusLabel").Text.Contains("正在转换 2 项") &&
              Field<Label>("statisticsLabel").Text.Contains("150"),
            "Parallel progress must include both active tasks and pending work without overwriting their totals.");
        updateBatch.Invoke(form, new object[] { batchProgress, new[] { true, false, false }, 1 });
        Check(Field<ProgressBar>("conversionProgressBar").Value == 41,
            "A settled task must contribute its full share of batch progress.");

        var completionPath = TaskFile("current-run-completion", faroRoot, zfsRoot);
        var completionTasks = ConversionTaskList.Load(completionPath);
        Field<List<BatchConversionTask>>("taskListItems").AddRange(completionTasks);
        form.GetType().GetField("taskListPath", flags)!.SetValue(form, completionPath);
        Field<CheckBox>("removeCompletedTaskItemsCheckBox").Checked = true;
        var removeCompleted = form.GetType().GetMethod("TryRemoveCompletedTaskListItems", flags)!;
        var completedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var faroOptions = Build(completionTasks[0]);
        completedOutputs.Add(Path.GetFullPath(faroOptions[0].OutputFilePath));
        removeCompleted.Invoke(form, new object[] { completedOutputs });
        Check(ConversionTaskList.Load(completionPath).Count == 2,
            "A partially completed group must remain in the task list.");

        completedOutputs.Add(Path.GetFullPath(faroOptions[1].OutputFilePath).ToUpperInvariant());
        removeCompleted.Invoke(form, new object[] { completedOutputs });
        Check(ConversionTaskList.Load(completionPath).Single().InputRoot == zfsRoot,
            "Only groups fully completed in the current run may be removed.");
        Check(!Directory.Exists(Path.Combine(testRoot, "logs")),
            "Task completion must not create a log directory.");
        Check(logFilesBefore.SequenceEqual(LogFiles()),
            "UI logging and task completion must not create or modify log files.");
    }
    catch (Exception error)
    {
        completionError = error;
    }
});
completionThread.SetApartmentState(ApartmentState.STA);
completionThread.Start();
completionThread.Join();
if (completionError is not null) throw new Exception("Conversion UI completion regression failed.", completionError);

if (args.Length > 0)
{
    var actual = ConversionTaskList.Load(args[0]);
    var actualOptions = actual.SelectMany(task => Build(task)).ToArray();
    Check(actual.Count == 113, "The supplied 113-row list must load in full.");
    Check(actual.Count(t => t.InputPaths.Any(p => ConversionPrecheckService.DetectVendor(p) == ScannerVendor.ZPlusF)) == 22,
        "All 22 actual ZFS directories must be included.");
    Check(actual.Count(t => t.InputPaths.Any(p => ConversionPrecheckService.DetectVendor(p) == ScannerVendor.Faro)) == 91,
        "All 91 actual FARO directories must remain included.");
    Check(actualOptions.All(o => o.Vendor != ScannerVendor.Auto) && actualOptions.Select(o => o.OutputFilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == actualOptions.Length,
        "Every actual scan must have a vendor and unique output.");
    var failingRow = actual.Single(t => string.Equals(t.InputRoot, @"F:\1\scan", StringComparison.OrdinalIgnoreCase));
    Check(failingRow.InputPaths.Count == 1 && Build(failingRow).Single().Vendor == ScannerVendor.ZPlusF,
        "Reported row 92 must become one ZFS conversion task.");
    var zfsErrors = actualOptions.Where(o => o.Vendor == ScannerVendor.ZPlusF)
        .Select(o => (o.InputFilePath, Result: new ConversionPrecheckService().Check(o)))
        .Where(item => !item.Result.IsValid).ToArray();
    Check(zfsErrors.Length == 1 && zfsErrors[0].InputFilePath == @"F:\2\tlsd.zfs" && zfsErrors[0].Result.Errors.Any(e => e.Contains("part2")),
        "The only actual ZFS precheck error should be the previously identified missing part2.");
    Console.WriteLine($"Actual list: {actual.Count} rows; FARO {actualOptions.Count(o => o.Vendor == ScannerVendor.Faro)} scans; Z+F {actualOptions.Count(o => o.Vendor == ScannerVendor.ZPlusF)} logical scans.");
    Console.WriteLine("Actual ZFS precheck: only F:\\2\\tlsd.zfs is blocked by missing part2; row 92 passes.");
}
Console.WriteLine($"PASS: {passed} regression checks. No raw scan data was converted or modified.");
