using EasyTry.Conversion.UI;

namespace EasyTry
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            var mainForm = new Form1();
            PointCloudConversionEntry.Attach(mainForm);
            Application.Run(mainForm);
        }
    }
}
