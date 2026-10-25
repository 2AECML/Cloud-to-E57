#nullable enable
using EasyTry.Conversion.Services;

namespace EasyTry.Conversion.UI;

partial class PointCloudConversionForm
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private GroupBox inputGroup = null!;
    private TextBox inputFileTextBox = null!;
    private Button browseInputButton = null!;
    private ComboBox vendorComboBox = null!;
    private TextBox outputFileTextBox = null!;
    private Button browseOutputButton = null!;
    private GroupBox settingsGroup = null!;
    private ComboBox mileageDirectionComboBox = null!;
    private ComboBox scanDirectionComboBox = null!;
    private ComboBox outputModeComboBox = null!;
    private Label segmentColumnsLabel = null!;
    private NumericUpDown segmentColumnsNumeric = null!;
    private NumericUpDown parallelTasksNumeric = null!;
    private GroupBox progressGroup = null!;
    private ProgressBar conversionProgressBar = null!;
    private Label statusLabel = null!;
    private Label statisticsLabel = null!;
    private Button precheckButton = null!;
    private Button importTaskListButton = null!;
    private CheckBox removeCompletedTaskItemsCheckBox = null!;
    private Button startConversionButton = null!;
    private Button cancelButton = null!;
    private RichTextBox logTextBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            cancellationTokenSource?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        inputGroup = new GroupBox();
        settingsGroup = new GroupBox();
        progressGroup = new GroupBox();
        logTextBox = new RichTextBox();

        SuspendLayout();
        ConfigureRootLayout();
        ConfigureInputGroup();
        ConfigureSettingsGroup();
        ConfigureProgressGroup();
        ConfigureActions();
        ConfigureLogGroup();

        AutoScaleDimensions = new SizeF(9F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 800);
        Controls.Add(rootLayout);
        MinimumSize = new Size(820, 840);
        Name = "PointCloudConversionForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "点云转换（Z+F / FARO → E57）";
        FormClosing += PointCloudConversionForm_FormClosing;
        ResumeLayout(false);
    }

    private void ConfigureRootLayout()
    {
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(12);
        rootLayout.RowCount = 5;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
    }

    private void ConfigureInputGroup()
    {
        inputGroup.AutoSize = true;
        inputGroup.Dock = DockStyle.Top;
        inputGroup.Padding = new Padding(12);
        inputGroup.Text = "输入与输出";

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));

        inputFileTextBox = CreateTextBox();
        browseInputButton = CreateButton("浏览…", browseInputButton_Click);
        vendorComboBox = CreateComboBox();
        vendorComboBox.Width = 220;
        vendorComboBox.SelectedIndexChanged += vendorComboBox_SelectedIndexChanged;
        outputFileTextBox = CreateTextBox();
        browseOutputButton = CreateButton("选择…", browseOutputButton_Click);

        AddRow(layout, 0, "扫描文件 / 文件夹", inputFileTextBox, browseInputButton);
        AddRow(layout, 1, "设备类型", vendorComboBox, new Label());
        AddRow(layout, 2, "E57 输出文件", outputFileTextBox, browseOutputButton);
        inputGroup.Controls.Add(layout);
        rootLayout.Controls.Add(inputGroup, 0, 0);
    }

    private void ConfigureSettingsGroup()
    {
        settingsGroup.AutoSize = true;
        settingsGroup.Dock = DockStyle.Top;
        settingsGroup.Padding = new Padding(12);
        settingsGroup.Text = "移动二维扫描设置";

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        mileageDirectionComboBox = CreateComboBox();
        scanDirectionComboBox = CreateComboBox();
        outputModeComboBox = CreateComboBox();
        outputModeComboBox.SelectedIndexChanged += outputModeComboBox_SelectedIndexChanged;
        segmentColumnsNumeric = new NumericUpDown
        {
            DecimalPlaces = 0,
            Increment = 5_000M,
            Minimum = 5_000M,
            Maximum = 1_000_000M,
            Value = 5_000M,
            ThousandsSeparator = true,
            Width = 200,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3, 5, 8, 5)
        };

        layout.Controls.Add(CreateLabel("里程方向"), 0, 0);
        layout.Controls.Add(mileageDirectionComboBox, 1, 0);
        layout.Controls.Add(CreateLabel("扫描方向"), 2, 0);
        layout.Controls.Add(scanDirectionComboBox, 3, 0);

        layout.Controls.Add(CreateLabel("E57 输出方式"), 0, 1);
        layout.Controls.Add(outputModeComboBox, 1, 1);
        segmentColumnsLabel = CreateLabel("每段扫描列数");
        layout.Controls.Add(segmentColumnsLabel, 2, 1);
        layout.Controls.Add(segmentColumnsNumeric, 3, 1);

        parallelTasksNumeric = new NumericUpDown
        {
            Minimum = 1,
            Maximum = BatchConversionScheduler.MaximumConcurrency,
            Value = BatchConversionScheduler.DefaultConcurrency,
            Width = 90,
            Margin = new Padding(3, 5, 8, 5)
        };
        layout.Controls.Add(CreateLabel("同时转换任务数"), 0, 2);
        layout.Controls.Add(parallelTasksNumeric, 1, 2);
        var concurrencyHint = CreateLabel("固定保留原始强度与 SDK 处理强度");
        concurrencyHint.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(concurrencyHint, 2, 2);
        layout.SetColumnSpan(concurrencyHint, 2);

        var policy = CreateLabel("Y 保存扫描列号；断面间距在正射成图时由小车速度和每秒圈数计算。无回波点通过 rowIndex / columnIndex 保留稀疏结构。");
        policy.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(policy, 0, 3);
        layout.SetColumnSpan(policy, 4);

        settingsGroup.Controls.Add(layout);
        rootLayout.Controls.Add(settingsGroup, 0, 1);
    }

    private void ConfigureProgressGroup()
    {
        progressGroup.AutoSize = true;
        progressGroup.Dock = DockStyle.Top;
        progressGroup.Padding = new Padding(12);
        progressGroup.Text = "转换状态";

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        conversionProgressBar = new ProgressBar { Dock = DockStyle.Top, Height = 24 };
        statusLabel = CreateLabel("等待输入");
        statisticsLabel = CreateLabel("扫描线：0 / 0；有效点：0；跳过无效点：0");
        statisticsLabel.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(conversionProgressBar, 0, 0);
        layout.Controls.Add(statusLabel, 0, 1);
        layout.Controls.Add(statisticsLabel, 0, 2);
        progressGroup.Controls.Add(layout);
        rootLayout.Controls.Add(progressGroup, 0, 2);
    }

    private void ConfigureActions()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 6)
        };
        precheckButton = CreateButton("预检查", precheckButton_Click);
        importTaskListButton = CreateButton("导入任务清单…", importTaskListButton_Click);
        removeCompletedTaskItemsCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = false,
            Margin = new Padding(12, 8, 3, 3),
            Text = "成功后从清单移除任务"
        };
        startConversionButton = CreateButton("开始转换", startConversionButton_Click);
        cancelButton = CreateButton("取消", cancelButton_Click);
        cancelButton.Enabled = false;
        panel.Controls.AddRange([
            importTaskListButton,
            precheckButton,
            startConversionButton,
            cancelButton,
            removeCompletedTaskItemsCheckBox
        ]);
        rootLayout.Controls.Add(panel, 0, 3);
    }

    private void ConfigureLogGroup()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Text = "运行日志"
        };
        logTextBox.BackColor = SystemColors.Window;
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Font = new Font("Consolas", 9F);
        logTextBox.ReadOnly = true;
        logTextBox.WordWrap = false;
        group.Controls.Add(logTextBox);
        rootLayout.Controls.Add(group, 0, 4);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control value, Control action)
    {
        layout.Controls.Add(CreateLabel(label), 0, row);
        layout.Controls.Add(value, 1, row);
        layout.Controls.Add(action, 2, row);
    }

    private static Label CreateLabel(string text) => new()
    {
        AutoSize = true,
        Margin = new Padding(3, 9, 3, 3),
        Text = text
    };

    private static TextBox CreateTextBox() => new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 5, 3, 5)
    };

    private static ComboBox CreateComboBox() => new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button { AutoSize = true, Text = text };
        button.Click += handler;
        return button;
    }
}
