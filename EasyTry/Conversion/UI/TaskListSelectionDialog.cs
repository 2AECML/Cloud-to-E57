using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;

namespace EasyTry.Conversion.UI;

internal sealed class TaskListSelectionDialog : Form
{
    private readonly CheckedListBox taskList = new();

    public TaskListSelectionDialog(IReadOnlyList<BatchConversionTask> tasks)
    {
        Text = "选择要执行的清单任务";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 460);
        ClientSize = new Size(920, 600);
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "请勾选本次需要执行的任务。每项注明 tlsd 文件状态，默认不选择任何任务。",
            Padding = new Padding(0, 0, 0, 10)
        }, 0, 0);

        taskList.CheckOnClick = true;
        taskList.Dock = DockStyle.Fill;
        taskList.HorizontalScrollbar = true;
        foreach (var task in tasks)
        {
            taskList.Items.Add(new TaskItem(task), isChecked: false);
        }
        root.Controls.Add(taskList, 0, 1);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        var confirm = new Button { AutoSize = true, Text = "确定", DialogResult = DialogResult.OK };
        var cancel = new Button { AutoSize = true, Text = "取消", DialogResult = DialogResult.Cancel };
        var selectAll = new Button { AutoSize = true, Text = "全选" };
        var clear = new Button { AutoSize = true, Text = "清空" };
        selectAll.Click += (_, _) => SetAllChecked(true);
        clear.Click += (_, _) => SetAllChecked(false);
        confirm.Click += (_, _) =>
        {
            if (taskList.CheckedItems.Count > 0) { return; }
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "请至少选择一个任务。", "尚未选择",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        actions.Controls.Add(confirm);
        actions.Controls.Add(cancel);
        actions.Controls.Add(clear);
        actions.Controls.Add(selectAll);
        root.Controls.Add(actions, 0, 2);
        AcceptButton = confirm;
        CancelButton = cancel;
        Controls.Add(root);
    }

    public IReadOnlyList<BatchConversionTask> SelectedTasks => taskList.CheckedItems
        .Cast<TaskItem>()
        .Select(item => item.Task)
        .ToArray();

    private void SetAllChecked(bool value)
    {
        for (var index = 0; index < taskList.Items.Count; index++)
        {
            taskList.SetItemChecked(index, value);
        }
    }

    private sealed record TaskItem(BatchConversionTask Task)
    {
        private readonly string tlsdStatus = TaskListDisplayText.GetTlsdStatus(Task);

        public override string ToString()
        {
            var vendors = string.Join(" / ", Task.InputPaths
                .Select(ConversionPrecheckService.DetectVendor)
                .Distinct()
                .Select(ConversionPrecheckService.GetVendorDisplayName));
            return $"[{vendors}] [{tlsdStatus}] {Task.InputRoot}    →    {Task.OutputDirectory}    ({Task.InputPaths.Count} 个扫描任务)";
        }
    }
}

