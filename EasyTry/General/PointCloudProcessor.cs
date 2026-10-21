using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra; // 引入 MathNet.Numerics 进行矩阵运算

namespace EasyTry.General
{
    public class PointCloudProcessor
    {
        public struct CircleParams
        {
            public double Yc;   // 圆心 Y
            public double Zc;   // 圆心 Z
            public double R;    // 半径
            public bool IsValid;

            public override string ToString()
                => $"Center=({Yc:F3},{Zc:F3}), R={R:F3}";
        }

        public static class TunnelCircleFitter
        {
            /// <summary>
            /// 批量清洗隧道点云（圆拟合 + 半径残差过滤）
            /// </summary>
            public static List<PointXYZI[]> CleanTunnelSectionsCircle(
                List<PointXYZI[]> sections,
                out List<CircleParams> perSectionCircle,
                double residualThresholdMeters = 0.05) // 默认 5cm 容差
            {
                perSectionCircle = new List<CircleParams>(sections.Count);
                var cleaned = new List<PointXYZI[]>(sections.Count);

                foreach (var sec in sections)
                {
                    var pts = sec.ToList();
                    if (pts.Count < 5)
                    {
                        perSectionCircle.Add(new CircleParams { IsValid = false });
                        cleaned.Add(sec); // 点太少，直接保留
                        continue;
                    }

                    var circle = FitCircle(pts);
                    if (!circle.IsValid)
                    {
                        perSectionCircle.Add(circle);
                        cleaned.Add(sec); // 拟合失败，保留原始
                        continue;
                    }

                    perSectionCircle.Add(circle);

                    // 按残差过滤
                    var inliers = new List<PointXYZI>();
                    foreach (var p in pts)
                    {
                        double r = Math.Sqrt((p.Y - circle.Yc) * (p.Y - circle.Yc) +
                                             (p.Z - circle.Zc) * (p.Z - circle.Zc));
                        if (Math.Abs(r - circle.R) <= residualThresholdMeters)
                            inliers.Add(p);
                    }

                    cleaned.Add(inliers.ToArray());
                }

                return cleaned;
            }

            /// <summary>
            /// 圆拟合（均值做圆心，平均半径）
            /// </summary>
            private static CircleParams FitCircle(List<PointXYZI> pts)
            {
                double my = pts.Average(p => p.Y);
                double mz = pts.Average(p => p.Z);

                double r = 0;
                foreach (var p in pts)
                {
                    r += Math.Sqrt((p.Y - my) * (p.Y - my) + (p.Z - mz) * (p.Z - mz));
                }
                r /= pts.Count;

                if (double.IsNaN(r) || r <= 0) return new CircleParams { IsValid = false };

                return new CircleParams { Yc = my, Zc = mz, R = r, IsValid = true };
            }
        }

    }
    
}
