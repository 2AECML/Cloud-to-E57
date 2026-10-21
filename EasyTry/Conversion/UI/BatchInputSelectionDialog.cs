namespace EasyTry.Conversion.UI;

/// <summary>
/// Lets the user choose an arbitrary subset of inputs discovered under one
/// directory. FolderBrowserDialog itself cannot select multiple folders.
/// </summary>
internal sealed class BatchInputSelectionDialog : Form
{
    private readonly CheckedListBox inputList = new();

    public BatchInputSelectionDialog(
        string title,
        IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 430);
        ClientSize = new Size(820, 520);
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

        var instruction = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 10),
            Text = "勾选需要加入本批任务的 FARO .fls 文件或扫描包文件夹。"
        };
        root.Controls.Add(instruction, 0, 0);

        inputList.CheckOnClick = true;
        inputList.Dock = DockStyle.Fill;
        inputList.HorizontalScrollbar = true;
        inputList.IntegralHeight = false;
        foreach (var path in candidates)
        {
            var kind = Directory.Exists(path) ? "文件夹" : "文件";
            inputList.Items.Add(new InputItem(path, kind), isChecked: true);
        }
        root.Controls.Add(inputList, 0, 1);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
            WrapContents = false
        };
        var confirm = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Text = "确定"
        };
        var cancel = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "取消"
        };
        var selectAll = new Button { AutoSize = true, Text = "全选" };
        var clear = new Button { AutoSize = true, Text = "清空" };
        selectAll.Click += (_, _) => SetAllChecked(true);
        clear.Click += (_, _) => SetAllChecked(false);
        confirm.Click += (_, _) =>
        {
            if (inputList.CheckedItems.Count > 0)
            {
                return;
            }

            DialogResult = DialogResult.None;
            MessageBox.Show(
                this,
                "请至少勾选一个输入。",
                "尚未选择",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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

    public IReadOnlyList<string> SelectedPaths => inputList.CheckedItems
        .Cast<InputItem>()
        .Select(item => item.Path)
        .ToArray();

    private void SetAllChecked(bool isChecked)
    {
        for (var index = 0; index < inputList.Items.Count; index++)
        {
            inputList.SetItemChecked(index, isChecked);
        }
    }

    private sealed record InputItem(string Path, string Kind)
    {
        public override string ToString() =>
            $"[{Kind}] {System.IO.Path.GetFileName(Path)}    {Path}";
    }
}
