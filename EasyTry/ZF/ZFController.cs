using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyTry.General;

namespace EasyTry
{
    //IControllerBase是一个接口类，Controller相当于是一个基类
    public class ZFController: IControllerBase
    {
        public Controller ZfController { get; set; }

        //定义了一个没有连接的数据处理器
        public event EventHandler Disconnected;

        //=>表示属性值由右边的表达式计算并返回
        public bool IsConnected => ZfController.IsConnected;

        //连接设备类型
        public ControllerType Type { get; }

        //是否可以支持暂停操作
        public bool CanPause => true;

        //初始化函数，将传入的参数值，赋给Controller类实例对应的属性值
        public ZFController(string ipAddress, int controlPort, int dataPort, string resolution, string frequency, string projectName, string fileName, int fileSize, bool gpsSyncSignal, bool receiveRealtime, ScannerIdent scanner = ScannerIdent.Zf9012)
        {
            ZfController = new Controller
            {
                ScannerIP = ipAddress,
                ControlPort = controlPort,
                DataPort = dataPort,
                ProjectName = projectName,
                FileName = fileName,
                ResolutionParam = resolution,
                FrequencyParam = frequency,
                FileSize = fileSize,
                GpsSyncSignal = gpsSyncSignal,
                //ReceieveRtData = receiveRealtime
            };

            //指定连接的设备类型是Z+F
            Type = ControllerType.ZF;
        }

        //表达体方法，这个方法只有箭头后面的一句代码
        public void Connect() => ZfController.Connect();

        public void Start()
        {
            if (ZfController.GpsSyncSignal)
                Thread.Sleep(30 * 1000);//Z+F扫描仪若采用IMU+GPS同步工作模式时，IMU初始化good之后等待30s之后再开始采集
            ZfController.StartScan();
        }

        //表达体方法，这个方法只有箭头后面的一句代码
        public void Pause() => ZfController.PauseScan();

        public void Stop()
        {
            ZfController.StopScan();
            ZfController.Disconnect();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
