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
    class AutoMessages
    {
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
        private IiQLibIf iiQLibIf { get; set; }

        ulong atime;
        //构造函数
        public AutoMessages()
        {
            iQLibIf AutoMess = new iQLibIfClass();
            AutoMess.License = licenseCode;
            iiQLibIf = (IiQLibIf)AutoMess;

        }
        public int GetNumAutomationMessages()
        {
            iiQLibIf.saveAs("test");
            int s1 = iiQLibIf.getNumScans();
            int s = iiQLibIf.getNumAutomationMessages(0);
            int s2 = iiQLibIf.getNumAutomationTriggers(0);
            iiQLibIf.getAutomationTriggerTime(0, 1, out atime);
            return s;
        }
    }
}
