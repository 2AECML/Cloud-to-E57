using EasyTry.Conversion.UI;
using EasyTry.Conversion.Services;

namespace EasyTry
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == ConversionWorkerProtocol.Argument)
            {
                return ConversionWorkerHost.Run();
            }
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            var mainForm = new Form1();
            PointCloudConversionEntry.Attach(mainForm);
            Application.Run(mainForm);
            return 0;
        }
    }
}
