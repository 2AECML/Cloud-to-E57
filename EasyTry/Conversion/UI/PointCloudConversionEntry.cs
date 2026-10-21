namespace EasyTry.Conversion.UI;

/// <summary>
/// 在不修改旧 Form1 及其 Designer 的前提下增加转换入口。
/// </summary>
public static class PointCloudConversionEntry
{
    private const string MenuStripName = "pointCloudConversionMenuStrip";

    public static void Attach(Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        if (mainForm.Controls.Find(MenuStripName, true).Length > 0)
        {
            return;
        }

        var menuStrip = new MenuStrip
        {
            Name = MenuStripName,
            Dock = DockStyle.Top
        };
        var toolsMenu = new ToolStripMenuItem("工具");
        var conversionMenuItem = new ToolStripMenuItem("点云转换（Z+F / FARO → E57）");
        conversionMenuItem.Text = "点云批量转换（Z+F / FARO → 通用 E57）";
        conversionMenuItem.Click += (_, _) => OpenConversionForm(mainForm);
        toolsMenu.DropDownItems.Add(conversionMenuItem);
        menuStrip.Items.Add(toolsMenu);

        mainForm.MainMenuStrip = menuStrip;
        mainForm.Controls.Add(menuStrip);
        menuStrip.BringToFront();

        var tabControl = mainForm.Controls.OfType<TabControl>().FirstOrDefault();
        if (tabControl is not null)
        {
            tabControl.Dock = DockStyle.None;
            tabControl.Anchor = AnchorStyles.Top |
                                AnchorStyles.Bottom |
                                AnchorStyles.Left |
                                AnchorStyles.Right;

            void UpdateTabBounds()
            {
                const int horizontalMargin = 12;
                const int bottomMargin = 12;
                var top = menuStrip.Bottom + 8;
                tabControl.Location = new Point(horizontalMargin, top);
                tabControl.Size = new Size(
                    Math.Max(200, mainForm.ClientSize.Width - horizontalMargin * 2),
                    Math.Max(200, mainForm.ClientSize.Height - top - bottomMargin));
            }

            UpdateTabBounds();
            mainForm.ClientSizeChanged += (_, _) => UpdateTabBounds();
        }
    }

    private static void OpenConversionForm(Form owner)
    {
        var existing = Application.OpenForms
            .OfType<PointCloudConversionForm>()
            .FirstOrDefault(form => !form.IsDisposed);

        if (existing is not null)
        {
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            existing.Show();
            existing.Activate();
            return;
        }

        new PointCloudConversionForm().Show(owner);
    }
}
