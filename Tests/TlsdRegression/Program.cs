using System.Globalization;
using System.Text;
using System.Text.Json;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using EasyTry.Conversion.Sources;
using EasyTry.Conversion.Writers;

var testRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) { throw new Exception(message); }
    passed++;
}
string Write(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(true));
    return path;
}
string Json(double speed = 0.5, double width = 1.5, double diameter = 5.4) =>
    JsonSerializer.Serialize(new { Speed = speed, CircleInfo = new { RingWidth = width, RingDiameter = diameter } });
IReadOnlyList<ConversionOptions> Build(BatchConversionTask task, E57OutputMode mode = E57OutputMode.SingleFile) =>
    ConversionTaskList.BuildOptions(task, mode, MileageDirection.Ascending, MobileScanDirection.Forward, 10_000);
ConversionOptions Options(string input, string? tlsd = null, E57OutputMode mode = E57OutputMode.SingleFile) => new()
{
    InputFilePath = input,
    TlsdFilePath = tlsd,
    OutputFilePath = Path.Combine(testRoot, "output", Guid.NewGuid().ToString("N") + ".e57"),
    Vendor = ScannerVendor.ZPlusF,
    OutputMode = mode,
    SegmentScanColumns = 1
};
async Task<(string Text, ConversionReport Report)> Convert(ConversionOptions options)
{
    var service = new PointCloudConversionService(_ => new FixtureScanSource(), () => new FixtureWriter());
    var report = await service.ConvertAsync(options, null, CancellationToken.None);
    return (File.ReadAllText(report.MetadataDocumentPath!), report);
}

var input = Write(Path.Combine(testRoot, "project", "scan", "1", "tlsd.zfs"), "raw fixture unchanged");
var localTlsd = Write(Path.Combine(testRoot, "project", "local.tlsd"), Json(1, 2, 6));
var explicitTlsd = Write(Path.Combine(testRoot, "external", "explicit.tlsd"), Json());
var inputRoot = Path.GetDirectoryName(input)!;
var taskPath = Write(Path.Combine(testRoot, "three-columns.txt"),
    $"{inputRoot}\t{testRoot}\t{explicitTlsd}\r\n{inputRoot}\t{testRoot}\t\r\n{inputRoot}\t{testRoot}\r\n");
var tasks = ConversionTaskList.Load(taskPath);
Check(tasks.Count == 3 && tasks[0].TlsdFilePath == explicitTlsd && tasks[1].TlsdFilePath is null && tasks[2].TlsdFilePath is null,
    "Three columns, an empty final column, and legacy two columns must load together.");
Check(Build(tasks[0]).Single().TlsdFilePath == explicitTlsd &&
    Build(tasks[0], E57OutputMode.SegmentedDataset).Single().TlsdFilePath == explicitTlsd,
    "Explicit TLSD must reach single-file and segmented options.");
var package = Path.Combine(testRoot, "faro", "Scan001.fls");
Write(Path.Combine(package, "Main"), "fixture");
Write(Path.Combine(package, "Scans", "Scan000", "ScanDataStream"), "fixture");
var continuous = Build(new BatchConversionTask(Path.GetDirectoryName(package)!, testRoot, [package])
    { TlsdFilePath = explicitTlsd }, E57OutputMode.AutoByScanColumnThreshold).Single();
Check(continuous.TlsdFilePath == explicitTlsd, "Continuous FARO task options must preserve TLSD.");
var relativeList = Write(Path.Combine(testRoot, "relative.txt"), $"{inputRoot}\t{testRoot}\texternal\\explicit.tlsd\r\n");
Check(ConversionTaskList.Load(relativeList).Single().TlsdFilePath == explicitTlsd,
    "Relative TLSD paths must resolve beside the task list.");
var removePath = Write(Path.Combine(testRoot, "remove.txt"), $"{inputRoot}\t{testRoot}\t{explicitTlsd}\r\n{inputRoot}\t{testRoot}\\retained\t\r\n");
Check(ConversionTaskList.RemoveTasks(removePath, [ConversionTaskList.Load(removePath)[0]]) == 1 &&
    File.ReadAllLines(removePath).Single().EndsWith('\t'), "Removing a three-column task must preserve another row's empty third column.");
foreach (var invalidRow in new[] { $"{inputRoot}\t{testRoot}\textra\tcolumn", $"{inputRoot}\t{testRoot}\twrong.json" })
{
    try
    {
        ConversionTaskList.Load(Write(Path.Combine(testRoot, "invalid.txt"), invalidRow));
        throw new Exception("Invalid task-list columns must be rejected.");
    }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("第 1 行"), "Invalid columns must report their row."); }
}

var oldCulture = CultureInfo.CurrentCulture;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
var explicitResult = await Convert(Options(input, explicitTlsd));
CultureInfo.CurrentCulture = oldCulture;
Check(explicitResult.Text.Contains("小车速度：0.5 m/s") && explicitResult.Text.Contains("环宽：1.5 m") &&
    explicitResult.Text.Contains("内径：5.4 m") && explicitResult.Text.Contains($"TLSD路径：{explicitTlsd}"),
    "Actual conversion completion must write explicit TLSD values with invariant decimals and units.");
Check(explicitResult.Text.Contains("计算得到的每秒圈数：97.65625 圈/秒"), "Existing scanner acquisition metadata must survive.");
Check(!explicitResult.Text.Contains("小车速度：1 m/s"), "An explicit path must override automatic local discovery.");
var discovered = await Convert(Options(input));
Check(discovered.Text.Contains("小车速度：1 m/s") && discovered.Text.Contains($"TLSD路径：{localTlsd}"),
    "Automatic discovery must find metadata above a nested scan directory.");
var directInput = Write(Path.Combine(testRoot, "direct", "Scan001.fls"), "raw FARO fixture");
Write(Path.Combine(testRoot, "direct", "direct.TLSD"), Json());
Check((await Convert(Options(directInput))).Text.Contains("小车速度：0.5 m/s"),
    "Direct FARO inputs and uppercase TLSD extensions must support local discovery.");
var chineseInput = Write(Path.Combine(testRoot, "chinese-project", "激光扫描数据", "Scan001.fls"), "fixture");
Write(Path.Combine(testRoot, "chinese-project", "metadata.tlsd"), Json());
Check((await Convert(Options(chineseInput))).Text.Contains("小车速度：0.5 m/s"),
    "The 激光扫描数据 directory convention must resolve its project parent's metadata.");
var segmented = await Convert(Options(input, explicitTlsd, E57OutputMode.SegmentedDataset));
Check(Path.GetFileName(segmented.Report.MetadataDocumentPath) == "扫描说明.txt" &&
    segmented.Text.Contains("内径：5.4 m"), "Segmented completion must write project parameters beside the dataset manifest.");
var continuousResult = await Convert(continuous);
Check(continuousResult.Text.Contains("小车速度：0.5 m/s"), "Continuous source and resolved output-mode cloning must preserve explicit TLSD.");
Check(File.ReadAllText(input) == "raw fixture unchanged" && File.ReadAllText(explicitTlsd) == Json(),
    "Reading metadata and converting fixtures must not modify original input or TLSD files.");

var strings = Write(Path.Combine(testRoot, "strings.tlsd"), "{\"speed\":\"0.75\",\"circleinfo\":{\"ringwidth\":\"1.2\",\"ringdiameter\":\"6.1\",},}");
Check((await Convert(Options(input, strings))).Text.Contains("内径：6.1 m"),
    "Numeric strings, case-insensitive field names, and trailing commas must be supported.");
var invalidValues = Write(Path.Combine(testRoot, "invalid-values.tlsd"), "{\"Speed\":\"NaN\",\"CircleInfo\":{\"RingWidth\":0,\"RingDiameter\":-1}}");
var invalidText = (await Convert(Options(input, invalidValues))).Text;
Check(invalidText.Contains("小车速度：未读取到") && invalidText.Contains("环宽：未读取到") && invalidText.Contains("内径：未读取到"),
    "Non-finite, zero, and negative values must never be exported as usable parameters.");
var partial = Write(Path.Combine(testRoot, "partial.tlsd"), "{\"Speed\":1,\"CircleInfo\":null}");
var partialText = (await Convert(Options(input, partial))).Text;
Check(partialText.Contains("小车速度：1 m/s") && partialText.Contains("环宽：未读取到"),
    "Missing CircleInfo must retain valid speed without inventing geometric parameters.");
var corrupt = Write(Path.Combine(testRoot, "corrupt.tlsd"), "{broken JSON");
Check((await Convert(Options(input, corrupt))).Text.Contains(".tlsd 参数读取失败"),
    "Malformed TLSD must produce a diagnostic without failing conversion.");
var missingTlsd = Path.Combine(testRoot, "missing.tlsd");
var missingText = (await Convert(Options(input, missingTlsd))).Text;
Check(missingText.Contains("指定的 .tlsd 文件不存在") && missingText.Contains("小车速度：未读取到"),
    "A missing explicit TLSD must not silently use another local file.");
Write(Path.Combine(testRoot, "project", "other.tlsd"), Json());
Check((await Convert(Options(input))).Text.Contains("多个 .tlsd 文件"),
    "Ambiguous project metadata must request an explicit path instead of choosing arbitrarily.");
var unrelated = Write(Path.Combine(testRoot, "unrelated", "scan.zfs"), "fixture");
Write(Path.Combine(testRoot, "ancestor.tlsd"), Json());
Check((await Convert(Options(unrelated))).Text.Contains("当前项目未发现 .tlsd 文件"),
    "Automatic discovery must not borrow metadata from an unrelated ancestor project.");
Check(new ConversionPrecheckService().Check(Options(input, missingTlsd)).Warnings.Any(message => message.Contains("TLSD")),
    "Metadata read problems must be visible during precheck.");

if (args.Length > 0 && args[0] == "--sample")
{
    var sampleOutput = Path.GetFullPath(args[1]);
    var realTlsd = Path.GetFullPath(args[2]);
    var sampleOptions = new ConversionOptions
    {
        InputFilePath = @"F:\chongqing\3\scan\tlsd.zfs",
        TlsdFilePath = realTlsd,
        OutputFilePath = sampleOutput,
        Vendor = ScannerVendor.ZPlusF
    };
    var service = new PointCloudConversionService(_ => new FixtureScanSource(), () => new NullPointCloudWriter());
    var sample = await service.ConvertAsync(sampleOptions, null, CancellationToken.None);
    Console.WriteLine($"Real TLSD sample: {sample.MetadataDocumentPath}; virtual E57: {sampleOutput}");
}
else if (args.Length > 0)
{
    var actual = ConversionTaskList.Load(args[0]);
    var actualOptions = actual.SelectMany(task => Build(task)).ToArray();
    Check(actualOptions.All(option => option.Vendor != ScannerVendor.Auto), "Every actual scan must retain a known vendor.");
    Check(actual.Zip(File.ReadAllLines(args[0]).Where(line => !string.IsNullOrWhiteSpace(line)))
        .All(pair => pair.First.TlsdFilePath == (pair.Second.Split('\t').ElementAtOrDefault(2) is { Length: > 0 } path ? Path.GetFullPath(path) : null)),
        "Every actual list row must retain its explicit or empty third field.");
    Console.WriteLine($"Actual list: {actual.Count} rows; {actual.Count(task => task.TlsdFilePath is not null)} explicit TLSD paths; {actualOptions.Length} scan options.");
}
Console.WriteLine($"PASS: {passed} TLSD regression checks. Only tiny fixtures were converted; real scan data was not opened or changed.");

sealed class FixtureScanSource : IScanSource
{
    public ScanMetadata Metadata { get; private set; } = new("", ScannerVendor.ZPlusF, 2, 1);
    public void Open(ConversionOptions options, CancellationToken cancellationToken) =>
        Metadata = new(options.InputFilePath, options.Vendor, 2, 1, ScannerModel: "Regression fixture",
            AcquisitionParameters: new(null, null, null, null, 500_000, 5120, null, 97.65625, "500000 / 5120", null, null));
    public IEnumerable<PointBatch> ReadBatches(int scanLinesPerBatch, CancellationToken cancellationToken)
    {
        yield return new PointBatch(0, 2, 2, 0,
            [new() { X = 1, ColumnIndex = 0 }, new() { X = 2, ColumnIndex = 1 }]);
    }
    public void Dispose() { }
}

sealed class FixtureWriter : IPointCloudWriter
{
    private string path = "";
    public void Open(ScanMetadata metadata, ConversionOptions options)
    {
        path = options.OutputFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }
    public void WriteBatch(PointBatch batch, CancellationToken cancellationToken) { }
    public void Complete(ConversionReport report) => File.WriteAllText(path, "Regression fixture; not a real E57.");
    public void Dispose() { }
}
