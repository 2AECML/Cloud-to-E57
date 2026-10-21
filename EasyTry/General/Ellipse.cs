using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace EasyTry.General
{
    public class Ellipse
    {
        //椭圆方程的5个参数
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; }
        public double E { get; set; }
        public double F { get; set; }

        //构造函数
        public Ellipse(double a, double b, double c, double d, double e, double f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        //椭圆拟合静态函数
        public static Ellipse FitEllipse(List<Point> points)
        {
            int n = points.Count;
            if (n < 5)
                throw new ArgumentException("至少需要 5 个点来拟合椭圆。");

            // 构建设计矩阵
            var designMatrix = Matrix<double>.Build.Dense(n, 6);
            var targetVector = Vector<double>.Build.Dense(n);

            for (int i = 0; i < n; i++)
            {
                double x = points[i].X;
                double y = points[i].Y;

                designMatrix[i, 0] = x * x;
                designMatrix[i, 1] = x * y;
                designMatrix[i, 2] = y * y;
                designMatrix[i, 3] = x;
                designMatrix[i, 4] = y;
                designMatrix[i, 5] = 1;

                targetVector[i] = 1; // 目标值为 1
            }

            // 使用最小二乘法求解
            var result = designMatrix.TransposeThisAndMultiply(designMatrix).Inverse() *
                         designMatrix.TransposeThisAndMultiply(targetVector);

            // 返回椭圆参数
            return new Ellipse(result[0], result[1], result[2], result[3], result[4], result[5]);
        }
    }
}
