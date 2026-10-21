using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyTry.General
{
    public class ScanningParameters
    {
        public int Frequency { set; get; }              //频率
        public double Resolution { set; get; }          //角度分辨率
        public int DesignBeginAngle { set; get; }       //固有的开始角度
        public int DesignEndAngle { set; get; }        //固有的结束角度
        public int OutBeginAngle { set; get; }          //输出的开始角度
        public int OutEndAngle { set; get; }           //输出的结束角度
        public int PointsNum { set; get; } = 0;

        //初始化函数
        public ScanningParameters(int frequency, double resolution, int dBeginAngle = 0, int dEndAngle = 360, int outBeginAngle = 0, int outEndAngle = 360, int? num = null)
        {
            Frequency = frequency;
            Resolution = resolution;
            DesignBeginAngle = dBeginAngle;
            DesignEndAngle = dEndAngle;
            OutBeginAngle = outBeginAngle;
            OutEndAngle = outEndAngle;
            if (num != null) PointsNum = num.Value;
            else if (resolution != 0)
                SetPointNum();
        }

        //设置点数方法
        public int SetPointNum()
        {
            double detaAngle = (OutEndAngle - OutBeginAngle);
            if (detaAngle == 0) detaAngle = 360;
            PointsNum = (int)Math.Round(detaAngle / Resolution);
            if (detaAngle != 360) PointsNum++;
            return PointsNum;
        }
    }
}
