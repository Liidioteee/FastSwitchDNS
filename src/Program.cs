using System;
using System.Threading;
using System.Windows.Forms;

namespace FastSwitchDNS
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, @"Local\FastSwitchDNS", out bool isFirst);
            if (!isFirst) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }
}
