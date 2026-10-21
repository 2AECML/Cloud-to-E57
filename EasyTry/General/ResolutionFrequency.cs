using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyTry.General
{
    public static class ResolutionFrequency
    {
        //this：这关键字用于声明一个扩展方法。
        //它的作用是将该方法扩展到 ScannerIdent 类型，使得你可以像调用实例方法一样调用它。
        public static string GetResolutionCommand(this ScannerIdent scanner, int resolution)
        {
            if (scanner == ScannerIdent.Zf9020)
                return (resolution / 1000 * 1000).ToString();
            if (scanner == ScannerIdent.Zf9012)
            {
                switch (resolution)
                {
                    case 20480:
                        return "sh";
                    case 10240:
                        return "h";
                    case 5120:
                        return "l";
                }
            }
            return "";
        }

        public static string GetFrequencyCommand(this ScannerIdent scanner, int frequency)
        {
            if (scanner == ScannerIdent.Zf9020)
                return (frequency / 10 * 10).ToString();
            if (scanner == ScannerIdent.Zf9012)
            {
                switch (frequency)
                {
                    case 200:
                        return "l";
                    case 100:
                        return "n";
                    case 50:
                        return "h";
                }
            }
            return "";
        }

        public static int GetZfPointsNumValue(this ScannerIdent scanner, string resolution)
        {
            if (scanner == ScannerIdent.Zf9020 && int.TryParse(resolution, out _))
            {
                switch (resolution)
                {
                    case "20000": return 20480;
                    case "10000": return 10240;
                    case "8000": return 8192;
                    case "6000": return 6827;
                    case "5000": return 5120;
                    case "4000": return 4096;
                    case "3000": return 3413;
                    case "2000": return 2048;
                    case "1000": return 1024;
                }
            }
            if (scanner == ScannerIdent.Zf9020 || scanner == ScannerIdent.Zf9012 || scanner == ScannerIdent.Zf5000)
            {
                switch (resolution)
                {
                    case "sh": return 20480;
                    case "h": return 10240;
                    case "l": return 5120;
                }
            }
            return 0;
        }

        public static int GetZfFrequencyValue(this ScannerIdent scanner, string frequency)
        {
            if (scanner == ScannerIdent.Zf9020 && int.TryParse(frequency, out _))
            {
                switch (frequency)
                {
                    case "260": return 267;
                    case "210": return 214;
                    case "160": return 160;
                    case "130": return 134;
                    case "100": return 107;
                    case "80": return 80;
                    case "60": return 67;
                    case "50": return 53;
                }
            }
            if (scanner == ScannerIdent.Zf9020)
            {
                switch (frequency)
                {
                    case "l": return 214;
                    case "n": return 107;
                    case "h": return 53;
                }
            }
            if (scanner == ScannerIdent.Zf5000)
            {
                switch (frequency)
                {
                    case "l": return 55;
                    case "n": return 27;
                    case "h": return 14;
                }
            }
            if (scanner == ScannerIdent.Zf9012)
            {
                switch (frequency)
                {
                    case "l": return 200;
                    case "n": return 100;
                    case "h": return 50;
                }
            }
            return 0;
        }

    }
}
