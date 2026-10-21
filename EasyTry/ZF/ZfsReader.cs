using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using zfs;
using static System.Windows.Forms.LinkLabel;

using ZfsSdkRef;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Spatial.Euclidean;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.ComponentModel;
using System.Windows.Forms;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Net.Http.Headers;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using Microsoft.VisualBasic.Devices;
using EasyTry.General;
using static EasyTry.General.PointCloudProcessor;
using System.Globalization;

namespace EasyTry.ZF
{
    public class ZfsReader
    {
        //*************** 声明逐行读取的属性 ***************
        private ZfsLineByLineRef LineByLine { get; set; }

        //声明基础参数
        public int ScanCols { get; protected set; }
        public int ScanRows { get; protected set; }
        public double ScanAngle { get; protected set; }

        //SDK的许可属性
        private static bool IsInitialed { get; set; }

        //扫描文件中的行数
        public int Lines => LineByLine is null ? int.MaxValue : LineByLine.Lines();


        public int StartSlice { get; set; } = 0;
        public int EndSlice { get; set; } = 9999;

        public double SliceWidth { get; set; } = 0.005;


        //--------------------函数：读取Z+F扫描文件（一次只能读取一个zfs文件）--------------------
        public void ZfsReaderFile(FileInfo zfsFileInfo, ScanState scanstate, out List<PointXYZI[]> PointsXYZI)
        {
            //读取一份扫描数据中的全部数据
            if (!IsInitialed)
                IsInitialed = ZfsRef.SetLicense("TongjiUniversity", "unlimited", "b1dda277");
            if (!IsInitialed)
                throw new Exception();

            PointsXYZI = new List<PointXYZI[]>();

            //只考虑盾构单圆类型
            OpenFlags flags = OpenFlags.OPEN_SINGLE;

            switch (scanstate.ScannerOperationMode)
            {
                case "移动2D（正向）":
                case "移动2D（反向）":

                    flags = OpenFlags.OPEN_IGNOR_LEVEL | OpenFlags.OPEN_IGNOR_COMPENSATOR;
                    break;
                case "设站扫描":
                    flags = OpenFlags.OPEN_SINGLE;
                    break;
                default:
                    break;
            }

            //读入zfs文件
            LineByLine = ZfsLineByLineRef.OpenLineByLineRef(zfsFileInfo.FullName, flags);

            //设置行数和列数（为了统一预览影像的宽度设置成定值）
            ScanCols = 5000;
            switch (scanstate.ScannerType)
            {
                case "ZF50XX":
                    ScanRows = LineByLine.Pixel();
                    ScanAngle = 320;
                    break;
                default:
                    ScanRows = LineByLine.Pixel() >= 10200 ? LineByLine.Pixel() - 40 : LineByLine.Pixel() - 20;// 存在部分空值点
                    ScanAngle = 360;
                    break;
            }

            //写入部分扫描状态量
            scanstate.ScanAngle = ScanAngle;
            scanstate.ScanRows = ScanRows;
            scanstate.ScanLines = Lines;



            StartSlice = 11249;
            EndSlice = 14205; //全部断面应该是Lines



            //读取全部扫描数据
            for (int colIndex = StartSlice; colIndex <= EndSlice; colIndex++) //将Lines改成2
            {
                GetRawSlice(colIndex, out PointXYZI[] pointsInLine);

                PointsXYZI.Add(pointsInLine);
            }
        }


        //--------------------函数：读取原始ZF扫描线，不考虑前后里程和朝向-------------------------
        private void GetRawSlice(int colIndex, out PointXYZI[] PointsInLine, bool fileter = true)
        {
            //检查传入的列索引是否合法
            if (colIndex < 0 || colIndex >= Lines)
                throw new ArgumentOutOfRangeException(nameof(colIndex));

            //初始化输出参数为指定长度的数组ScanRows缩小为20
            PointsInLine = new PointXYZI[ScanRows];


            //**********************  一定对数组中的类进行初始化  **********************
            for (int i = 0; i < PointsInLine.Length; i++)
            {
                PointsInLine[i] = new PointXYZI();  // 为每个数组元素创建一个新的PointXYZI实例
            }

            LineByLine.SkipTo(colIndex); // 从指定环开始读入扫描线

            if (fileter) //默认filter为真
                LineByLine.Filter();

            //读取一列中的各个扫描点
            for (var i = 0; i < ScanRows; i++)
            {
                //将指针设置为当前行的像素位置
                LineByLine.Set(i); // set pointer to pixel

                //计算反射值的归一化值rfNorm RfNormProcessed()
                double rfNorm = LineByLine.RfNormProcessed();

                //int rf = LineByLine.Rf();
                //int minRf = LineByLine.MinRf();
                //int maxRf = LineByLine.MaxRf();

                //检查rfNorm是否在0到1的范围内
                bool rfValid = rfNorm >= 0 && rfNorm <= 1;

                //把rfNorm小于0的值调整为0，大于1的值调整为1
                if (rfNorm < 0) rfNorm = 0;
                else if (rfNorm > 1) rfNorm = 1;

                //*********将强度值转化为色彩值*********
                //byte rfNormadjust = RfNormAdjust(rfNorm, 0);

                //if (rfNorm <= 0) rfNormadjust = 0;
                //else if (rfNorm >= 1) rfNormadjust = 255;

                if (LineByLine.IsSet(i) && rfValid)
                {
                    (double x, double y, double z) = LineByLine.XYZ();

                    x = Math.Round(x, 5);
                    y = Math.Round(y, 5);
                    z = Math.Round(z, 5);

                    PointsInLine[i].X = x;
                    PointsInLine[i].Y = y;
                    PointsInLine[i].Z = z;
                    PointsInLine[i].I = rfNorm * 255;
                }
                else
                {
                    PointsInLine[i].X = double.NaN;
                    PointsInLine[i].Y = double.NaN;
                    PointsInLine[i].Z = double.NaN;
                    PointsInLine[i].I = rfNorm * 255;
                }
            }
        }


        //--------------------函数：对归一化的反射值进行线性拉伸（图像增强方法）--------------------
        public static byte RfNormAdjust(double uniformRF, int colorType)
        {
            double adjustNorm = 0.0;

            if (colorType > 2 || colorType < 0)
            {
                colorType = 0; // 其它参数暂不支持，默认设置为0
            }

            // 默认配色方案，一般运营地铁效果良好
            if (colorType == 0)
            {
                if (uniformRF < 0.02)
                {
                    adjustNorm = (uniformRF + 0.01) * 10;
                }
                else if (uniformRF >= 0.02 && uniformRF < 0.1)
                {
                    adjustNorm = 0.30 + (uniformRF - 0.02) * 6;
                }
                else if (uniformRF >= 0.1 && uniformRF < 0.2)
                {
                    adjustNorm = 0.78 + (uniformRF - 0.1) * 1.5;
                }
                else if (uniformRF >= 0.2 && uniformRF < 0.3)
                {
                    adjustNorm = 0.93 + (uniformRF - 0.2) * 0.5;
                }
                else
                {
                    adjustNorm = 0.98 + (uniformRF - 0.3) / 0.7 * 0.02;
                }
            }

            if (colorType == 1)
            {
                if (uniformRF < 0.02)
                {
                    adjustNorm = uniformRF * 6;
                }
                else if (uniformRF >= 0.02 && uniformRF < 0.10)
                {
                    adjustNorm = 0.12 + uniformRF * 4;
                }
                else if (uniformRF >= 0.10 && uniformRF < 0.35)
                {
                    adjustNorm = 0.52 + (uniformRF - 0.10) * 1.5;
                }
                else if (uniformRF >= 0.35 && uniformRF < 0.40)
                {
                    adjustNorm = 0.895 + (uniformRF - 0.35) * 1.0;
                }
                else
                {
                    adjustNorm = 0.945 + (uniformRF - 0.40) / 0.6 * 0.055;
                }
            }

            if (colorType == 2)
            {
                if (uniformRF < 0.02)
                {
                    adjustNorm = uniformRF * 4;
                }
                else if (uniformRF >= 0.02 && uniformRF < 0.05)
                {
                    adjustNorm = 0.08 + (uniformRF - 0.02) * 4;
                }
                else if (uniformRF >= 0.05 && uniformRF < 0.10)
                {
                    adjustNorm = 0.20 + (uniformRF - 0.05) * 3.0;
                }
                else if (uniformRF >= 0.10 && uniformRF < 0.50)
                {
                    adjustNorm = 0.35 + (uniformRF - 0.10) * 1.5;
                }
                else
                {
                    adjustNorm = 0.95 + (uniformRF - 0.50) / 0.5 * 0.05;
                }
            }

            return (byte)(adjustNorm * 255);
        }


        //--------------------函数： 将 XYZI_all 输出到 TXT 文件-------------------------------
        public void WriteXYZIToTxt(FileInfo FilePath, List<PointXYZI[]> pointsXYZI)
        {

            // === 新增：根据开始环和结束环生成输出文件名 ===
            string baseName = Path.GetFileNameWithoutExtension(FilePath.Name);
            string ringSuffix = (StartSlice == EndSlice)
                ? $"_R{StartSlice}"
                : $"_R{StartSlice}-{EndSlice}";
            string outName = baseName + ringSuffix + ".txt";
            string filePath = Path.Combine(FilePath.DirectoryName ?? "", outName);

            try
            {
                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    // ===== 文件头 =====
                    writer.WriteLine("# 来源: " + FilePath.Name);
                    writer.WriteLine("# 断面间距: " + SliceWidth.ToString("F3", CultureInfo.InvariantCulture));
                    writer.WriteLine("# 开始断面: " + StartSlice.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("# 结束断面: " + EndSlice.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("# 列: X,Y,Z,I");
                    writer.WriteLine(); // 空行分隔头与正文

                    // ===== 正文：Y 为环号，从 StartRing 递增到 EndRing =====
                    // pointsXYZI.Count 通常为 (EndRing - StartRing + 1)
                    for (int i = 0; i < pointsXYZI.Count; i++)
                    {
                        int currRing = StartSlice + i; // 当前环号
                        //string yStr = currRing.ToString(CultureInfo.InvariantCulture);
                        string yStr = Math.Round((StartSlice + i) * SliceWidth, 3, MidpointRounding.AwayFromZero).ToString("F3", CultureInfo.InvariantCulture);

                        for (int j = 0; j < pointsXYZI[i].Length; j++)
                        {
                            writer.Write(
                                pointsXYZI[i][j].Y.ToString("G17", CultureInfo.InvariantCulture) + "," +
                                yStr + "," +
                                pointsXYZI[i][j].Z.ToString("G17", CultureInfo.InvariantCulture) + "," +
                                pointsXYZI[i][j].I.ToString(CultureInfo.InvariantCulture)
                            );
                            writer.WriteLine();
                        }
                    }

                    MessageBox.Show("数据已成功输出到文件：" + filePath, "操作成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("没有权限访问该路径，请检查权限设置。");
            }

        }


        //--------------------函数： 生成强度影像(基本实现步骤)----------------------------------
        public void CreateIntensityImage(string imagefilepath, List<PointXYZI[]> pointsXYZI, double resolution)
        {
            //创建影像的存储路径
            string path = imagefilepath + "\\intensity_image.png";


            double minx = double.MaxValue;
            double miny = double.MaxValue;
            double mini = double.MaxValue;

            double maxx = double.MinValue;
            double maxy = double.MinValue;
            double maxi = double.MinValue;

            for (int i = 0; i < pointsXYZI.Count; i++)
            {
                for (int j = 0; j < pointsXYZI[i].Length; j++)
                {
                    minx = Math.Min(minx, pointsXYZI[i][j].X);
                    miny = Math.Min(miny, pointsXYZI[i][j].Y);

                    maxx = Math.Max(maxx, pointsXYZI[i][j].X);
                    maxy = Math.Max(maxy, pointsXYZI[i][j].Y);

                    maxi = Math.Max(maxi, pointsXYZI[i][j].I);
                }
            }

            double lx = maxx - minx;                            //点云长度
            double ly = maxy - miny;                            //点云宽度
            int rows = (int)Math.Round(ly / resolution);        //图像高度
            int cols = (int)Math.Round(lx / resolution);        //图像宽度

            Mat img = new Mat(rows, cols, MatType.CV_8UC3, new Scalar(0, 0, 0));

            //声明强度格网矩阵
            double[,] gridIntensity = new double[rows, cols];

            //初始化强度网格矩阵
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    gridIntensity[i, j] = 0;
                }
            }

            //声明强度网格矩阵索引
            int m = 0;
            int n = 0;

            //将点压入所在网格
            for (int i = 0; i < pointsXYZI.Count; i++)
            {
                for (int j = 0; j < pointsXYZI[i].Length; j++)
                {
                    //此处存疑
                    m = (int)Math.Round((maxy - pointsXYZI[i][j].Y) / resolution);
                    n = (int)Math.Round((pointsXYZI[i][j].X - minx) / resolution);

                    if (m >= 0 && m < rows && n >= 0 && n < cols)
                    {
                        gridIntensity[m, n] = Math.Max(pointsXYZI[i][j].I, gridIntensity[m, n]);
                    }

                }
            }

            //强度拉伸到0-255之间赋值
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (gridIntensity[i, j] > 0)
                    {
                        int pixel = (int)(gridIntensity[i, j] / maxi * 255);

                        img.At<Vec3b>(i, j) = new Vec3b((byte)pixel, (byte)pixel, (byte)pixel);
                    }
                }
            }

            Cv2.ImWrite(path, img);

            //释放内存
            img.Dispose();

            MessageBox.Show("图像于" + path + "成功生成");
        }


        //--------------------函数： 生成强度影像(TLSD)----------------------------------------
        public void CreateIntensityImageTLSD(string ImagePath, List<PointXYZI[]> PointsXYZI, ScanState scanstate, int colStartIndex, int previewImageWidth)
        {
            //增加强度数据调整循环



            //创建影像的存储路径
            string path = ImagePath;

            colStartIndex = colStartIndex < Lines ? colStartIndex : Lines;

            //colEndIndex = colEndIndex < Lines ? colEndIndex : Lines; // 自动读取至最后一个扫描线

            //基于传入的数据扫描线数
            int colEndIndex = colStartIndex + previewImageWidth;

            //加载用于生成影像的信息
            //设置宽高
            //int imageWidth = previewImageWidth; // 使用统一的影像宽度，避免最后一幅的宽度不足问题

            int imageWidth = previewImageWidth;

            //判断剩余扫描数据是否够预定影像的宽度
            if (PointsXYZI.Count - colStartIndex < previewImageWidth)
            {
                imageWidth = PointsXYZI.Count - colStartIndex;
                colEndIndex = colStartIndex + imageWidth;
            }

            int imageHeight = ScanRows;

            //声明强度数据容器
            double[,] intensitys = new double[ScanRows, imageWidth];

            //确认里程顺序和扫描模式
            var mode = scanstate.ScannerOperationMode;
            var mileageOrder = scanstate.MileageOrder;

            //进行坐标转换
            for (int colIndex = colStartIndex; colIndex < colEndIndex; colIndex++)
            {
                if (mode == "移动2D（正向）" || mode == "移动2D（反向）")
                {
                    int rgbColIndex = 0;

                    if (mileageOrder == "里程升序")
                        rgbColIndex = colIndex - colStartIndex;
                    else if (mileageOrder == "里程降序")
                        rgbColIndex = imageWidth - (colIndex - colStartIndex) - 1;

                    //存储每行读取出来的数据的强度值
                    for (int rowIndex = 0; rowIndex < ScanRows; rowIndex++)
                    {
                        //设置行索引，并根据扫描的方向进行初始化
                        int rgbRowIndex = mode == "移动2D（正向）" ? rowIndex : ScanRows - rowIndex - 1;

                        //将强度值写入到用于创建图像的数据源中(相当于转置)
                        intensitys[rgbRowIndex, rgbColIndex] = PointsXYZI[colIndex][rowIndex].I;
                    }
                }
            }

            //新建空白图像
            Mat mat = new Mat(imageHeight, previewImageWidth, MatType.CV_8UC1, new Scalar(255));

            //将强度值数据写入到图像
            for (int i = 0; i < imageHeight; i++)
            {
                for (int j = 0; j < imageWidth; j++)
                {
                    //之后进行强制转化修改
                    mat.At<byte>(i, j) = (byte)intensitys[i, j];
                }
            }

            //将生成的图像保存
            Cv2.ImWrite(path, mat);

            //释放内存
            mat.Dispose();

        }
    }
}
