namespace EasyTry
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            inputbutton = new Button();
            ZFfilelistBox = new ListBox();
            zfslabel = new Label();
            zftypelabel = new Label();
            ZFtypecomboBox = new ComboBox();
            exportbutton = new Button();
            exporttypelabel = new Label();
            exporttypecomboBox = new ComboBox();
            ScanerWorkTypelabel = new Label();
            ZFScaner = new TabControl();
            tabPage2 = new TabPage();
            listView1 = new ListView();
            label11 = new Label();
            groupBox2 = new GroupBox();
            Faroexportbutton = new Button();
            FaroimagewidthcomboBox = new ComboBox();
            FaroexporttypecomboBox = new ComboBox();
            label9 = new Label();
            label8 = new Label();
            groupBox1 = new GroupBox();
            ScanObjectCodecomboBox = new ComboBox();
            label10 = new Label();
            FaroMileageOrdercomboBox = new ComboBox();
            Faroinputbutton = new Button();
            label5 = new Label();
            label6 = new Label();
            FaroscanerworktypecomboBox = new ComboBox();
            label7 = new Label();
            FarotypecomboBox = new ComboBox();
            FaroScannergroupBox = new GroupBox();
            Namebox = new TextBox();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            IPbox = new TextBox();
            Baudbox = new TextBox();
            Frequencybox = new TextBox();
            Stop = new Button();
            Write = new Button();
            Start = new Button();
            Link = new Button();
            Showbox = new TextBox();
            tabPage1 = new TabPage();
            listView2 = new ListView();
            scanerparametergroupBox = new GroupBox();
            IPtextBox = new TextBox();
            IPlabel = new Label();
            ResolutioncomboBox = new ComboBox();
            Portlabel = new Label();
            DataporttextBox = new TextBox();
            resolutionratiolabel = new Label();
            ProjectNametextBox = new TextBox();
            CommandporttextBox = new TextBox();
            ProjectNamelabel = new Label();
            zfclosescanbutton = new Button();
            zfstartscanbutton = new Button();
            linkbutton = new Button();
            exportgroupBox = new GroupBox();
            imagewidthcomboBox = new ComboBox();
            imagelabel = new Label();
            ScandataparametergroupBox = new GroupBox();
            MileageOrdercomboBox = new ComboBox();
            MileageOrderlabel = new Label();
            scanerworktypecomboBox = new ComboBox();
            zhiliangtoolTip = new ToolTip(components);
            pinlvtoolTip = new ToolTip(components);
            toolTip1 = new ToolTip(components);
            toolTip2 = new ToolTip(components);
            ZFScaner.SuspendLayout();
            tabPage2.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox1.SuspendLayout();
            FaroScannergroupBox.SuspendLayout();
            tabPage1.SuspendLayout();
            scanerparametergroupBox.SuspendLayout();
            exportgroupBox.SuspendLayout();
            ScandataparametergroupBox.SuspendLayout();
            SuspendLayout();
            // 
            // inputbutton
            // 
            inputbutton.Location = new Point(494, 93);
            inputbutton.Name = "inputbutton";
            inputbutton.Size = new Size(166, 65);
            inputbutton.TabIndex = 0;
            inputbutton.Text = "读入扫描文件";
            inputbutton.UseVisualStyleBackColor = true;
            inputbutton.Click += selectbutton_Click;
            // 
            // ZFfilelistBox
            // 
            ZFfilelistBox.FormattingEnabled = true;
            ZFfilelistBox.ItemHeight = 24;
            ZFfilelistBox.Location = new Point(768, 90);
            ZFfilelistBox.Name = "ZFfilelistBox";
            ZFfilelistBox.Size = new Size(327, 532);
            ZFfilelistBox.TabIndex = 2;
            // 
            // zfslabel
            // 
            zfslabel.AutoSize = true;
            zfslabel.Location = new Point(768, 43);
            zfslabel.Name = "zfslabel";
            zfslabel.Size = new Size(100, 24);
            zfslabel.TabIndex = 3;
            zfslabel.Text = "操作控制框";
            // 
            // zftypelabel
            // 
            zftypelabel.AutoSize = true;
            zftypelabel.Location = new Point(25, 51);
            zftypelabel.Name = "zftypelabel";
            zftypelabel.Size = new Size(136, 24);
            zftypelabel.TabIndex = 4;
            zftypelabel.Text = "扫描仪型号类型";
            // 
            // ZFtypecomboBox
            // 
            ZFtypecomboBox.FormattingEnabled = true;
            ZFtypecomboBox.Items.AddRange(new object[] { "Z+F 50XX", "Z+F 90XX" });
            ZFtypecomboBox.Location = new Point(244, 48);
            ZFtypecomboBox.Name = "ZFtypecomboBox";
            ZFtypecomboBox.Size = new Size(225, 32);
            ZFtypecomboBox.TabIndex = 5;
            // 
            // exportbutton
            // 
            exportbutton.Location = new Point(475, 67);
            exportbutton.Name = "exportbutton";
            exportbutton.Size = new Size(185, 61);
            exportbutton.TabIndex = 6;
            exportbutton.Text = "导出扫描文件";
            exportbutton.UseVisualStyleBackColor = true;
            exportbutton.Click += exportbutton_Click;
            // 
            // exporttypelabel
            // 
            exporttypelabel.AutoSize = true;
            exporttypelabel.Location = new Point(29, 52);
            exporttypelabel.Name = "exporttypelabel";
            exporttypelabel.Size = new Size(118, 24);
            exporttypelabel.TabIndex = 7;
            exporttypelabel.Text = "导出文件类型";
            // 
            // exporttypecomboBox
            // 
            exporttypecomboBox.FormattingEnabled = true;
            exporttypecomboBox.Items.AddRange(new object[] { "TXT", "LAS", "BIN", "IMAGE" });
            exporttypecomboBox.Location = new Point(192, 49);
            exporttypecomboBox.Name = "exporttypecomboBox";
            exporttypecomboBox.Size = new Size(225, 32);
            exporttypecomboBox.TabIndex = 8;
            // 
            // ScanerWorkTypelabel
            // 
            ScanerWorkTypelabel.AutoSize = true;
            ScanerWorkTypelabel.Location = new Point(25, 108);
            ScanerWorkTypelabel.Name = "ScanerWorkTypelabel";
            ScanerWorkTypelabel.Size = new Size(136, 24);
            ScanerWorkTypelabel.TabIndex = 9;
            ScanerWorkTypelabel.Text = "扫描仪工作类型";
            // 
            // ZFScaner
            // 
            ZFScaner.Controls.Add(tabPage2);
            ZFScaner.Controls.Add(tabPage1);
            ZFScaner.Location = new Point(12, 12);
            ZFScaner.Name = "ZFScaner";
            ZFScaner.SelectedIndex = 0;
            ZFScaner.Size = new Size(1124, 907);
            ZFScaner.TabIndex = 10;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(listView1);
            tabPage2.Controls.Add(label11);
            tabPage2.Controls.Add(groupBox2);
            tabPage2.Controls.Add(groupBox1);
            tabPage2.Controls.Add(FaroScannergroupBox);
            tabPage2.Controls.Add(Stop);
            tabPage2.Controls.Add(Write);
            tabPage2.Controls.Add(Start);
            tabPage2.Controls.Add(Link);
            tabPage2.Controls.Add(Showbox);
            tabPage2.Location = new Point(4, 33);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1116, 870);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Faro";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // listView1
            // 
            listView1.Location = new Point(733, 620);
            listView1.Name = "listView1";
            listView1.Size = new Size(338, 197);
            listView1.TabIndex = 25;
            listView1.UseCompatibleStateImageBehavior = false;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(733, 40);
            label11.Name = "label11";
            label11.Size = new Size(100, 24);
            label11.TabIndex = 24;
            label11.Text = "操作控制框";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(Faroexportbutton);
            groupBox2.Controls.Add(FaroimagewidthcomboBox);
            groupBox2.Controls.Add(FaroexporttypecomboBox);
            groupBox2.Controls.Add(label9);
            groupBox2.Controls.Add(label8);
            groupBox2.Location = new Point(36, 668);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(637, 165);
            groupBox2.TabIndex = 23;
            groupBox2.TabStop = false;
            groupBox2.Text = "数据导出参数";
            // 
            // Faroexportbutton
            // 
            Faroexportbutton.Location = new Point(429, 46);
            Faroexportbutton.Name = "Faroexportbutton";
            Faroexportbutton.Size = new Size(183, 85);
            Faroexportbutton.TabIndex = 24;
            Faroexportbutton.Text = "导出扫描文件";
            Faroexportbutton.UseVisualStyleBackColor = true;
            Faroexportbutton.Click += Faroexportbutton_Click;
            // 
            // FaroimagewidthcomboBox
            // 
            FaroimagewidthcomboBox.FormattingEnabled = true;
            FaroimagewidthcomboBox.Items.AddRange(new object[] { "4000", "5000", "6000", "7000", "8000" });
            FaroimagewidthcomboBox.Location = new Point(216, 117);
            FaroimagewidthcomboBox.Name = "FaroimagewidthcomboBox";
            FaroimagewidthcomboBox.Size = new Size(182, 32);
            FaroimagewidthcomboBox.TabIndex = 3;
            // 
            // FaroexporttypecomboBox
            // 
            FaroexporttypecomboBox.FormattingEnabled = true;
            FaroexporttypecomboBox.Items.AddRange(new object[] { "TXT", "LAS", "BIN", "IMAGE" });
            FaroexporttypecomboBox.Location = new Point(216, 56);
            FaroexporttypecomboBox.Name = "FaroexporttypecomboBox";
            FaroexporttypecomboBox.Size = new Size(182, 32);
            FaroexporttypecomboBox.TabIndex = 2;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(27, 120);
            label9.Name = "label9";
            label9.Size = new Size(118, 24);
            label9.TabIndex = 1;
            label9.Text = "生成影像宽度";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(27, 59);
            label8.Name = "label8";
            label8.Size = new Size(118, 24);
            label8.TabIndex = 0;
            label8.Text = "导出文件类型";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(ScanObjectCodecomboBox);
            groupBox1.Controls.Add(label10);
            groupBox1.Controls.Add(FaroMileageOrdercomboBox);
            groupBox1.Controls.Add(Faroinputbutton);
            groupBox1.Controls.Add(label5);
            groupBox1.Controls.Add(label6);
            groupBox1.Controls.Add(FaroscanerworktypecomboBox);
            groupBox1.Controls.Add(label7);
            groupBox1.Controls.Add(FarotypecomboBox);
            groupBox1.Location = new Point(36, 374);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(637, 278);
            groupBox1.TabIndex = 22;
            groupBox1.TabStop = false;
            groupBox1.Text = "扫描数据参数";
            // 
            // ScanObjectCodecomboBox
            // 
            ScanObjectCodecomboBox.FormattingEnabled = true;
            ScanObjectCodecomboBox.Items.AddRange(new object[] { "单圆", "马蹄形隧道", "矩形隧道", "大圆", "其它形式", "双圆", "道床", "铁路", "公路" });
            ScanObjectCodecomboBox.Location = new Point(216, 225);
            ScanObjectCodecomboBox.Name = "ScanObjectCodecomboBox";
            ScanObjectCodecomboBox.Size = new Size(182, 32);
            ScanObjectCodecomboBox.TabIndex = 22;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(27, 228);
            label10.Name = "label10";
            label10.Size = new Size(118, 24);
            label10.TabIndex = 21;
            label10.Text = "扫描项目类型";
            // 
            // FaroMileageOrdercomboBox
            // 
            FaroMileageOrdercomboBox.FormattingEnabled = true;
            FaroMileageOrdercomboBox.Items.AddRange(new object[] { "里程升序", "里程降序" });
            FaroMileageOrdercomboBox.Location = new Point(216, 162);
            FaroMileageOrdercomboBox.Name = "FaroMileageOrdercomboBox";
            FaroMileageOrdercomboBox.Size = new Size(182, 32);
            FaroMileageOrdercomboBox.TabIndex = 20;
            // 
            // Faroinputbutton
            // 
            Faroinputbutton.Location = new Point(429, 104);
            Faroinputbutton.Name = "Faroinputbutton";
            Faroinputbutton.Size = new Size(183, 85);
            Faroinputbutton.TabIndex = 21;
            Faroinputbutton.Text = "读入扫描文件";
            Faroinputbutton.UseVisualStyleBackColor = true;
            Faroinputbutton.Click += Faroinputbutton_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(27, 45);
            label5.Name = "label5";
            label5.Size = new Size(136, 24);
            label5.TabIndex = 15;
            label5.Text = "扫描仪型号类型";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(27, 100);
            label6.Name = "label6";
            label6.Size = new Size(136, 24);
            label6.TabIndex = 16;
            label6.Text = "扫描仪工作类型";
            // 
            // FaroscanerworktypecomboBox
            // 
            FaroscanerworktypecomboBox.FormattingEnabled = true;
            FaroscanerworktypecomboBox.Items.AddRange(new object[] { "设站扫描", "移动2D（正向）", "移动2D（反向）" });
            FaroscanerworktypecomboBox.Location = new Point(216, 100);
            FaroscanerworktypecomboBox.Name = "FaroscanerworktypecomboBox";
            FaroscanerworktypecomboBox.Size = new Size(182, 32);
            FaroscanerworktypecomboBox.TabIndex = 19;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(27, 165);
            label7.Name = "label7";
            label7.Size = new Size(136, 24);
            label7.TabIndex = 17;
            label7.Text = "扫描仪里程顺序";
            // 
            // FarotypecomboBox
            // 
            FarotypecomboBox.FormattingEnabled = true;
            FarotypecomboBox.Items.AddRange(new object[] { "Faro" });
            FarotypecomboBox.Location = new Point(216, 43);
            FarotypecomboBox.Name = "FarotypecomboBox";
            FarotypecomboBox.Size = new Size(182, 32);
            FarotypecomboBox.TabIndex = 18;
            // 
            // FaroScannergroupBox
            // 
            FaroScannergroupBox.Controls.Add(Namebox);
            FaroScannergroupBox.Controls.Add(label1);
            FaroScannergroupBox.Controls.Add(label2);
            FaroScannergroupBox.Controls.Add(label3);
            FaroScannergroupBox.Controls.Add(label4);
            FaroScannergroupBox.Controls.Add(IPbox);
            FaroScannergroupBox.Controls.Add(Baudbox);
            FaroScannergroupBox.Controls.Add(Frequencybox);
            FaroScannergroupBox.Location = new Point(36, 36);
            FaroScannergroupBox.Name = "FaroScannergroupBox";
            FaroScannergroupBox.Size = new Size(398, 326);
            FaroScannergroupBox.TabIndex = 14;
            FaroScannergroupBox.TabStop = false;
            FaroScannergroupBox.Text = "扫描仪参数设置";
            // 
            // Namebox
            // 
            Namebox.Location = new Point(111, 263);
            Namebox.Name = "Namebox";
            Namebox.Size = new Size(248, 30);
            Namebox.TabIndex = 7;
            Namebox.Text = "test01";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(25, 45);
            label1.Name = "label1";
            label1.Size = new Size(26, 24);
            label1.TabIndex = 0;
            label1.Text = "IP";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(25, 122);
            label2.Name = "label2";
            label2.Size = new Size(64, 24);
            label2.TabIndex = 1;
            label2.Text = "端口号";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(25, 192);
            label3.Name = "label3";
            label3.Size = new Size(46, 24);
            label3.TabIndex = 2;
            label3.Text = "频率";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(25, 266);
            label4.Name = "label4";
            label4.Size = new Size(82, 24);
            label4.TabIndex = 3;
            label4.Text = "项目名称";
            // 
            // IPbox
            // 
            IPbox.Location = new Point(111, 42);
            IPbox.Name = "IPbox";
            IPbox.Size = new Size(248, 30);
            IPbox.TabIndex = 4;
            IPbox.Text = "192.168.111.2";
            // 
            // Baudbox
            // 
            Baudbox.Location = new Point(111, 119);
            Baudbox.Name = "Baudbox";
            Baudbox.Size = new Size(248, 30);
            Baudbox.TabIndex = 5;
            Baudbox.Text = "8400";
            // 
            // Frequencybox
            // 
            Frequencybox.Location = new Point(111, 189);
            Frequencybox.Name = "Frequencybox";
            Frequencybox.Size = new Size(248, 30);
            Frequencybox.TabIndex = 6;
            Frequencybox.Text = "20";
            // 
            // Stop
            // 
            Stop.Location = new Point(482, 277);
            Stop.Name = "Stop";
            Stop.Size = new Size(191, 63);
            Stop.TabIndex = 13;
            Stop.Text = "结束扫描";
            Stop.UseVisualStyleBackColor = true;
            Stop.Click += Stop_Click_1;
            // 
            // Write
            // 
            Write.Location = new Point(482, 203);
            Write.Name = "Write";
            Write.Size = new Size(191, 63);
            Write.TabIndex = 11;
            Write.Text = "记录扫描";
            Write.UseVisualStyleBackColor = true;
            // 
            // Start
            // 
            Start.Location = new Point(482, 130);
            Start.Name = "Start";
            Start.Size = new Size(191, 63);
            Start.TabIndex = 10;
            Start.Text = "开始扫描";
            Start.UseVisualStyleBackColor = true;
            Start.Click += Start_Click_1;
            // 
            // Link
            // 
            Link.Location = new Point(482, 56);
            Link.Name = "Link";
            Link.Size = new Size(191, 63);
            Link.TabIndex = 9;
            Link.Text = "连接扫描仪";
            Link.UseVisualStyleBackColor = true;
            Link.Click += Link_Click;
            // 
            // Showbox
            // 
            Showbox.Location = new Point(728, 85);
            Showbox.Multiline = true;
            Showbox.Name = "Showbox";
            Showbox.Size = new Size(343, 515);
            Showbox.TabIndex = 8;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(listView2);
            tabPage1.Controls.Add(scanerparametergroupBox);
            tabPage1.Controls.Add(zfclosescanbutton);
            tabPage1.Controls.Add(zfstartscanbutton);
            tabPage1.Controls.Add(linkbutton);
            tabPage1.Controls.Add(exportgroupBox);
            tabPage1.Controls.Add(ScandataparametergroupBox);
            tabPage1.Controls.Add(ZFfilelistBox);
            tabPage1.Controls.Add(zfslabel);
            tabPage1.Location = new Point(4, 33);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1116, 870);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Z+F";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // listView2
            // 
            listView2.Location = new Point(768, 677);
            listView2.Name = "listView2";
            listView2.Size = new Size(327, 165);
            listView2.TabIndex = 31;
            listView2.UseCompatibleStateImageBehavior = false;
            listView2.ColumnClick += listView2_ColumnClick;
            listView2.MouseClick += ListView2_MouseClick;
            // 
            // scanerparametergroupBox
            // 
            scanerparametergroupBox.Controls.Add(IPtextBox);
            scanerparametergroupBox.Controls.Add(IPlabel);
            scanerparametergroupBox.Controls.Add(ResolutioncomboBox);
            scanerparametergroupBox.Controls.Add(Portlabel);
            scanerparametergroupBox.Controls.Add(DataporttextBox);
            scanerparametergroupBox.Controls.Add(resolutionratiolabel);
            scanerparametergroupBox.Controls.Add(ProjectNametextBox);
            scanerparametergroupBox.Controls.Add(CommandporttextBox);
            scanerparametergroupBox.Controls.Add(ProjectNamelabel);
            scanerparametergroupBox.Location = new Point(36, 36);
            scanerparametergroupBox.Name = "scanerparametergroupBox";
            scanerparametergroupBox.Size = new Size(479, 342);
            scanerparametergroupBox.TabIndex = 30;
            scanerparametergroupBox.TabStop = false;
            scanerparametergroupBox.Text = "扫描仪参数设置";
            // 
            // IPtextBox
            // 
            IPtextBox.Location = new Point(132, 43);
            IPtextBox.Name = "IPtextBox";
            IPtextBox.Size = new Size(302, 30);
            IPtextBox.TabIndex = 22;
            IPtextBox.Text = "172.20.4.100";
            // 
            // IPlabel
            // 
            IPlabel.AutoSize = true;
            IPlabel.Location = new Point(37, 46);
            IPlabel.Name = "IPlabel";
            IPlabel.Size = new Size(62, 24);
            IPlabel.TabIndex = 14;
            IPlabel.Text = "IP地址";
            // 
            // ResolutioncomboBox
            // 
            ResolutioncomboBox.FormattingEnabled = true;
            ResolutioncomboBox.Location = new Point(120, 213);
            ResolutioncomboBox.Name = "ResolutioncomboBox";
            ResolutioncomboBox.Size = new Size(314, 32);
            ResolutioncomboBox.TabIndex = 28;
            // 
            // Portlabel
            // 
            Portlabel.AutoSize = true;
            Portlabel.Location = new Point(37, 97);
            Portlabel.Name = "Portlabel";
            Portlabel.Size = new Size(167, 24);
            Portlabel.TabIndex = 15;
            Portlabel.Text = "命令端口+数据端口";
            // 
            // DataporttextBox
            // 
            DataporttextBox.Location = new Point(265, 143);
            DataporttextBox.Name = "DataporttextBox";
            DataporttextBox.Size = new Size(169, 30);
            DataporttextBox.TabIndex = 27;
            DataporttextBox.Text = "6101";
            // 
            // resolutionratiolabel
            // 
            resolutionratiolabel.AutoSize = true;
            resolutionratiolabel.Location = new Point(37, 216);
            resolutionratiolabel.Name = "resolutionratiolabel";
            resolutionratiolabel.Size = new Size(64, 24);
            resolutionratiolabel.TabIndex = 16;
            resolutionratiolabel.Text = "分辨率";
            // 
            // ProjectNametextBox
            // 
            ProjectNametextBox.Location = new Point(159, 289);
            ProjectNametextBox.Name = "ProjectNametextBox";
            ProjectNametextBox.Size = new Size(275, 30);
            ProjectNametextBox.TabIndex = 26;
            ProjectNametextBox.Text = "project_01";
            // 
            // CommandporttextBox
            // 
            CommandporttextBox.Location = new Point(37, 143);
            CommandporttextBox.Name = "CommandporttextBox";
            CommandporttextBox.Size = new Size(167, 30);
            CommandporttextBox.TabIndex = 23;
            CommandporttextBox.Text = "6100";
            // 
            // ProjectNamelabel
            // 
            ProjectNamelabel.AutoSize = true;
            ProjectNamelabel.Location = new Point(37, 292);
            ProjectNamelabel.Name = "ProjectNamelabel";
            ProjectNamelabel.Size = new Size(82, 24);
            ProjectNamelabel.TabIndex = 18;
            ProjectNamelabel.Text = "项目名称";
            // 
            // zfclosescanbutton
            // 
            zfclosescanbutton.Location = new Point(554, 269);
            zfclosescanbutton.Name = "zfclosescanbutton";
            zfclosescanbutton.Size = new Size(169, 70);
            zfclosescanbutton.TabIndex = 21;
            zfclosescanbutton.Text = "结束扫描";
            zfclosescanbutton.UseVisualStyleBackColor = true;
            zfclosescanbutton.Click += zfclosescanbutton_Click;
            // 
            // zfstartscanbutton
            // 
            zfstartscanbutton.Location = new Point(554, 173);
            zfstartscanbutton.Name = "zfstartscanbutton";
            zfstartscanbutton.Size = new Size(169, 70);
            zfstartscanbutton.TabIndex = 20;
            zfstartscanbutton.Text = "开始扫描";
            zfstartscanbutton.UseVisualStyleBackColor = true;
            zfstartscanbutton.Click += zfstartscanbutton_Click;
            // 
            // linkbutton
            // 
            linkbutton.Location = new Point(554, 83);
            linkbutton.Name = "linkbutton";
            linkbutton.Size = new Size(169, 70);
            linkbutton.TabIndex = 19;
            linkbutton.Text = "连接扫描仪";
            linkbutton.UseVisualStyleBackColor = true;
            linkbutton.Click += linkbutton_Click;
            // 
            // exportgroupBox
            // 
            exportgroupBox.Controls.Add(imagewidthcomboBox);
            exportgroupBox.Controls.Add(exporttypelabel);
            exportgroupBox.Controls.Add(imagelabel);
            exportgroupBox.Controls.Add(exporttypecomboBox);
            exportgroupBox.Controls.Add(exportbutton);
            exportgroupBox.Location = new Point(36, 666);
            exportgroupBox.Name = "exportgroupBox";
            exportgroupBox.Size = new Size(686, 176);
            exportgroupBox.TabIndex = 13;
            exportgroupBox.TabStop = false;
            exportgroupBox.Text = "数据导出参数";
            // 
            // imagewidthcomboBox
            // 
            imagewidthcomboBox.FormattingEnabled = true;
            imagewidthcomboBox.Items.AddRange(new object[] { "4000", "5000", "6000", "7000", "8000" });
            imagewidthcomboBox.Location = new Point(192, 115);
            imagewidthcomboBox.Name = "imagewidthcomboBox";
            imagewidthcomboBox.Size = new Size(225, 32);
            imagewidthcomboBox.TabIndex = 12;
            // 
            // imagelabel
            // 
            imagelabel.AutoSize = true;
            imagelabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            imagelabel.Location = new Point(28, 118);
            imagelabel.Name = "imagelabel";
            imagelabel.Size = new Size(118, 24);
            imagelabel.TabIndex = 11;
            imagelabel.Text = "生成影像宽度";
            // 
            // ScandataparametergroupBox
            // 
            ScandataparametergroupBox.Controls.Add(MileageOrdercomboBox);
            ScandataparametergroupBox.Controls.Add(MileageOrderlabel);
            ScandataparametergroupBox.Controls.Add(scanerworktypecomboBox);
            ScandataparametergroupBox.Controls.Add(zftypelabel);
            ScandataparametergroupBox.Controls.Add(ZFtypecomboBox);
            ScandataparametergroupBox.Controls.Add(ScanerWorkTypelabel);
            ScandataparametergroupBox.Controls.Add(inputbutton);
            ScandataparametergroupBox.Location = new Point(36, 398);
            ScandataparametergroupBox.Name = "ScandataparametergroupBox";
            ScandataparametergroupBox.Size = new Size(686, 242);
            ScandataparametergroupBox.TabIndex = 10;
            ScandataparametergroupBox.TabStop = false;
            ScandataparametergroupBox.Text = "扫描数据参数";
            // 
            // MileageOrdercomboBox
            // 
            MileageOrdercomboBox.FormattingEnabled = true;
            MileageOrdercomboBox.Items.AddRange(new object[] { "里程升序", "里程降序" });
            MileageOrdercomboBox.Location = new Point(244, 170);
            MileageOrdercomboBox.Name = "MileageOrdercomboBox";
            MileageOrdercomboBox.Size = new Size(225, 32);
            MileageOrdercomboBox.TabIndex = 11;
            // 
            // MileageOrderlabel
            // 
            MileageOrderlabel.AutoSize = true;
            MileageOrderlabel.Location = new Point(25, 173);
            MileageOrderlabel.Name = "MileageOrderlabel";
            MileageOrderlabel.Size = new Size(136, 24);
            MileageOrderlabel.TabIndex = 12;
            MileageOrderlabel.Text = "扫描仪里程顺序";
            // 
            // scanerworktypecomboBox
            // 
            scanerworktypecomboBox.FormattingEnabled = true;
            scanerworktypecomboBox.Items.AddRange(new object[] { "设站扫描", "移动2D（正向）", "移动2D（反向）" });
            scanerworktypecomboBox.Location = new Point(244, 105);
            scanerworktypecomboBox.Name = "scanerworktypecomboBox";
            scanerworktypecomboBox.Size = new Size(225, 32);
            scanerworktypecomboBox.TabIndex = 10;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1148, 938);
            Controls.Add(ZFScaner);
            Name = "Form1";
            Text = "扫描数据处理器";
            Load += Form1_Load;
            ZFScaner.ResumeLayout(false);
            tabPage2.ResumeLayout(false);
            tabPage2.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            FaroScannergroupBox.ResumeLayout(false);
            FaroScannergroupBox.PerformLayout();
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            scanerparametergroupBox.ResumeLayout(false);
            scanerparametergroupBox.PerformLayout();
            exportgroupBox.ResumeLayout(false);
            exportgroupBox.PerformLayout();
            ScandataparametergroupBox.ResumeLayout(false);
            ScandataparametergroupBox.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Button inputbutton;
        private ListBox ZFfilelistBox;
        private Label zfslabel;
        private Label zftypelabel;
        private ComboBox ZFtypecomboBox; 
        private Button exportbutton;
        private Label exporttypelabel;
        private ComboBox exporttypecomboBox;
        private Label ScanerWorkTypelabel;
        private TabControl ZFScaner;
        private TabPage tabPage1;
        private GroupBox ScandataparametergroupBox;
        private ComboBox scanerworktypecomboBox;
        private Label MileageOrderlabel;
        private ComboBox MileageOrdercomboBox;
        private Label imagelabel;
        private ComboBox imagewidthcomboBox;
        private TabPage tabPage2;
        private Label label2;
        private Label label1;
        private TextBox Showbox;
        private TextBox Namebox;
        private TextBox Frequencybox;
        private TextBox Baudbox;
        private TextBox IPbox;
        private Label label4;
        private Label label3;
        private Button Stop;
        private Button Write;
        private Button Start;
        private Button Link;
        private GroupBox exportgroupBox;
        private Label IPlabel;
        private Label ProjectNamelabel;
        private Label resolutionratiolabel;
        private Label Portlabel;
        private TextBox DataporttextBox;
        private TextBox ProjectNametextBox;
        private TextBox CommandporttextBox;
        private TextBox IPtextBox;
        private Button zfclosescanbutton;
        private Button zfstartscanbutton;
        private Button linkbutton;
        private ComboBox ResolutioncomboBox;
        private GroupBox scanerparametergroupBox;
        private GroupBox FaroScannergroupBox;
        private Label label5;
        private Label label7;
        private Label label6;
        private GroupBox groupBox1;
        private ComboBox FaroMileageOrdercomboBox;
        private ComboBox FaroscanerworktypecomboBox;
        private ComboBox FarotypecomboBox;
        private Button Faroinputbutton;
        private GroupBox groupBox2;
        private Label label9;
        private Label label8;
        private Button Faroexportbutton;
        private ComboBox FaroimagewidthcomboBox;
        private ComboBox FaroexporttypecomboBox;
        private Label label10;
        private ComboBox ScanObjectCodecomboBox;
        private Label label11;
        private ToolTip zhiliangtoolTip;
        private ToolTip pinlvtoolTip;
        private ListView listView1;
        private ListView listView2;
        private ToolTip toolTip1;
        private ToolTip toolTip2;
    }
}
