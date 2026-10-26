using System.Globalization;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using EasyTry.Conversion.Writers;

namespace EasyTry.Conversion.UI;

public partial class PointCloudConversionForm : Form
{
    private readonly ConversionPrecheckService precheckService = new();
    private readonly IPointCloudConversionService conversionService;
    private readonly List<string> batchInputPaths = [];
    private readonly List<BatchConversionTask> taskListItems = [];
    private CancellationTokenSource? cancellationTokenSource;
    private string? taskListPath;
    private bool stoppedForLowDisk;
    private string? lowDiskMessage;
    private DateTime lastDiskSpaceCheck = DateTime.MinValue;
    private bool conversionRunning;

    public PointCloudConversionForm()
        : this(new PointCloudConversionService())
    {
    }

    internal PointCloudConversionForm(IPointCloudConversionService conversionService)
    {
        this.conversionService = conversionService ?? throw new ArgumentNullException(nameof(conversionService));
        InitializeComponent();
        InitializeSelections();
        AppendLog("全文件流式转换与真实 E57 写入功能已加载。", LogLevel.Information);
    }

    private void InitializeSelections()
    {
        vendorComboBox.Items.Add(new SelectionItem<ScannerVendor>("自动识别", ScannerVendor.Auto));
        vendorComboBox.Items.Add(new SelectionItem<ScannerVendor>("Z+F（.zfs）", ScannerVendor.ZPlusF));
        vendorComboBox.Items.Add(new SelectionItem<ScannerVendor>("FARO（.fls 文件夹）", ScannerVendor.Faro));
        vendorComboBox.SelectedIndex = 0;

        mileageDirectionComboBox.Items.Add(new SelectionItem<MileageDirection>("里程升序", MileageDirection.Ascending));
        mileageDirectionComboBox.Items.Add(new SelectionItem<MileageDirection>("里程降序", MileageDirection.Descending));
        mileageDirectionComboBox.SelectedIndex = 0;

        scanDirectionComboBox.Items.Add(new SelectionItem<MobileScanDirection>("移动 2D（正向）", MobileScanDirection.Forward));
        scanDirectionComboBox.Items.Add(new SelectionItem<MobileScanDirection>("移动 2D（反向）", MobileScanDirection.Reverse));
        scanDirectionComboBox.SelectedIndex = 0;

        outputModeComboBox.Items.Add(new SelectionItem<E57OutputMode>(
            "单个 E57（兼容模式）",
            E57OutputMode.SingleFile));
        outputModeComboBox.Items.Add(new SelectionItem<E57OutputMode>(
            "连续分段 E57 数据集（推荐大数据）",
            E57OutputMode.SegmentedDataset));
        outputModeComboBox.Items.Add(new SelectionItem<E57OutputMode>(
            "连续 FARO 合并：按列阈值自动单文件 / 分段",
            E57OutputMode.AutoByScanColumnThreshold));
        outputModeComboBox.SelectedIndex = 0;
        segmentColumnsNumeric.Enabled = false;

        inputFileTextBox.Multiline = true;
        inputFileTextBox.ReadOnly = true;
        inputFileTextBox.ScrollBars = ScrollBars.Vertical;
        inputFileTextBox.Height = 76;
        browseInputButton.Click -= browseInputButton_Click;
        browseInputButton.Click += browseBatchInputButton_Click;
        browseInputButton.Text = "批量选择…";
        browseOutputButton.Click -= browseOutputButton_Click;
        browseOutputButton.Click += browseBatchOutputButton_Click;
        browseOutputButton.Text = "选择目录…";
        precheckButton.Click -= precheckButton_Click;
        precheckButton.Click += batchPrecheckButton_Click;
        precheckButton.Text = "批量预检查";
        startConversionButton.Click -= startConversionButton_Click;
        startConversionButton.Click += startBatchConversionButton_Click;
        startConversionButton.Text = "开始批量转换";
        inputFileTextBox.PlaceholderText = "Z+F 可多选 .zfs；FARO 请选择包含多个 .fls 扫描包的目录";
        outputFileTextBox.PlaceholderText = "批量 E57 输出目录";
        Text = "点云批量转换（Z+F / FARO → 通用 E57）";
        inputGroup.Text = "批量输入与输出目录";
        var inputLayout = inputGroup.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault();
        if (inputLayout is not null)
        {
            inputLayout.GetControlFromPosition(0, 0)!.Text = "批量原始扫描";
            inputLayout.GetControlFromPosition(0, 1)!.Text = "设备类型";
            inputLayout.GetControlFromPosition(0, 2)!.Text = "E57 输出目录";
        }
    }

    private void browseInputButton_Click(object? sender, EventArgs e)
    {
        var selectedVendor = GetSelectedValue(vendorComboBox, ScannerVendor.Auto);
        var inputPath = selectedVendor switch
        {
            ScannerVendor.Faro => SelectFaroScanFolder(),
            ScannerVendor.ZPlusF => SelectZfsScanFile(),
            _ => SelectInputByType()
        };

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        inputFileTextBox.Text = inputPath;
        AutoDetectVendorAndOutput(inputPath);
        statusLabel.Text = "已选择输入路径，建议先执行预检查。";
        AppendLog($"选择输入路径：{inputPath}", LogLevel.Information);
    }

    private string? SelectInputByType()
    {
        var result = MessageBox.Show(
            this,
            "请选择要导入的扫描数据类型：\n\n" +
            "选择“是”：FARO 原始 .fls 扫描文件夹\n" +
            "选择“否”：Z+F .zfs 扫描文件\n" +
            "选择“取消”：返回",
            "选择扫描数据类型",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => SelectFaroScanFolder(),
            DialogResult.No => SelectZfsScanFile(),
            _ => null
        };
    }

    private string? SelectFaroScanFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择完整的 FARO 原始扫描文件夹（文件夹名通常以 .fls 结尾）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var currentPath = inputFileTextBox.Text.Trim();
        if (Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private string? SelectZfsScanFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 Z+F 移动二维扫描文件",
            Filter = "Z+F 扫描文件 (*.zfs)|*.zfs",
            CheckFileExists = true,
            Multiselect = false
        };

        var currentPath = inputFileTextBox.Text.Trim();
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }

        return dialog.ShowDialog(this) == DialogResult.OK
            ? ZfsVolumeSet.GetPrimaryPath(dialog.FileName)
            : null;
    }

    private void browseOutputButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "选择 E57 输出文件",
            Filter = "E57 点云文件 (*.e57)|*.e57",
            AddExtension = true,
            DefaultExt = "e57",
            OverwritePrompt = true,
            FileName = BuildDefaultOutputFileName(inputFileTextBox.Text)
        };

        var currentOutput = outputFileTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(currentOutput))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentOutput);
            dialog.FileName = Path.GetFileName(currentOutput);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFileTextBox.Text = dialog.FileName;
            AppendLog($"设置输出文件：{dialog.FileName}", LogLevel.Information);
        }
    }

    private void vendorComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (vendorComboBox.SelectedItem is SelectionItem<ScannerVendor> item)
        {
            AppendLog($"设备类型设置为：{item.DisplayName}", LogLevel.Information);
        }
    }

    private void outputModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var outputMode = GetSelectedValue(
            outputModeComboBox,
            E57OutputMode.SingleFile);
        var usesColumnThreshold = outputMode != E57OutputMode.SingleFile;
        segmentColumnsNumeric.Enabled = usesColumnThreshold && !conversionRunning;
        segmentColumnsLabel.Text = outputMode == E57OutputMode.AutoByScanColumnThreshold
            ? "单文件 / 每段列阈值"
            : "每段扫描列数";
        if (outputMode == E57OutputMode.SegmentedDataset)
        {
            AppendLog(
                "已选择连续分段 E57：各段 columnIndex 从 0 开始，cartesianY 保持有效全局扫描列号。",
                LogLevel.Information);
        }
        else if (outputMode == E57OutputMode.AutoByScanColumnThreshold)
        {
            AppendLog(
                "已选择连续 FARO 合并：按数字编号连接；总列数不超过阈值时输出单个 E57，超过时直接流式分段。",
                LogLevel.Information);
        }
    }

    private void precheckButton_Click(object? sender, EventArgs e) =>
        RunPrecheck(showDialog: true);

    private async void startConversionButton_Click(object? sender, EventArgs e)
    {
        if (!RunPrecheck(showDialog: false))
        {
            return;
        }

        var options = BuildOptions();
        if (File.Exists(options.OutputFilePath))
        {
            var overwriteResult = MessageBox.Show(
                this,
                "输出 E57 已存在。是否在转换成功并通过校验后替换该文件？",
                "确认覆盖",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (overwriteResult != DialogResult.Yes)
            {
                return;
            }
        }

        cancellationTokenSource = new CancellationTokenSource();
        SetRunningState(true);

        var progress = new Progress<ConversionProgress>(UpdateProgress);
        try
        {
            AppendLog("请求开始转换。", LogLevel.Information);
            var report = await conversionService.ConvertAsync(
                options,
                progress,
                cancellationTokenSource.Token);
            statusLabel.Text = "E57 转换和回读校验完成";
            conversionProgressBar.Value = 100;
            UpdateCompletedStatistics(report);
            AppendReport(report);
            MessageBox.Show(this, $"E57 已生成并通过回读校验：{options.OutputFilePath}",
                "转换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "转换已取消";
            AppendLog("转换已由用户取消。", LogLevel.Warning);
        }
        catch (NotSupportedException ex)
        {
            statusLabel.Text = "当前功能不可用";
            AppendLog(ex.Message, LogLevel.Warning);
            MessageBox.Show(this, ex.Message, "功能说明",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "转换失败";
            AppendLog($"转换失败：{ex.Message}", LogLevel.Error);
            MessageBox.Show(this, ex.Message, "转换失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRunningState(false);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private void browseBatchInputButton_Click(object? sender, EventArgs e)
    {
        var vendor = GetSelectedValue(vendorComboBox, ScannerVendor.Auto);
        if (vendor == ScannerVendor.Auto)
        {
            var choice = MessageBox.Show(
                this,
                "选择“是”批量导入 FARO .fls 扫描包；选择“否”批量导入 Z+F .zfs 文件。",
                "选择批量输入类型",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel)
            {
                return;
            }

            vendor = choice == DialogResult.Yes
                ? ScannerVendor.Faro
                : ScannerVendor.ZPlusF;
            vendorComboBox.SelectedIndex = vendor == ScannerVendor.ZPlusF ? 1 : 2;
        }

        IReadOnlyList<string> selectedPaths = vendor switch
        {
            ScannerVendor.ZPlusF => SelectZfsScanFiles(),
            ScannerVendor.Faro => SelectFaroInputs(),
            _ => []
        };
        if (selectedPaths.Count == 0)
        {
            return;
        }

        ClearTaskListMode();
        batchInputPaths.Clear();
        batchInputPaths.AddRange(selectedPaths);
        inputFileTextBox.Lines = batchInputPaths.ToArray();
        if (string.IsNullOrWhiteSpace(outputFileTextBox.Text))
        {
            outputFileTextBox.Text = GetDefaultBatchOutputDirectory(
                batchInputPaths[0]);
        }

        statusLabel.Text = $"已选择 {batchInputPaths.Count} 个{ConversionPrecheckService.GetVendorDisplayName(vendor)}输入";
        AppendLog(statusLabel.Text, LogLevel.Information);
    }

    private IReadOnlyList<string> SelectZfsScanFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "批量选择 Z+F 原始扫描文件",
            Filter = "Z+F 扫描文件 (*.zfs)|*.zfs",
            CheckFileExists = true,
            Multiselect = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? BatchInputDiscovery.NormalizeZfsFiles(dialog.FileNames)
            : [];
    }

    private IReadOnlyList<string> SelectFaroInputs()
    {
        var choice = MessageBox.Show(
            this,
            "选择“是”可多选独立 .fls 文件；选择“否”可选择一个 .fls 扫描包或包含多个扫描包的目录。",
            "选择 FARO 批量导入方式",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        return choice switch
        {
            DialogResult.Yes => SelectFaroScanFiles(),
            DialogResult.No => SelectFaroBatchFolder(),
            _ => []
        };
    }

    private IReadOnlyList<string> SelectFaroScanFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "批量选择 FARO 原始扫描文件",
            Filter = "FARO 扫描文件 (*.fls)|*.fls",
            CheckFileExists = true,
            Multiselect = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? BatchInputDiscovery.NormalizeFaroFiles(dialog.FileNames)
            : [];
    }

    private IReadOnlyList<string> SelectFaroBatchFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择一个 .fls 扫描包，或选择其直接包含多个 .fls 扫描包的批量目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return [];
        }

        var paths = BatchInputDiscovery.DiscoverFaroScanFolders(
            dialog.SelectedPath);
        if (paths.Count == 0)
        {
            MessageBox.Show(
                this,
                "所选目录不是完整 FARO .fls 扫描包，且其第一层子目录中没有完整 .fls 扫描包。",
                "未发现 FARO 扫描",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return [];
        }

        using var selectionDialog = new BatchInputSelectionDialog(
            "选择 FARO 批量输入",
            paths);
        return selectionDialog.ShowDialog(this) == DialogResult.OK
            ? selectionDialog.SelectedPaths
            : [];
    }

    private void browseBatchOutputButton_Click(object? sender, EventArgs e)
    {
        if (taskListItems.Count > 0)
        {
            MessageBox.Show(
                this,
                "任务清单模式下，输出目录由清单第二列指定。",
                "任务清单模式",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择所有通用 E57 文件的输出目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        var current = outputFileTextBox.Text.Trim();
        if (Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFileTextBox.Text = dialog.SelectedPath;
            AppendLog($"批量输出目录：{dialog.SelectedPath}", LogLevel.Information);
        }
    }

    private void batchPrecheckButton_Click(object? sender, EventArgs e) =>
        RunBatchPrecheck(showDialog: true, out _);

    private void importTaskListButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 EasyTry 自动转换任务清单",
            Filter = "文本任务清单 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var availableTasks = ConversionTaskList.Load(dialog.FileName);
            using var selectionDialog = new TaskListSelectionDialog(availableTasks);
            if (selectionDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            var tasks = selectionDialog.SelectedTasks;
            taskListItems.Clear();
            taskListItems.AddRange(tasks);
            taskListPath = Path.GetFullPath(dialog.FileName);
            batchInputPaths.Clear();
            batchInputPaths.AddRange(tasks.SelectMany(item => item.InputPaths));
            vendorComboBox.SelectedIndex = 0;
            inputFileTextBox.Lines = tasks
                .Select(TaskListDisplayText.FormatTaskPath)
                .ToArray();
            outputFileTextBox.Text = $"由任务清单指定（{tasks.Count} 个输出位置）";
            outputFileTextBox.ReadOnly = true;
            browseOutputButton.Enabled = false;
            statusLabel.Text = $"已载入任务清单：{tasks.Count} 组，{batchInputPaths.Count} 个扫描任务（自动识别 FARO / Z+F）";
            AppendLog($"已载入任务清单：{taskListPath}", LogLevel.Success);
            AppendLog(statusLabel.Text, LogLevel.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"任务清单载入失败：{ex.Message}", LogLevel.Error);
            MessageBox.Show(this, ex.Message, "任务清单无效",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearTaskListMode()
    {
        taskListItems.Clear();
        taskListPath = null;
        outputFileTextBox.ReadOnly = false;
        browseOutputButton.Enabled = true;
    }

    private async void startBatchConversionButton_Click(object? sender, EventArgs e)
    {
        if (!RunBatchPrecheck(showDialog: false, out var options))
        {
            return;
        }

        var existingOutputs = options.Count(ConversionOutputLayout.OutputExists);
        if (existingOutputs > 0)
        {
            var overwrite = MessageBox.Show(
                this,
                $"有 {existingOutputs} 个输出已经存在。单文件模式会在校验成功后替换；" +
                "分段模式会校验参数并复用已完成分段。是否继续？",
                "确认覆盖或续作",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        foreach (var directory in options
                     .Select(item => Path.GetDirectoryName(item.OutputFilePath))
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(directory!);
        }
        cancellationTokenSource = new CancellationTokenSource();
        stoppedForLowDisk = false;
        lowDiskMessage = null;
        lastDiskSpaceCheck = DateTime.MinValue;
        SetRunningState(true);
        var succeeded = 0;
        var failures = new List<string>();
        var completedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < options.Count; index++)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                var item = options[index];
                if (!DiskSpaceGuard.HasSafeFreeSpace(item.OutputFilePath, out var freeBytes))
                {
                    stoppedForLowDisk = true;
                    lowDiskMessage =
                        $"输出盘剩余空间 {DiskSpaceGuard.FormatBytes(freeBytes)}，" +
                        $"低于安全余量 {DiskSpaceGuard.FormatBytes(DiskSpaceGuard.MinimumFreeBytes)}。";
                    AppendLog(lowDiskMessage + " 自动流程已停止。", LogLevel.Error);
                    break;
                }

                var itemDisplayName = item.ContinuousInputPaths.Count > 0
                    ? ContinuousFaroInputSet.BuildDatasetName(item.ContinuousInputPaths)
                    : Path.GetFileName(item.InputFilePath);
                AppendLog($"[{index + 1}/{options.Count}] 开始转换：{item.InputFilePath}", LogLevel.Information);
                var progress = new Progress<ConversionProgress>(value =>
                {
                    MonitorDiskSpace(item);
                    UpdateBatchProgress(value, index, options.Count, itemDisplayName);
                });
                try
                {
                    var report = await conversionService.ConvertAsync(
                        item, progress, cancellationTokenSource.Token);
                    succeeded++;
                    completedOutputs.Add(Path.GetFullPath(item.OutputFilePath));
                    UpdateCompletedStatistics(report);
                    AppendBatchReport(report, item);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.InputFilePath}：{ex.Message}");
                    AppendLog($"[{index + 1}/{options.Count}] 转换失败，继续下一项：{ex.Message}", LogLevel.Error);
                }
            }

            statusLabel.Text = stoppedForLowDisk
                ? $"空间不足，已停止：成功 {succeeded}，失败 {failures.Count}"
                : $"批量转换完成：成功 {succeeded}，失败 {failures.Count}";
            if (!stoppedForLowDisk)
            {
                conversionProgressBar.Value = 100;
            }
            var summary = statusLabel.Text + $"\n输出位置：{outputFileTextBox.Text.Trim()}";
            if (stoppedForLowDisk)
            {
                summary += $"\n\n{lowDiskMessage}\n释放空间后可重新选择未完成的任务。";
            }
            if (failures.Count > 0)
            {
                summary += "\n\n失败原因：\n" + string.Join("\n", failures);
            }
            AppendLog(statusLabel.Text, stoppedForLowDisk || failures.Count > 0 ? LogLevel.Warning : LogLevel.Success);
            MessageBox.Show(this, summary, "批量转换结果", MessageBoxButtons.OK,
                stoppedForLowDisk || failures.Count > 0
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = stoppedForLowDisk
                ? $"空间不足，已安全停止；已成功完成 {succeeded} 项"
                : $"批量转换已取消；已成功完成 {succeeded} 项";
            var summary = statusLabel.Text;
            if (stoppedForLowDisk)
            {
                summary += $"\n\n{lowDiskMessage}\n释放空间后可重新选择未完成的任务。";
            }
            if (failures.Count > 0)
            {
                summary += "\n\n失败原因：\n" + string.Join("\n", failures);
            }
            AppendLog(summary, LogLevel.Warning);
            MessageBox.Show(this, summary, "批量转换已停止", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            TryRemoveCompletedTaskListItems(completedOutputs);
            SetRunningState(false);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private void MonitorDiskSpace(ConversionOptions item)
    {
        if (stoppedForLowDisk ||
            DateTime.UtcNow - lastDiskSpaceCheck < TimeSpan.FromSeconds(2))
        {
            return;
        }

        lastDiskSpaceCheck = DateTime.UtcNow;
        if (DiskSpaceGuard.HasSafeFreeSpace(item.OutputFilePath, out var freeBytes))
        {
            return;
        }

        stoppedForLowDisk = true;
        lowDiskMessage =
            $"输出盘剩余空间 {DiskSpaceGuard.FormatBytes(freeBytes)}，" +
            $"低于安全余量 {DiskSpaceGuard.FormatBytes(DiskSpaceGuard.MinimumFreeBytes)}。";
        statusLabel.Text = lowDiskMessage + " 正在安全停止当前转换。";
        AppendLog(statusLabel.Text, LogLevel.Error);
        cancellationTokenSource?.Cancel();
    }

    private bool RunBatchPrecheck(
        bool showDialog,
        out IReadOnlyList<ConversionOptions> options)
    {
        try
        {
            options = BuildBatchOptions();
        }
        catch (Exception ex)
        {
            options = [];
            statusLabel.Text = "批量参数错误";
            AppendLog($"批量参数错误：{ex.Message}", LogLevel.Error);
            MessageBox.Show(this, ex.Message, "批量参数错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var nativeError = NativeE57Writer.GetAvailabilityError();
        if (nativeError is not null)
        {
            errors.Add(nativeError);
            AppendLog(nativeError, LogLevel.Error);
        }
        foreach (var item in options)
        {
            var result = precheckService.Check(item);
            var itemName = item.ContinuousInputPaths.Count > 0
                ? ContinuousFaroInputSet.BuildDatasetName(item.ContinuousInputPaths)
                : Path.GetFileName(item.InputFilePath);
            foreach (var message in result.Information)
            {
                AppendLog($"[{itemName}] {message}", LogLevel.Information);
            }
            foreach (var message in result.Warnings)
            {
                AppendLog($"[{itemName}] {message}", LogLevel.Warning);
            }
            foreach (var message in result.Errors)
            {
                AppendLog($"[{itemName}] {message}", LogLevel.Error);
            }
            warnings.AddRange(result.Warnings.Select(message => $"[{itemName}] {message}"));
            errors.AddRange(result.Errors.Select(message => $"[{itemName}] {message}"));
        }

        var valid = errors.Count == 0;
        statusLabel.Text = valid
            ? $"批量预检查通过：{options.Count} 项，警告 {warnings.Count} 项"
            : $"批量预检查未通过：错误 {errors.Count} 项";
        AppendLog(statusLabel.Text, valid ? LogLevel.Success : LogLevel.Error);
        if (showDialog || !valid)
        {
            var details = string.Join("\n", errors.Concat(warnings));
            MessageBox.Show(this,
                statusLabel.Text + (details.Length > 0 ? "\n\n" + details : ""),
                valid ? "批量预检查通过" : "批量预检查未通过",
                MessageBoxButtons.OK,
                valid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        return valid;
    }

    private IReadOnlyList<ConversionOptions> BuildBatchOptions()
    {
        if (taskListItems.Count > 0)
        {
            return BuildTaskListOptions();
        }

        var selectedVendor = GetSelectedValue(
            vendorComboBox,
            ScannerVendor.Auto);
        var vendor = BatchInputDiscovery.ValidateSingleVendor(
            batchInputPaths,
            selectedVendor);
        var normalizedInputPaths = vendor == ScannerVendor.ZPlusF
            ? BatchInputDiscovery.NormalizeZfsFiles(batchInputPaths)
            : BatchInputDiscovery.NormalizeFaroFiles(batchInputPaths);
        var outputDirectory = outputFileTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("请选择批量 E57 输出目录。");
        }

        var outputMode = GetSelectedValue(
            outputModeComboBox,
            E57OutputMode.SingleFile);
        if (outputMode == E57OutputMode.AutoByScanColumnThreshold)
        {
            if (vendor != ScannerVendor.Faro)
            {
                throw new InvalidOperationException(
                    "连续合并自动阈值模式当前只支持 FARO .fls 文件或扫描包。");
            }

            var continuousInputs = ContinuousFaroInputSet.NormalizeAndValidate(
                normalizedInputPaths);
            return
            [
                new ConversionOptions
                {
                    InputFilePath = continuousInputs[0],
                    ContinuousInputPaths = continuousInputs,
                    OutputFilePath = ContinuousFaroInputSet.BuildOutputFilePath(
                        outputDirectory,
                        continuousInputs),
                    Vendor = ScannerVendor.Faro,
                    MileageDirection = GetSelectedValue(
                        mileageDirectionComboBox,
                        MileageDirection.Ascending),
                    ScanDirection = GetSelectedValue(
                        scanDirectionComboBox,
                        MobileScanDirection.Forward),
                    PreserveRawIntensity = true,
                    PreserveProcessedIntensity = true,
                    OutputMode = outputMode,
                    SegmentScanColumns = decimal.ToInt32(segmentColumnsNumeric.Value)
                }
            ];
        }

        var options = normalizedInputPaths.Select(inputPath => new ConversionOptions
        {
            InputFilePath = inputPath,
            OutputFilePath = BatchInputDiscovery.BuildOutputFilePath(
                outputDirectory,
                inputPath),
            Vendor = vendor,
            MileageDirection = GetSelectedValue(
                mileageDirectionComboBox,
                MileageDirection.Ascending),
            ScanDirection = GetSelectedValue(
                scanDirectionComboBox,
                MobileScanDirection.Forward),
            PreserveRawIntensity = true,
            PreserveProcessedIntensity = true,
            OutputMode = outputMode,
            SegmentScanColumns = decimal.ToInt32(segmentColumnsNumeric.Value)
        }).ToArray();

        var collisions = options
            .GroupBy(
                item => item.OutputFilePath,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (collisions.Length > 0)
        {
            throw new InvalidOperationException(
                "批量输入中存在同名扫描，生成的 E57 文件名会冲突：" +
                string.Join("；", collisions));
        }

        return options;
    }

    private IReadOnlyList<ConversionOptions> BuildTaskListOptions()
    {
        var options = new List<ConversionOptions>();
        foreach (var task in taskListItems)
        {
            options.AddRange(BuildTaskOptions(task));
        }

        var collisions = options
            .GroupBy(item => item.OutputFilePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (collisions.Length > 0)
        {
            throw new InvalidOperationException(
                "任务清单会生成同名输出，请调整输出目录：" +
                string.Join("；", collisions));
        }

        return options;
    }

    private void TryRemoveCompletedTaskListItems(IReadOnlySet<string> completedOutputs)
    {
        if (!removeCompletedTaskItemsCheckBox.Checked ||
            string.IsNullOrWhiteSpace(taskListPath) || taskListItems.Count == 0)
        {
            return;
        }

        try
        {
            var completedTasks = taskListItems
                .Where(task => BuildTaskOptions(task).All(option =>
                    completedOutputs.Contains(Path.GetFullPath(option.OutputFilePath))))
                .ToArray();
            if (completedTasks.Length == 0) { return; }

            var removed = ConversionTaskList.RemoveTasks(taskListPath, completedTasks);
            taskListItems.RemoveAll(task => completedTasks.Contains(task));
            inputFileTextBox.Lines = taskListItems
                .Select(TaskListDisplayText.FormatTaskPath)
                .ToArray();
            statusLabel.Text += $"；已从清单移除 {removed} 个完成项";
            AppendLog($"已从任务清单移除 {removed} 个完成项。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            AppendLog($"从任务清单移除完成项失败：{ex.Message}", LogLevel.Error);
            MessageBox.Show(this, ex.Message, "从任务清单移除完成项失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private IReadOnlyList<ConversionOptions> BuildTaskOptions(BatchConversionTask task)
    {
        var outputMode = GetSelectedValue(outputModeComboBox, E57OutputMode.SingleFile);
        var mileageDirection = GetSelectedValue(mileageDirectionComboBox, MileageDirection.Ascending);
        var scanDirection = GetSelectedValue(scanDirectionComboBox, MobileScanDirection.Forward);
        var segmentColumns = decimal.ToInt32(segmentColumnsNumeric.Value);
        return ConversionTaskList.BuildOptions(
            task, outputMode, mileageDirection, scanDirection, segmentColumns);
    }

    private void UpdateBatchProgress(
        ConversionProgress progress,
        int completedItemCount,
        int totalItemCount,
        string inputName)
    {
        var overall = (completedItemCount * 100 + progress.Percent) /
                      Math.Max(1, totalItemCount);
        conversionProgressBar.Value = Math.Clamp(overall, 0, 100);
        var message = $"[{completedItemCount + 1}/{totalItemCount}] {inputName}：{progress.Stage}";
        if (statusLabel.Text != message)
        {
            AppendLog(message, LogLevel.Information);
        }
        statusLabel.Text = message;
        statisticsLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            "当前文件扫描线：{0:N0} / {1:N0}；有效点：{2:N0}；跳过无效点：{3:N0}",
            progress.ProcessedScanLines,
            progress.TotalScanLines,
            progress.ValidPointCount,
            progress.InvalidPointCount);
    }

    private static string GetDefaultBatchOutputDirectory(string inputPath)
    {
        var normalized = inputPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(normalized) ?? Environment.CurrentDirectory;
    }

    private void cancelButton_Click(object? sender, EventArgs e)
    {
        if (!conversionRunning || cancellationTokenSource is null)
        {
            return;
        }

        cancelButton.Enabled = false;
        statusLabel.Text = "正在请求取消…";
        AppendLog("已发送取消请求，等待当前批次安全结束。", LogLevel.Warning);
        cancellationTokenSource.Cancel();
    }

    private void PointCloudConversionForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!conversionRunning)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "转换任务正在运行。是否取消任务并关闭页面？",
            "确认关闭",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        cancellationTokenSource?.Cancel();
    }

    private bool RunPrecheck(bool showDialog)
    {
        ConversionOptions options;
        try
        {
            options = BuildOptions();
        }
        catch (Exception ex)
        {
            statusLabel.Text = "参数错误";
            AppendLog($"参数读取失败：{ex.Message}", LogLevel.Error);
            MessageBox.Show(this, ex.Message, "参数错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        AppendLog("开始执行转换预检查…", LogLevel.Information);
        var result = precheckService.Check(options);
        var e57Error = NativeE57Writer.GetAvailabilityError();
        if (e57Error is not null)
        {
            result.Errors.Add(e57Error);
        }
        foreach (var message in result.Information)
        {
            AppendLog(message, LogLevel.Information);
        }
        foreach (var message in result.Warnings)
        {
            AppendLog(message, LogLevel.Warning);
        }
        foreach (var message in result.Errors)
        {
            AppendLog(message, LogLevel.Error);
        }
        statusLabel.Text = result.IsValid
            ? $"基础预检查通过，警告 {result.Warnings.Count} 项"
            : $"基础预检查未通过，错误 {result.Errors.Count} 项";
        AppendLog(statusLabel.Text, result.IsValid ? LogLevel.Success : LogLevel.Error);
        if (showDialog || !result.IsValid)
        {
            var details = string.Join("\n", result.Errors.Concat(result.Warnings));
            MessageBox.Show(this,
                statusLabel.Text + (details.Length > 0 ? "\n\n" + details : ""),
                result.IsValid ? "预检查通过" : "预检查未通过",
                MessageBoxButtons.OK,
                result.IsValid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        return result.IsValid;
    }

    private ConversionOptions BuildOptions() => new()
    {
        InputFilePath = inputFileTextBox.Text.Trim(),
        OutputFilePath = outputFileTextBox.Text.Trim(),
        Vendor = GetSelectedValue(vendorComboBox, ScannerVendor.Auto),
        MileageDirection = GetSelectedValue(mileageDirectionComboBox, MileageDirection.Ascending),
        ScanDirection = GetSelectedValue(scanDirectionComboBox, MobileScanDirection.Forward),
        PreserveRawIntensity = true,
        PreserveProcessedIntensity = true,
        OutputMode = GetSelectedValue(
            outputModeComboBox,
            E57OutputMode.SingleFile),
        SegmentScanColumns = decimal.ToInt32(segmentColumnsNumeric.Value)
    };

    private void AutoDetectVendorAndOutput(string inputPath)
    {
        vendorComboBox.SelectedIndex = ConversionPrecheckService.DetectVendor(inputPath) switch
        {
            ScannerVendor.ZPlusF => 1,
            ScannerVendor.Faro => 2,
            _ => 0
        };
        outputFileTextBox.Text = Path.ChangeExtension(inputPath, ".e57");
    }

    private static string BuildDefaultOutputFileName(string inputPath) =>
        string.IsNullOrWhiteSpace(inputPath)
            ? "converted.e57"
            : Path.GetFileName(Path.ChangeExtension(inputPath, ".e57"));

    private void SetRunningState(bool running)
    {
        conversionRunning = running;
        inputGroup.Enabled = !running;
        settingsGroup.Enabled = !running;
        importTaskListButton.Enabled = !running;
        removeCompletedTaskItemsCheckBox.Enabled = !running;
        precheckButton.Enabled = !running;
        startConversionButton.Enabled = !running;
        cancelButton.Enabled = running;
        segmentColumnsNumeric.Enabled = !running && GetSelectedValue(
            outputModeComboBox,
            E57OutputMode.SingleFile) != E57OutputMode.SingleFile;
        if (running)
        {
            conversionProgressBar.Value = 0;
            statusLabel.Text = "正在准备转换…";
            AppendLog(statusLabel.Text, LogLevel.Information);
        }
    }

    private void UpdateProgress(ConversionProgress progress)
    {
        conversionProgressBar.Value = Math.Clamp(progress.Percent, 0, 100);
        if (statusLabel.Text != progress.Stage)
        {
            AppendLog(progress.Stage, LogLevel.Information);
        }
        statusLabel.Text = progress.Stage;
        statisticsLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            "扫描线：{0:N0} / {1:N0}；有效点：{2:N0}；跳过无效点：{3:N0}",
            progress.ProcessedScanLines,
            progress.TotalScanLines,
            progress.ValidPointCount,
            progress.InvalidPointCount);
    }

    private void UpdateCompletedStatistics(ConversionReport report)
    {
        statisticsLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            "已完成扫描线：{0:N0}；有效点：{1:N0}；跳过无效点：{2:N0}；耗时：{3:N2} 秒",
            report.ProcessedScanLines, report.ValidPointCount,
            report.InvalidPointCount, report.Elapsed.TotalSeconds);
    }

    private void AppendBatchReport(
        ConversionReport report,
        ConversionOptions options)
    {
        var outputPath = ConversionOutputLayout.GetDisplayPath(
            options,
            report.ResolvedOutputMode);
        AppendLog(
            report.ResolvedOutputMode == E57OutputMode.SegmentedDataset
                ? $"连续分段 E57 数据集已生成：{outputPath}"
                : $"E57 已生成并通过回读校验：{outputPath}",
            LogLevel.Success);
        AppendLog(
            $"扫描结构：{report.Metadata.ScanColumns:N0} 条扫描线 × {report.Metadata.ScanRows:N0} 行；" +
            $"有效点：{report.ValidPointCount:N0}；无效点：{report.InvalidPointCount:N0}；" +
            $"耗时：{report.Elapsed.TotalSeconds:N2} 秒。",
            LogLevel.Information);
        if (report.Metadata.SourceParts is { Count: > 0 } sourceParts)
        {
            AppendLog(
                $"连续 FARO 来源：{sourceParts.Count:N0} 个扫描包；全局列索引 0–{report.Metadata.ScanColumns - 1:N0}。",
                LogLevel.Information);
            foreach (var sourcePart in sourceParts)
            {
                AppendLog(
                    $"  [{sourcePart.SourceIndex + 1}/{sourceParts.Count}] " +
                    $"{Path.GetFileName(sourcePart.SourcePath)}：" +
                    $"局部 {sourcePart.LocalScanColumns:N0} 列，" +
                    $"全局列 {sourcePart.EffectiveGlobalColumnStart:N0}–{sourcePart.EffectiveGlobalColumnEnd:N0}",
                    LogLevel.Information);
            }
        }
        AppendScannerMetadata(report.Metadata);
        AppendMetadataDocument(report.MetadataDocumentPath);
    }

    private void AppendReport(ConversionReport report)
    {
        AppendLog($"真实 E57 已生成并通过回读校验：{outputFileTextBox.Text.Trim()}", LogLevel.Success);
        AppendLog($"扫描结构：{report.Metadata.ScanColumns:N0} 条扫描线 × {report.Metadata.ScanRows:N0} 行。", LogLevel.Information);
        AppendLog($"有效点：{report.ValidPointCount:N0}；无效点：{report.InvalidPointCount:N0}。", LogLevel.Information);
        AppendLog($"X 范围：{report.XRange}；Y 范围：{report.YRange}；Z 范围：{report.ZRange}。", LogLevel.Information);
        AppendLog($"原始强度范围：{report.RawIntensityRange}。", LogLevel.Information);
        AppendLog($"SDK 处理强度范围：{report.ProcessedIntensityRange}。", LogLevel.Information);
        AppendLog($"耗时：{report.Elapsed.TotalSeconds:N2} 秒。", LogLevel.Information);
        AppendScannerMetadata(report.Metadata);
        AppendMetadataDocument(report.MetadataDocumentPath);
    }

    private void AppendScannerMetadata(ScanMetadata metadata)
    {
        var details = new List<string>
        {
            $"厂商={ConversionPrecheckService.GetVendorDisplayName(metadata.Vendor)}"
        };
        AddMetadataDetail(details, "型号", metadata.ScannerModel);
        AddMetadataDetail(details, "序列号", metadata.ScannerSerialNumber);
        AddMetadataDetail(details, "固件", metadata.ScannerFirmware);
        AddMetadataDetail(
            details,
            "采集时间",
            metadata.AcquisitionDate?.ToString("yyyy-MM-dd HH:mm:ss"));
        AddMetadataDetail(details, "项目", metadata.ProjectName);
        AddMetadataDetail(details, "地点", metadata.Location);
        AddMetadataDetail(details, "分辨率", metadata.AcquisitionParameters?.Resolution);
        AddMetadataDetail(details, "质量", metadata.AcquisitionParameters?.Quality);
        AddMetadataDetail(
            details,
            "每秒圈数",
            metadata.AcquisitionParameters?.CalculatedRevolutionsPerSecond is > 0
                ? $"{metadata.AcquisitionParameters.CalculatedRevolutionsPerSecond.Value:0.######}"
                : null);
        AppendLog($"扫描仪元数据：{string.Join("；", details)}。", LogLevel.Information);
    }

    private void AppendMetadataDocument(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            AppendLog($"扫描说明已生成：{path}", LogLevel.Success);
        }
    }

    private static void AddMetadataDetail(
        ICollection<string> details,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add($"{name}={value.Trim()}");
        }
    }

    private void AppendLog(string message, LogLevel level)
    {
        if (logTextBox.IsDisposed)
        {
            return;
        }

        var prefix = level switch
        {
            LogLevel.Success => "PASS",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            _ => "INFO"
        };

        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [{prefix}] {message}{Environment.NewLine}");
        logTextBox.SelectionStart = logTextBox.TextLength;
        logTextBox.ScrollToCaret();
    }

    private static T GetSelectedValue<T>(ComboBox comboBox, T defaultValue) =>
        comboBox.SelectedItem is SelectionItem<T> item ? item.Value : defaultValue;

    private sealed record SelectionItem<T>(string DisplayName, T Value)
    {
        public override string ToString() => DisplayName;
    }

    private enum LogLevel
    {
        Information,
        Success,
        Warning,
        Error
    }
}
