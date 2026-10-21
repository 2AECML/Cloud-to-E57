using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyTry.General
{
    public interface IControllerBase
    {
        event EventHandler Disconnected;

        /// <summary>
        /// 类型
        /// </summary>
        ControllerType Type { get; }

        /// <summary>
        /// 能否暂停
        /// </summary>
        bool CanPause { get; }

        /// <summary>
        /// 连接
        /// </summary>
        void Connect();

        /// <summary>
        /// 开始
        /// </summary>
        void Start();

        /// <summary>
        /// 暂停
        /// </summary>
        void Pause();

        /// <summary>
        /// 停止
        /// </summary>
        void Stop();
    }
}
