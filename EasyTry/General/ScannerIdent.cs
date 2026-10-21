using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyTry.General
{
    //设备标识
    public enum ScannerIdent
    {
        [Description("LMS1XX")]
        LMS1XX,

        [Description("LMS4XX")]
        LMS4XX,

        [Description("LMS5XX")]
        LMS5XX,

        [Description("Zf9012")]
        Zf9012,

        [Description("Zf5000")]
        Zf5000,

        [Description("Zf9020")]
        Zf9020,

        [Description("LeicaP")]
        LeicaP,

        [Description("Unknow")]
        Unknow,
    }

    public enum ScannerFirm
    {
        [Description("Sick")]
        Sick,

        [Description("Z+F")]
        ZF,

        [Description("Leica")]
        Leica,

        [Description("Faro")]
        Faro,

        [Description("Unknow")]
        Unknow,
    }
}
