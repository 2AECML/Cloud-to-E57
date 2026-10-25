using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZfsSdkRef;

namespace EasyTry.General
{
    public class Controller
    {
        public string ScannerIP { get; set; }
        public int ControlPort { get; set; }   //6100
        public int DataPort { get; set; }      //6101

        public bool Calibrated { get; internal set; } = true;//是否进行校准
        private string LastProjectName { get; set; } = "";
        public string ProjectName { get; set; }
        public string FileName { get; set; }

        public int FileSize { get; set; }
        public bool GpsSyncSignal { get; set; }
        public string ResolutionParam { get; set; }
        public string FrequencyParam { get; set; }
        public ScannerIdent ScannerIdent { get; private set; }

        //此处判断调用了DLL中封装类ZfControllerRef，判断是否连接，逐级向上传导，用于判断
        private ZfControllerRef ControlHandler { get; set; }
        public bool IsConnected => ControlHandler.IsConnected();

        //构造函数
        public Controller()
        {
            //ZfsRef.SetLoaclDllDirectory("Libs");
            ControlHandler = new ZfControllerRef();  //ZfControllerRef是封装到DLL中的类
        }

        /// <summary>
        ///判断连接（用于向上传递连接状态）
        /// </summary>
        /// <returns>是否连接</returns>
        public bool Connect()
        {
            //ControlHandler = new ZfControllerRef();
            //进行连接操作，并返回连接结果
            bool isConnect = ControlHandler.Connect(ScannerIP, ControlPort);

            //如果没有连接成功返回false
            if (!isConnect)
                return false;

            //连接成功执行以下操作
            ControlHandler.Detect();
            GetIdent();
            return true;
        }

        /// <summary>
        /// 获取扫描类型函数
        /// </summary>
        private void GetIdent()
        {
            //没有连接就返回
            if (!IsConnected) return;

            //连接后执行以下造作
            ZfIdent ident = ControlHandler.GetType();
            switch (ident)
            {
                case ZfIdent.I5003:
                case ZfIdent.I5006:
                case ZfIdent.I5010:
                case ZfIdent.I5016:
                    ScannerIdent = ScannerIdent.Zf5000;
                    break;
                case ZfIdent.P9000:
                    string info = ControlHandler.Cmd("info");
                    if (info.Contains("9012"))
                        ScannerIdent = ScannerIdent.Zf9012;
                    else if (info.Contains("9020"))
                        ScannerIdent = ScannerIdent.Zf9020;
                    else
                        ScannerIdent = ScannerIdent.Unknow;
                    break;
                default:
                    ScannerIdent = ScannerIdent.Unknow;
                    break;
            }
        }

        #region 扫描指令

        /// <summary>
        /// 发送扫描CMD指令
        /// </summary>
        private string SendCmd(string cmd)
        {
            //如果没有连接返回null
            if (!IsConnected)
                return null;

            //连接后返回实例方法返回值
            return ControlHandler.Cmd(cmd);
        }

        /// <summary>
        /// 开始扫描
        /// </summary>
        public void StartScan()
        {
            if (LastProjectName == ProjectName)
                ProjectName += "0";

            //声明开始扫描指令
            string startCmdStr;
            string rtLine = "";

            //拼接开始扫描指令
            if (GpsSyncSignal == false)
            {
                startCmdStr = "scan" + " -profiler" + $" -resolution {ResolutionParam}" + $" -quality {FrequencyParam}" +
                    " -endless" + " -counterbyte" + $" -path {ProjectName}" + $" -file {FileName}" +
                    $" -filesize {FileSize}mb" + " -cnt1reset" + " -cntlreset" + " -statusstream" +
                    " -cnt1" + " -cnt2" + " -cnt2reset" + rtLine;
            }
            else
            {
                startCmdStr = "scan" + " -profiler" + $" -resolution {ResolutionParam}" + $" -quality {FrequencyParam}" +
                   " -endless" + " -counterbyte" + $" -path {ProjectName}" + $" -file {FileName}" +
                   " -gps" + " -pps" + " -lineheadersize 72" +
                   $" -filesize {FileSize}mb" + " -cnt1reset" + " -cntlreset" + " -statusstream" +
                   " -cnt1" + " -cnt2" + " -cnt2reset" + rtLine;
            }

            //获取扫描仪频率和点数，来修正一些特殊情况
            int frequency = ScannerIdent.GetZfFrequencyValue(FrequencyParam);
            int pointsNum = ScannerIdent.GetZfPointsNumValue(ResolutionParam);

            if (frequency == 0)
                frequency = 50;
            if (pointsNum == 0)
                pointsNum = 5120;

            //可能无用
            ScanningParameters parameters = new ScanningParameters(frequency, 360.0 / pointsNum, num: pointsNum);
            LastProjectName = ProjectName;

            SendCmd(startCmdStr);

            return;

        }

        /// <summary>
        /// 暂停当前模式的扫描进程
        /// </summary>
        public void PauseScan() => SendCmd("pause");  //调用封装好的方法

        /// <summary>
        /// 停止扫描
        /// </summary>
        public void StopScan()
        {
            if (ControlHandler.IsConnected() || ControlHandler.Connect(ScannerIP, ControlPort))

                ControlHandler.Stop(); //断开网线后，即便重连，IsConnected亦为false

            else
                return;

            //暂停执行 200 毫秒（即 0.2 秒）
            Thread.Sleep(200);
        }

        /// <summary>
        /// 断开扫描仪连接
        /// </summary>
        public void Disconnect()
        {
            ControlHandler?.Disconnect();
            ControlHandler?.Dispose();
        }

        /// <summary>
        /// 忽略扫描仪发生的一般错误
        /// </summary>
        public void IgnoreError() => ControlHandler.Ignore();

        /// <summary>
        /// 放弃当前扫描指令
        /// </summary>
        public void AbortScan() => ControlHandler.Abort();

        /// <summary>
        /// 重启上一次扫描模式
        /// </summary>
        public void RestartScan() => ControlHandler.Retry();
        #endregion
    }
}
