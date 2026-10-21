using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyTry.General
{
    //声明扫描仪基本参数类
    internal class ScanStatus
    {
    }

    public class ScanState
    {
        public string ScannerType { get; set; }
        public string ScannerOperationMode { get; set; }

        public string ScanObjectCode { get; set; }
        public string MileageOrder { get; set; }

        public string ScanOrder { get; set; }
        public double ScanAngle { get; set; }
        public int ScanPreviewImageWidth { get; set; }

        public int ScanLines { get; set; }

        public int ScanRows { get; set; }

    }

    public class ScanHeader
    {
        public double Mile { get; set; }
        public float Track { get; set; }
        public int Num { get; set; }
    }

    public class PointXYZI
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double I { get; set; }

        public PointXYZI()
        {
            X = 0;
            Y = 0;
            Z = 0;
            I = 0;
        }

    }

    /// <summary>
    /// Z+F扫描仪分辨率设置
    /// </summary>
    public enum ZFResolution
    {
        [Description("l")]
        Low,

        [Description("h")]
        High,

        [Description("sh")]
        SuperHigh
    }

    /// <summary>
    /// Z+F扫描仪转速设置
    /// </summary>
    public enum ZFFrequency
    {
        [Description("h")]
        Frequency50,

        [Description("n")]
        Frequency100,

        [Description("l")]
        Frequency200
    }

    /// <summary>
    ///  连接设备类型
    /// </summary>
    public enum ControllerType
    {
        [Description("None")]
        None,

        [Description("Faro")]
        Faro,

        [Description("ZF")]
        ZF,

        [Description("Leica")]
        Leica,

        [Description("IMU")]
        IMU,

        [Description("轨距")]
        Track,

        [Description("Sick")]
        Sick,

        [Description("FaroP")]
        FaroP
    }

    /// <summary>
    /// 通用圆形衬砌环，5+1块管片，通缝或错缝
    /// </summary>
    public enum ScanObjectCode
    {
        /// <summary>
        /// 单圆
        /// </summary>
        [Description("单圆")]
        Circle,

        /// <summary>
        /// 马蹄形隧道
        /// </summary>
        [Description("马蹄形隧道")]
        Horseshoe,

        /// <summary>
        /// 矩形隧道
        /// </summary>
        [Description("矩形隧道")]
        Rectangle,

        /// <summary>
        /// 大圆带中隔墙
        /// </summary>
        [Description("大圆")]
        BigCircle,

        /// <summary>
        /// 其它形式
        /// </summary>
        [Description("其它形式")]
        Others,

        /// <summary>
        /// 双圆
        /// </summary>
        [Description("双圆")]
        DoubleCircle,

        /// <summary>
        /// 道床
        /// </summary>
        [Description("道床")]
        TrackBalt,

        /// <summary>
        /// 铁路
        /// </summary>
        [Description("铁路")]
        Railway,

        /// <summary>
        /// 公路
        /// </summary>
        [Description("公路")]
        Road
    }
}
