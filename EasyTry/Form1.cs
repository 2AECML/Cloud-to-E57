using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ZfsSdkRef;
using EnumsNET;
using EasyTry.General;
using System.Threading.Tasks;
using EasyTry.ZF;
using EasyTry.Faro;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Runtime.InteropServices;


namespace EasyTry
{
    public partial class Form1 : Form
    {
        //Faro扫描仪控制变量
        private ScanControl m_ScanControl;
        private AutoMessages m_AutoMessages;



        public string PortName { get; set; }
        public string BaudName { get; set; }
        public string Frequency { get; set; }
        public string ScanIP { get; set; }  //1860122
        public string ScanProjectName { get; set; }           //扫描文件项目名（扫描仪内）

        public int scanProjectNum = 1;
        public string returnmsg = "";
        bool isOpen = false;

        public event Action Connecting;

        public event EventHandler<IControllerBase> Connected;

        public event EventHandler<string> ConnectFailed;
        public Dictionary<ScannerIdent, Dictionary<int, List<int>>> Parameters { get; set; }
        public string zfIpAddress { get; set; }
        public int zfCmdPort { get; set; }
        public int zfDataPort { get; set; }
        public ScannerIdent zfSelectedScanner { get; set; } = ScannerIdent.Zf9012;
        public ZFResolution zfResolution { get; set; }
        public ZFFrequency zfFrequency { get; set; }

        public string zfSelectedResolution { get; set; }
        public string zfSelectedFrequency { get; set; }

        private string zfLastProjectName { get; set; }
        public string zfProjectName { get; set; }
        public string zfFileName { get; set; }
        public int zfFileSize { get; set; }

        public bool GpsSyncSignal { get; set; }
        public bool ReceieveRtData = false;

        //暂不考虑添加实时接收数据功能
        //public bool ReceieveRtData => RtScanHandler != null && (RtScanHandler.CreateRtSlice || RtScanHandler.CreateRtImage);
        //public RealtimeScanHandler RtScanHandler { get; set; }

        private ZFController zfcontroller;

        public Form1()
        {
            InitializeComponent();

            //扫描仪分辨率和质量的提示文字 
            zhiliangtoolTip.SetToolTip(label3, "分辨率指的是扫描点云的密度，即在单位距离内采集的测量点数。分辨率越高，点云越密集，细节表现越好，但扫描时间也会相应增加");
            pinlvtoolTip.SetToolTip(resolutionratiolabel, "分辨率指的是扫描点云的密度，即在单位距离内采集的测量点数。分辨率越高，点云越密集，细节表现越好，但扫描时间也会相应增加");

            //Z+F分辨率和质量的提示
            listView2.View = View.Details; // 设置为表格视图
            listView2.FullRowSelect = true; // 整行选择
            listView2.GridLines = true; // 显示网格线
            listView2.MultiSelect = false; // 是否允许多选

            // 添加列标题
            listView2.Columns.Add("组合", 80, HorizontalAlignment.Left);
            listView2.Columns.Add("分辨率", 120, HorizontalAlignment.Center);
            listView2.Columns.Add("转速", 120, HorizontalAlignment.Center);

            // 方法1：逐个添加子项
            ListViewItem item1 = new ListViewItem("1"); // 第一列数据
            item1.SubItems.Add("低");                  // 第二列
            item1.SubItems.Add("高");                   // 第三列
            listView2.Items.Add(item1);

            // 方法2：使用数组添加
            string[] row2 = { "2(默认)", "中", "中" };
            listView2.Items.Add(new ListViewItem(row2));

            string[] row3 = { "3", "高", "低" };
            listView2.Items.Add(new ListViewItem(row3));


            //--------------Z+F外业部分-------------------
            DataContext = this;

            //初始化空的参数字典
            Parameters = new Dictionary<ScannerIdent, Dictionary<int, List<int>>>();

            //声明+初始化Z+F9020参数列表
            Dictionary<int, List<int>> parameters_9020 = new Dictionary<int, List<int>>
            {
                { 20480, new List<int>() { 107, 53 } },
                { 10240, new List<int>() { 267, 134, 67 } },
                { 8192, new List<int>() { 267, 134, 67 } },
                { 6827, new List<int>() { 160, 80 } },
                { 5120, new List<int>() { 214, 107, 53 } },
                { 4096, new List<int>() { 267, 134, 67 } },
                { 3413, new List<int>() { 160, 80 } },
                { 2048, new List<int>() { 267, 134, 67 } },
                { 1024, new List<int>() { 267, 134 } },
            };

            //声明+初始化Z+F9012参数列表
            Dictionary<int, List<int>> parameters_9012 = new Dictionary<int, List<int>>
            {
                { 20480, new List<int>() { 50 } },
                { 10240, new List<int>() { 100, 50 } },
                { 5120, new List<int>() { 200, 100, 50 } }
            };

            //将两种参数列表和对应的枚举类型连接
            Parameters.Add(ScannerIdent.Zf9020, parameters_9020);
            Parameters.Add(ScannerIdent.Zf9012, parameters_9012);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ResolutioncomboBox.Items.Add("低");
            ResolutioncomboBox.Items.Add("高");
            ResolutioncomboBox.Items.Add("超高");
            ResolutioncomboBox.SelectedItem = "高";

            FarotypecomboBox.SelectedIndex = 0;
            FaroscanerworktypecomboBox.SelectedIndex = 1;
            FaroMileageOrdercomboBox.SelectedIndex = 0 ;
            ScanObjectCodecomboBox.SelectedIndex = 0 ;
            FaroexporttypecomboBox.SelectedIndex=0 ;

            ZFtypecomboBox.SelectedIndex = 1;
            scanerworktypecomboBox.SelectedIndex = 1;
            MileageOrdercomboBox.SelectedIndex = 0;


        }

        #region Z+F扫描仪部分

        #region 外业部分
        //连接Z+F扫描仪
        private async void linkbutton_Click(object sender, EventArgs e)
        {

            //*********补充各个数据框的读入********

            if (IPtextBox.Text == null)
            {
                MessageBox.Show("请输入Z+F扫描仪的IP");
                return;
            }

            zfIpAddress = IPtextBox.Text;

            if (CommandporttextBox.Text == null)
            {
                MessageBox.Show("请输入扫描仪的命令端口");
                return;
            }

            zfCmdPort = int.Parse(CommandporttextBox.Text);

            if (DataporttextBox.Text == null)
            {
                MessageBox.Show("请输入扫描仪的数据端口");
                return;
            }

            zfCmdPort = int.Parse(CommandporttextBox.Text);

            if (ProjectNametextBox.Text == null)
            {
                MessageBox.Show("请输入扫描项目名称");
                return;
            }

            zfProjectName = ProjectNametextBox.Text;

            if (ResolutioncomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择扫描分辨率");
                return;
            }

            zfSelectedResolution = ResolutioncomboBox.SelectedItem.ToString();

            //上一次项目不为空且上一次项目的名称和这次相同，需修改本次的项目名称
            if (!string.IsNullOrEmpty(zfLastProjectName) && zfLastProjectName == zfProjectName)
            {
                MessageBox.Show("请修改项目名称");
                return;
            }

            //运用封装方法，获取分辨率和频率的命令字符串
            string resolutionStr, frequencyStr;
            //resolutionStr = zfSelectedScanner.GetResolutionCommand(zfSelectedResolution);
            //frequencyStr = zfSelectedScanner.GetFrequencyCommand(zfSelectedFrequency);
            if (zfSelectedResolution == "低")
            {
                resolutionStr = "l";
                frequencyStr = "l";
            }

            else if (zfSelectedResolution == "高")
            {
                resolutionStr = "h";
                frequencyStr = "n";
            }

            else
            {
                resolutionStr = "sh";
                frequencyStr = "h";
            }

            zfFileName = "tlsd";
            zfFileSize = 2048;
            try
            {
                zfcontroller = new ZFController(zfIpAddress, zfCmdPort, zfDataPort, resolutionStr, frequencyStr,
                    zfProjectName, zfFileName, zfFileSize, GpsSyncSignal, ReceieveRtData);
            }
            catch (SEHException ex)
            {
                MessageBox.Show("非托管代码抛出异常: " + ex.Message);
            }

            try
            {
                Connecting?.Invoke();
                await Task.Run(() => zfcontroller.Connect());  //await 关键字用于异步编程，它允许程序在等待异步操作完成时，不会阻塞当前线程。

                //zfcontroller.Connect();

                if (!zfcontroller.IsConnected)
                {
                    ConnectFailed?.Invoke(this, "连接失败");
                    return;
                }
                zfLastProjectName = zfProjectName;
            }
            catch (Exception exception)
            {
                ConnectFailed?.Invoke(this, exception.Message);
                return;
            }

            Connected?.Invoke(this, zfcontroller);

            //MessageBox.Show("扫描仪连接成功");
            ZFfilelistBox.Items.Add("Z+F扫描仪连接成功");
        }

        //Z+F扫描仪开始扫描
        private void zfstartscanbutton_Click(object sender, EventArgs e)
        {
            zfcontroller.Start();
            ZFfilelistBox.Items.Add("Z+F扫描仪开始扫描");
        }

        //Z+F扫描仪停止扫描
        private void zfclosescanbutton_Click(object sender, EventArgs e)
        {
            zfcontroller.Stop();
            ZFfilelistBox.Items.Add("Z+F扫描仪停止扫描");
        }
        #endregion

        #region 内业部分

        public FileInfo MainScanFile;
        ScanState scanState = new ScanState();

        ZfsReader profiler = new ZfsReader();
        List<PointXYZI[]> MainWindowPointsXYZI = new List<PointXYZI[]>();

        //选择扫描文件，加载数据到工作流中
        private void selectbutton_Click(object sender, EventArgs e)
        {
            if (ZFtypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择Z+F扫描仪的的型号");
                return;
            }

            scanState.ScannerType = (string)ZFtypecomboBox.SelectedItem;

            if (scanerworktypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择Z+F扫描仪的的工作类型");
                return;
            }

            scanState.ScannerOperationMode = (string)scanerworktypecomboBox.SelectedItem;

            if (MileageOrdercomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择里程顺序");
                return;
            }
            scanState.MileageOrder = (string)MileageOrdercomboBox.SelectedItem;

            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            fileDialog.Filter = "ZFS(*.zfs)|*.zfs";
            fileDialog.Multiselect = false; //定义可以一次只能选择一个文件

            //判断用户在文件对话框,成功选择了文件
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                MainScanFile = new FileInfo(fileDialog.FileName);

                profiler.ZfsReaderFile(MainScanFile, scanState, out List<PointXYZI[]> PointsXYZI);
                MainWindowPointsXYZI = PointsXYZI;

                ZFfilelistBox.Items.Clear();
                ZFfilelistBox.Items.Add(MainScanFile.Name + "读取成功");
            }
            else
            {
                return;
            }
        }

        private void exportbutton_Click(object sender, EventArgs e)
        {
            if (exporttypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择输出文件类型");
                return;
            }

            string ExportType = (string)exporttypecomboBox.SelectedItem;

            switch (ExportType)
            {
                //生成TXT文件
                case "TXT":
                    profiler.WriteXYZIToTxt(MainScanFile, MainWindowPointsXYZI);
                    break;

                //生成图像
                case "IMAGE":
                    if (imagewidthcomboBox.SelectedItem == null)
                    {
                        MessageBox.Show("用户未选择生成影像的宽度，自动采用默认影像宽度");
                        scanState.ScanPreviewImageWidth = 5000;
                    }
                    else
                    {
                        string selectedText = imagewidthcomboBox.SelectedItem?.ToString();

                        int selectedValue = 0;
                        if (int.TryParse(selectedText, out selectedValue))
                        {
                            // 使用 selectedValue
                        }

                        scanState.ScanPreviewImageWidth = selectedValue;

                    }

                    int num = 0;

                    if (MainWindowPointsXYZI.Count % scanState.ScanPreviewImageWidth == 0)
                    {
                        num = MainWindowPointsXYZI.Count / scanState.ScanPreviewImageWidth;
                    }

                    else
                    {
                        num = MainWindowPointsXYZI.Count / scanState.ScanPreviewImageWidth + 1;
                    }


                    for (int i = 0; i < num; i++)
                    {
                        string imagepath = MainScanFile.DirectoryName + "\\preview" + "_" + i + "_image.png";

                        int colStart = i * scanState.ScanPreviewImageWidth;
                        int imageWidth = scanState.ScanPreviewImageWidth;
                        profiler.CreateIntensityImageTLSD(imagepath, MainWindowPointsXYZI, scanState, colStart, imageWidth);
                        MessageBox.Show("preview" + "_" + i + "_image.png" + "成功生成");
                    }
                    break;
            }

            exporttypecomboBox.SelectedItem = null; //清空选择的导出文件类型
            imagewidthcomboBox.SelectedItem = null;
        }

        #endregion

        #endregion

        #region Faro扫描仪部分

        #region Faro外业部分
        //连接扫描仪
        private void Link_Click(object sender, EventArgs e)
        {
            //--------------Faro外业部分-------------------
            m_ScanControl = new ScanControl();
            m_AutoMessages = new AutoMessages();

            Showbox.Text = "连接传感器....\r\n";

            //传感器
            BaudName = Baudbox.Text;
            int baudvalue = 0;
            int.TryParse(BaudName, out baudvalue);


            //扫描仪
            m_ScanControl.ScanIP = IPbox.Text;
            m_ScanControl.ScanFileName = Namebox.Text;
            m_ScanControl.ScanFileNum = scanProjectNum;

            if (m_ScanControl.ScanConnect() == true)
            {
                Showbox.Text += "连接成功\r\n";
            }
            else
            {
                Showbox.Text += "连接失败请检查与扫描仪连接....\r\n";
            }
        }

        //开始扫描
        private void Start_Click_1(object sender, EventArgs e)
        {
            m_ScanControl.StartHelicalCanGreyScan();    //开始运行
            Showbox.Text += "开始扫描\r\n";
        }

        //记录数据
        private void Write_Click(object sender, EventArgs e)
        {
            int a = m_ScanControl.RecordScan();
            Showbox.Text += "记录数据\r\n";
        }

        //停止扫描
        private void Stop_Click_1(object sender, EventArgs e)
        {
            m_ScanControl.StopScan();
            //m_ScanControl.ShutDow();
            if (m_ScanControl.ScanConnect() == false)
            {
                Showbox.Text += ("扫描结束!\r\n");
            }
        }

        //关闭扫描仪函数，并未使用
        //private void Shut_Click(object sender, EventArgs e)
        //{
        //    m_ScanControl.ShutDow();//扫描仪关机
        //    Showbox.Text += ("关闭!\r\n");
        //}

        #endregion

        #region Faro内业部分

        public FileInfo FaroScanFile;
        ScanState FaroscanState = new ScanState();

        FlsReader flsreader = new FlsReader();
        List<PointXYZI[]> FaroPointsXYZI = new List<PointXYZI[]>();

        private void Faroinputbutton_Click(object sender, EventArgs e)
        {

            if (FarotypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择Faro扫描仪的的型号");
                return;
            }

            FaroscanState.ScannerType = (string)FarotypecomboBox.SelectedItem;

            if (FaroscanerworktypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择Faro扫描仪的的工作类型");
                return;
            }

            FaroscanState.ScannerOperationMode = (string)FaroscanerworktypecomboBox.SelectedItem;

            if (FaroMileageOrdercomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择里程顺序");
                return;
            }

            FaroscanState.MileageOrder = (string)FaroMileageOrdercomboBox.SelectedItem;

            if (ScanObjectCodecomboBox.SelectedItem == null)
            {
                MessageBox.Show("扫描项目类型");
                return;
            }

            FaroscanState.ScanObjectCode = (string)ScanObjectCodecomboBox.SelectedItem;

            OpenFileDialog FarofileDialog = new OpenFileDialog();
            FarofileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            FarofileDialog.Filter = "RLS(*.fls)|*.fls";
            FarofileDialog.Multiselect = false; //定义可以一次只能选择一个文件

            //判断用户在文件对话框,成功选择了文件
            if (FarofileDialog.ShowDialog() == DialogResult.OK)
            {
                FaroScanFile = new FileInfo(FarofileDialog.FileName);

                flsreader.FlsReaderFile(FaroScanFile, FaroscanState, out List<PointXYZI[]> PointsXYZI);
                Showbox.Text = FaroScanFile.Name;
                FaroPointsXYZI = PointsXYZI;

                Showbox.Text = "读取完成";
                //MessageBox.Show("读取完成");
            }
            else
            {
                return;
            }
        }

        private void Faroexportbutton_Click(object sender, EventArgs e)
        {
            if (FaroexporttypecomboBox.SelectedItem == null)
            {
                MessageBox.Show("请选择输出文件类型");
                return;
            }

            string ExportType = (string)FaroexporttypecomboBox.SelectedItem;

            switch (ExportType)
            {
                //生成TXT文件
                case "TXT":
                    flsreader.FaroWriteXYZIToTxt(FaroScanFile, FaroPointsXYZI);
                    break;

                //生成图像
                case "IMAGE":
                    if (FaroimagewidthcomboBox.SelectedItem == null)
                    {
                        MessageBox.Show("用户未选择生成影像的宽度，自动采用默认影像宽度");
                        FaroscanState.ScanPreviewImageWidth = 5000;
                    }
                    else
                    {
                        string selectedText = FaroimagewidthcomboBox.SelectedItem?.ToString();

                        int selectedValue = 0;
                        if (int.TryParse(selectedText, out selectedValue))
                        {
                            // 使用 selectedValue
                        }

                        FaroscanState.ScanPreviewImageWidth = selectedValue;

                    }

                    int num = 0;

                    if (FaroPointsXYZI.Count % FaroscanState.ScanPreviewImageWidth == 0)
                    {
                        num = FaroPointsXYZI.Count / FaroscanState.ScanPreviewImageWidth;
                    }

                    else
                    {
                        num = FaroPointsXYZI.Count / FaroscanState.ScanPreviewImageWidth + 1;
                    }


                    for (int i = 0; i < num; i++)
                    {
                        string imagepath = FaroScanFile.DirectoryName + "\\preview" + "_" + i + "_image.png";

                        int colStart = i * FaroscanState.ScanPreviewImageWidth;
                        int imageWidth = FaroscanState.ScanPreviewImageWidth;

                        flsreader.FaroCreateIntensityImageTLSD(imagepath, FaroPointsXYZI, FaroscanState, colStart, imageWidth);
                        MessageBox.Show("preview" + "_" + i + "_image.png" + "成功生成");
                    }
                    break;
            }
        }
        #endregion

        #endregion

        private void ListView2_MouseClick(object sender, MouseEventArgs e)
        {
            // 隐藏之前的提示
            toolTip1.Hide(listView2);

            // 获取点击位置信息
            ListViewHitTestInfo hitTest = listView2.HitTest(e.Location);

            // 检查是否点击在单元格上
            if (hitTest.Item != null && hitTest.SubItem != null)
            {
                // 获取点击单元格的内容
                string columnHeader = listView2.Columns[
                    hitTest.Item.SubItems.IndexOf(hitTest.SubItem)].Text;

                // 自定义提示内容
                string tooltipText = "";

                int rowIndex = listView2.Items.IndexOf(hitTest.Item);

                switch (rowIndex)
                {
                    case 0:
                        tooltipText = $"组合1 \n" +
                                    $"分辨率: 低（约 1-5 mm @ 10m）\n" +
                                    $"转速: 高（150 - 200 RPM）";
                        break;
                    case 1:
                        tooltipText = $"组合2 \n" +
                                    $"分辨率: 中（约 1-3 mm @ 10m）\n" +
                                    $"转速: 中（60 - 120 RPM）";
                        break;
                    case 2:
                        tooltipText = $"组合2 \n" +
                                    $"分辨率: 高（约 0.5-1 mm @ 10m）\n" +
                                    $"转速: 低（10 - 50 RPM）";
                        break;
                    default:
                        break;
                }

                // 显示提示
                toolTip1.Show(tooltipText, listView2,
                             e.Location.X + 20, e.Location.Y + 20, 4000);
            }
        }

        private void listView2_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            string message = GetColumnExplanation(e.Column);
            MessageBox.Show(message, "参数解释", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string GetColumnExplanation(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return "分辨率和转速的设置需要配套，一般高分辨率需要降低转速，低分辨率需要提高转速。";
                case 1:
                    return $"分辨率：指的是扫描点云的密度，即在单位距离内采集的测量点数。分辨率越高，点云越密集，细节表现越好，但扫描时间也会相应增加，单位：毫米@距离。\n"+
                            $"例：3.1mm @ 10m → 在10米距离上，点间距为3.1毫米";
                case 2:
                    return "转速：通常表示扫描头每分钟旋转的圈数，单位是rpm。";
                default:
                    return "没有这个列的解释。";
            }
        }
    }
}
