using EasyTry.General;
using IQOPENLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MathNet.Numerics.LinearAlgebra;
using MathNet.Spatial.Euclidean;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using static System.Runtime.InteropServices.JavaScript.JSType;
using LSSDKLib;
using OpenCvSharp;
using System.Globalization;

using System.Globalization;

namespace EasyTry.Faro
{
    public class FlsReader
    {
        private IiQLibIf LibRef { get; set; }

        //声明基础参数
        public int ScanCols { get; protected set; }
        public int ScanRows { get; protected set; }
        public double ScanAngle { get; protected set; }

        public int FileIndex { get; set; } = -1;

        public double AddConstCorrection { get; set; } = 0;
        public double MultiplyConstCorrection { get; set; } = 1;
        private int HalfScanRows { get; set; }

        public int StartRing { get; set; } = 0;
        public int EndRing { get; set; } = 9999;

        public double RingWidth { get; set; } = 0.005;

        //--------------------函数：读取Faro扫描文件（一次只能读取一个zfs文件）默认5000列--------------------
        public void FlsReaderFile(FileInfo zfsFileInfo, ScanState scanstate, out List<PointXYZI[]> PointsXYZI, string licenseKey = "664WL6NRTCKXXKPT6KZCSPLPJ")
        {
            //编写许可证代码
            var licenseCode = "FARO Open Runtime License\n"
                                 + "Key:"
                                 + licenseKey
                                 + "\n"
                                 + "\n"
                                 + "The software is the registered property of "
                                 + "FARO Scanner Production GmbH, Stuttgart, Germany.\n"
                                 + "All rights reserved.\n"
                                 + "This software may only be used with written permission "
                                 + "of FARO Scanner Production GmbH, Stuttgart, Germany.";
            
            //初始化返回值数据
            PointsXYZI = new List<PointXYZI[]>();

            try
            {
                //写入许可证代码
                IiQLicensedInterfaceIf licLibIf = new iQLibIfClass();
                licLibIf.License = licenseCode;
                LibRef = (IiQLibIf)licLibIf;

                //写入读取文件路径
                string fileName;

                fileName = zfsFileInfo.FullName;
                if (!File.Exists(fileName)) return;
                LibRef.load(fileName);

                //选择扫描项目
                switch (scanstate.ScanObjectCode)
                {
                    case "道床":
                        LibRef.scanReflectionMode = 0;//mode = 0,0-2047;mode = 1 ,0-255
                        break;
                    default:
                        LibRef.scanReflectionMode = 1;
                        break;
                }

                //读取扫描基本变量
                
                ScanCols = LibRef.getScanNumCols(0)/2;//默认5000列
                ScanRows = LibRef.getScanNumRows(0) * 2;
                HalfScanRows = LibRef.getScanNumRows(0);

                #region 更新扫描角度，Faro S150在1/4分辨率，1X质量时出现304°+8650点，与常规点数8534（6）+300°参数不一致
                LibRef.getPolarScanPoints2(0, 0, 0, HalfScanRows, out Array posLeft, out Array reflectionLeft);//左侧数据
                LibRef.getPolarScanPoints2(0, 0, 0 + ScanCols, HalfScanRows, out Array posRight, out Array reflectionRight);//右侧数据

                var leftThetaList = new List<double>(HalfScanRows);
                for (int iRow = 0; iRow < HalfScanRows; iRow++)
                {
                    var theta = (double)posLeft.GetValue(iRow * 3 + 2);
                    leftThetaList.Add(theta);
                }
                var rightThetaList = new List<double>(HalfScanRows);
                for (int iRow = 0; iRow < HalfScanRows; iRow++)
                {
                    var theta = (double)posRight.GetValue(iRow * 3 + 2);
                    rightThetaList.Add(theta);
                }
                ScanAngle = Math.Round((leftThetaList.Max() - leftThetaList.Min() + rightThetaList.Max() - rightThetaList.Min()) / Math.PI * 180.0, 4);
                #endregion

            }
            catch (Exception exc)
            {
                if (exc.Message.Contains("找不到指定的程序"))
                    MessageBox.Show("加载SDK出错，请尝试重启程序！");

                else
                    MessageBox.Show("加载fls文件出错！\n" + exc.Message + "\n" + zfsFileInfo.FullName);
            }

            if (ScanCols == 0)
            {
                MessageBox.Show("fls文件不支持或已破坏，无法读取！");
                return;//fls文件读取失败时，不生成影像
            }

            if (ScanCols > 10 * 1000)
                MessageBox.Show("单个fls扫描线超过20000线，预览时间较长！");

            var mileageOrder = scanstate.MileageOrder;
            string scanMode = scanstate.ScannerOperationMode;
            string objectCode = scanstate.ScanObjectCode;

            //判断两个变量值
            bool mirror = scanMode == "移动2D（正向）";
            bool reverse = scanMode != "移动2D（正向）";

            int columnCount = ScanCols;



            StartRing = 0;
            EndRing = 100;



            // 控制环的读取范围
            for (int iCol = StartRing; iCol <= EndRing; iCol++)
            {
                //新建一行点存储容器
                PointXYZI[] pointsInLine = new PointXYZI[ScanRows];

                //逐行读取数据
                (var xy, var reflections) = GetRawSlice(iCol, mirror, reverse);

                //进行灰度值拉伸增强
                //var reflectionsAdjust = FaroGrayScaleAdjust(reflections);

                //根据里程顺序，调整行的顺序
                int rgbColIndex = mileageOrder == "里程升序" ? iCol : ScanCols - iCol - 1;

                //进行循环添加
                for (int rowIndex = 0; rowIndex < ScanRows; rowIndex++)
                {
                    //数组中装着自定义的类，数组中的每一个元素都需要进行实例化
                    pointsInLine[rowIndex] = new PointXYZI();
                    pointsInLine[rowIndex].X = xy[rowIndex, 0];
                    pointsInLine[rowIndex].Y = xy[rowIndex, 1];
                    pointsInLine[rowIndex].I = (byte)reflections[rowIndex];
                }
                PointsXYZI.Add(pointsInLine);
            }
        }

        //--------------------函数：重新组织和输出读取到的扫描数据-------------------------
        //函数返回一个关于XY的二维矩阵和一个强度值数组
        private (Matrix<double> xy, int[] intensity) GetRawSlice(int colIndex, bool mirror, bool reverse, double? addConst = null, double? multiplyConst = null, int scanIndex = 0)
        {
            int mirrorData = mirror ? -1 : 1;
            //左边列索引为函数传进来的列索引
            int colLeftIndex = colIndex;
            //右边列索引为左边列索引加上总共的扫描列数
            int colRightIndex = colLeftIndex + ScanCols;

            //创建一个ScanRows行，2列的密集矩阵
            Matrix<double> xy = Matrix<double>.Build.Dense(ScanRows, 2);
            //创建强度值数组
            int[] intensity = new int[ScanRows];

            //分成左右两部分进行数据组织
            GetXYZScanPoints(scanIndex, 0, colLeftIndex, HalfScanRows, out Point3D[] posLeft, out int[] reflectionLeft, addConst, multiplyConst);//左侧数据
            GetXYZScanPoints(scanIndex, 0, colRightIndex, HalfScanRows, out Point3D[] posRight, out int[] reflectionRight, addConst, multiplyConst);//右侧数据
            for (int iRow = 0; iRow < HalfScanRows; iRow++)
            {
                //HalfScanRows为起点开始读取
                int rgbRowIndex = (!reverse) ? (HalfScanRows + iRow) : (HalfScanRows - iRow - 1);//右上，左下
                intensity[rgbRowIndex] = reflectionLeft[iRow];
                Point3D point = posLeft[iRow];
                xy[rgbRowIndex, 0] = point.X * mirrorData;
                xy[rgbRowIndex, 1] = point.Z;
            }
            for (int iRow = 0; iRow < HalfScanRows; iRow++)
            {
                int rgbRowIndex = (!reverse) ? (HalfScanRows - iRow - 1) : (HalfScanRows + iRow);
                intensity[rgbRowIndex] = reflectionRight[iRow];
                Point3D point = posRight[iRow];
                xy[rgbRowIndex, 0] = point.X * mirrorData;
                xy[rgbRowIndex, 1] = point.Z;
            }
            return (xy, intensity);
        }

        //--------------------函数：读取Faro扫描点数据-------------------------
        private void GetXYZScanPoints(int scan, int row, int col, int numRows, out Point3D[] points, out int[] reflections, double? addConst = null, double? multiplyConst = null)
        {
            //addConst是加常数,如果函数参数传进来为空，则用一般值
            if (addConst == null) addConst = AddConstCorrection;

            //multiplyConst是乘常数，如果函数参数传进来为空，则用一般值
            if (multiplyConst == null) multiplyConst = MultiplyConstCorrection;

            //声明两个数组,用来存放点和强度值
            Array posArray, reflectionArray;

            //如果不进行加常数和乘常数修正，就调用输出直角坐标的方法
            if (addConst == 0 && multiplyConst == 1)
                //通过这个方法拿到了点的序列和反射强度，之后就剩组织和输出
                LibRef.getXYZScanPoints2(scan, row, col, numRows, out posArray, out reflectionArray);
            else
                //其他情况调用输出极坐标的方法
                LibRef.getPolarScanPoints2(scan, row, col, numRows, out posArray, out reflectionArray);

            //这两个是输出参数，这里进行初始化
            points = new Point3D[reflectionArray.Length];
            reflections = new int[reflectionArray.Length];

            //向输出参数中，添加读取到的扫描数据
            for (int iRow = 0; iRow < reflectionArray.Length; iRow++)
            {
                Point3D point;

                //两种不同坐标系下坐标
                if (addConst == 0 && multiplyConst == 1)
                {
                    double x = (double)posArray.GetValue(iRow * 3 + 0);
                    double y = (double)posArray.GetValue(iRow * 3 + 1);
                    double z = (double)posArray.GetValue(iRow * 3 + 2);
                    point = new Point3D(x, y, z);
                }
                else
                {
                    double r = (double)posArray.GetValue(iRow * 3 + 0);
                    if (addConst != null && multiplyConst != null && r > 0)
                        r = addConst.Value + multiplyConst.Value * r;
                    double phi = (double)posArray.GetValue(iRow * 3 + 1);
                    double theta = (double)posArray.GetValue(iRow * 3 + 2);
                    point = PolarToXYZ(r, phi, theta);
                }
                points[iRow] = point;
                reflections[iRow] = (int)reflectionArray.GetValue(iRow);
            }
        }

        //--------------------函数：根据SDK定义,将极坐标转化到直角坐标系下-------------------------
        private Point3D PolarToXYZ(double r, double phi, double theta)
        {
            double x = r * Math.Cos(phi) * Math.Cos(theta);
            double y = -r * Math.Sin(phi) * Math.Cos(theta);
            double z = r * Math.Sin(theta);
            return new Point3D(x, y, z);
        }

        //--------------------函数： Faro生成强度影像(TLSD)----------------------------------------
        public void FaroCreateIntensityImageTLSD(string ImagePath, List<PointXYZI[]> PointsXYZI, ScanState scanstate, int colStartIndex, int previewImageWidth)
        {
            //创建影像的存储路径
            string path = ImagePath;

            colStartIndex = colStartIndex < ScanCols ? colStartIndex : ScanCols;

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
                    //之后修改强制转化
                    mat.At<byte>(i, j) = (byte)intensitys[i, j];
                }
            }

            //将生成的图像保存
            Cv2.ImWrite(path, mat);

            //释放内存
            mat.Dispose();

        }

        //--------------------函数： 将 XYZI_all 输出到 TXT 文件（带文件头 & Y为环号）-------------------------------
        public void FaroWriteXYZIToTxt(FileInfo FilePath, List<PointXYZI[]> pointsXYZI)
        {
            // === 新增：根据开始环和结束环生成输出文件名 ===
            string baseName = Path.GetFileNameWithoutExtension(FilePath.Name);
            string ringSuffix = (StartRing == EndRing)
                ? $"_R{StartRing}"
                : $"_R{StartRing}-{EndRing}";
            string outName = baseName + ringSuffix + ".txt";
            string filePath = Path.Combine(FilePath.DirectoryName ?? "", outName);


            try
            {
                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    // ===== 文件头 =====
                    writer.WriteLine("# 来源: " + FilePath.Name);
                    writer.WriteLine("# 断面间距: " + RingWidth.ToString("F3", CultureInfo.InvariantCulture));
                    writer.WriteLine("# 开始断面: " + StartRing.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("# 结束断面: " + EndRing.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("# 列: X,Y,Z,I");
                    writer.WriteLine(); // 空行分隔头与正文

                    // ===== 正文：Y 为环号，从 StartRing 递增到 EndRing =====
                    // pointsXYZI.Count 通常为 (EndRing - StartRing + 1)
                    for (int i = 0; i < pointsXYZI.Count; i++)
                    {
                        int currRing = StartRing + i; // 当前环号
                        //string yStr = currRing.ToString(CultureInfo.InvariantCulture);
                        string yStr = Math.Round((StartRing + i) * RingWidth, 3, MidpointRounding.AwayFromZero).ToString("F3", CultureInfo.InvariantCulture);

                        for (int j = 0; j < pointsXYZI[i].Length; j++)
                        {
                            writer.Write(
                                pointsXYZI[i][j].X.ToString("G17", CultureInfo.InvariantCulture) + "," +
                                yStr + "," +
                                pointsXYZI[i][j].Y.ToString("G17", CultureInfo.InvariantCulture) + "," +
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


        //--------------------函数： 对强度值进行线性拉伸（只针对单圆盾构隧道）-------------------------------
        private byte[] FaroGrayScaleAdjust(int[] srcReflection)
        {
            if ((srcReflection == null) || (srcReflection.Length == 0))
                return null;

            byte[] result = new byte[srcReflection.Length];

            int maxReflection = 255;
            int scaleInterval = 8;//灰度插值点间隔

            Matrix<double> table = Matrix<double>.Build.Dense(33, 2);
            //黑色
            table[0, 0] = 0; //输入值
            table[0, 1] = 0; //输出值
            //分段灰度
            for (int iRow = 1; iRow <= 31; iRow++)
            {
                table[iRow, 0] = scaleInterval * iRow;
            }
            //区间2
            for (int iRow = 1; iRow <= 5; iRow++)
            {
                table[iRow, 1] = table[iRow, 0] * 0.97;
            }
            //区间3
            for (int iRow = 6; iRow <= 13; iRow++)
            {
                table[iRow, 1] = table[iRow, 0] * 0.85;
            }
            //区间4,拉伸
            for (int iRow = 14; iRow <= 28; iRow++)
            {
                table[iRow, 1] = (table[iRow, 0] - 40) * 1.15;//略调暗，增加对比度
            }
            //区间5
            for (int iRow = 29; iRow <= 31; iRow++)
            {
                table[iRow, 1] = table[iRow, 0] * 0.98;
            }
            //白色
            table[32, 0] = maxReflection;
            table[32, 1] = maxReflection;

            //使用spline样条曲线插值
            var spline = MathNet.Numerics.Interpolation.LinearSpline.InterpolateSorted(table.Column(0).ToArray(), table.Column(1).ToArray());

            for (int idx = 0; idx < srcReflection.Length; idx++)
            {
                result[idx] = (byte)spline.Interpolate(srcReflection[idx]);//样条插值
            }

            return result;
        }
    }
}
