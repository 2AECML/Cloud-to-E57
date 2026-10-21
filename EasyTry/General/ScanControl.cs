using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using LSSDKLib;
using IQOPENLib;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using System.Diagnostics;

namespace EasyTry.General
{
    class ScanControl : _IScanCtrlSDKEvents
    {
        private IScanCtrlSDK scanCtrl { get; set; }
        private IiQLibIf scanInformation { get; set; }
        private int cookie;
        private IConnectionPoint connectionPoint;
        private IiQLibIf iiQLibIf { get; set; }

        //更换扫描仪时需更换扫描仪的IP
        public string ScanIP;
        public int NumCols;         //总线数
        public int SplitLines;         //单个文件的线数
        public int ScanFileNum;               //扫描初始编号
        public string ScanFileName;            //扫描文件基本名
        public event EventHandler ScanCompleted;

        //更换扫描仪时需更换许可码
        private string licenseCode = "FARO Open Runtime License\n"
            + "Key:"
            + "664WL6NRTCKXXKPT6KZCSPLPJ"
            + "\n"
            + "\n"
            + "The software is the registered property of "
            + "FARO Scanner Production GmbH, Stuttgart, Germany.\n"
            + "All rights reserved.\n"
            + "This software may only be used with written permission "
            + "of FARO Scanner Production GmbH, Stuttgart, Germany.";

        /// <summary>
        /// 构造函数
        /// </summary>
        public ScanControl()
        {
            //定义对象，解锁
            IiQLicensedInterfaceIf licSDKIf = new ScanCtrlSDKClass();
            licSDKIf.License = licenseCode;
            scanCtrl = (IScanCtrlSDK)licSDKIf;
        }


        ///<summary>
        ///连接扫描仪
        ///<summary>
        //连接过程
        public bool ScanConnect()
        {
            scanCtrl.ScannerIP = ScanIP;
            //调用内置方法进行扫描仪连接
            scanCtrl.connect();
            Thread.Sleep(500);
            if (scanCtrl.Connected)
            {
                return true;
            }
            return false;
        }

        //扫描完成，未使用
        public void scanCompleted()
        {
            connectionPoint.Unadvise(cookie);
            OnScanCompleted(EventArgs.Empty);
        }

        //扫描完成，未使用
        protected void OnScanCompleted(EventArgs e)
        {
            var temp = ScanCompleted;
            if (temp != null)
            {
                temp(this, e);
            }
        }

        //开始扫描，执行螺旋式灰度扫描
        public void StartHelicalCanGreyScan()
        {
            Guid IID_IScanCtrlSDKEvents = typeof(_IScanCtrlSDKEvents).GUID;

            //使用 scanCtrl 对象查找 IID_IScanCtrlSDKEvents 接口的连接点，并将连接点存储到 connectionPoint 中。
            ((IConnectionPointContainer)scanCtrl).FindConnectionPoint(ref IID_IScanCtrlSDKEvents, out connectionPoint);

            //将当前对象（this）注册为事件接收者，并通过 cookie 保存订阅信息。
            connectionPoint.Advise(this, out cookie);

            // 可设置扫描参数
            scanCtrl.ScanMode = ScanMode.HelicalCanGrey;//StationaryGrey
            scanCtrl.SplitAfterLines = 500;// SplitLines;
            scanCtrl.NumCols = 100000;// NumCols;
            scanCtrl.ScanFileNumber = 0;
            scanCtrl.ScanBaseName = "HelicalGrey";
            scanCtrl.StorageMode = StorageMode.SMRemote;
            scanCtrl.RemoteScanStoragePath = @"G:\S";

            //固定扫描参数
            scanCtrl.Resolution = 1;
            scanCtrl.MeasurementRate = 8;
            scanCtrl.NoiseCompression = 1;
            scanCtrl.syncParam();
            Thread.Sleep(1000);
            scanCtrl.startScan();

        }  //连接后，准备开始

        //扫描进程，未使用
        public int ScanProgress
        {
            get
            {
                return scanCtrl.ScanProgress;
            }
        }

        //开始记录，记录状态，未使用
        public bool RecordingStatus()   
        {
            Thread.Sleep(1000);
            HelicalRecordingStatus status;
            scanCtrl.inquireRecordingStatus(out status);
            if (status.ToString() == "HRSRecording")
                return true;
            else
                return false;
        }

        //开始记录
        public int RecordScan() 
        {
            scanCtrl.recordScan();
            Thread.Sleep(1000);
            string path = @"D:\zfs\HelicalGrey016.fls";
            scanInformation.load(path);
            int n = scanInformation.getScanNumCols(scanInformation.loadScan(16));
            while (scanInformation.getScanNumCols(13) == 500)
            {
                scanCtrl.pauseScan();
            }
            return 0;
        }

        //错误提醒，未使用
        public void ShowExceptions()   
        {
            string exceptionMessages = string.Empty;
            for (int i = 0; i < scanCtrl.NumberExceptions; i++)
            {
                exceptionMessages += scanCtrl.getExceptionMsg(i) + "\n";
            }
            MessageBox.Show(exceptionMessages);

        }

        //状态
        /// <summary>
        /// Pauses scan data recording when scanning on helical CAN mode. Restart scan data recording with RecordScan.
        /// </summary>
        public void PauseScan()
        {
            switch (scanCtrl.pauseScan())
            {
                case 0:
                    return;
                case 3:
                    throw new InvalidOperationException("not connected");
                default:
                    throw new InvalidOperationException("unknown return value");
            }
        }   //结束记录

        public void StopScan()
        {
            scanCtrl.stopScan();
        }  //结束记录

        public void ShutDow()
        {
            int a = scanCtrl.shutDown();
        }//关机

        public void ReadData(string ip) //访问扫描数据
        {

            scanCtrl.RemoteScanAccess = RemoteScanAccessStatus.RSAEnabled;

            Process p = new Process();
            p.StartInfo.FileName = "explorer.exe";
            p.StartInfo.Arguments = @"\\" + ip;
            p.Start();
        }
        public int GetNumAutomationMessages()
        {
            iiQLibIf.save();
            iiQLibIf.getNumScans();
            int s = iiQLibIf.getNumAutomationMessages(0);
            return s;
        }
    }
}
