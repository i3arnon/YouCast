using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using YouCast.Properties;

namespace YouCast
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ISingleInstanceApp
    {
        [STAThread]
        public static void Main()
        {
            if (!Settings.Default.UseSingleInstance)
            {
                RunApplication();
            }
            else if (SingleInstance<App>.InitializeAsFirstInstance(Process.GetCurrentProcess().ProcessName))
            {
                RunApplication();

                // Allow single instance code to perform cleanup operations
                SingleInstance<App>.Cleanup();
            }
        }

        private static void RunApplication()
        {
            var application = new App();
            application.InitializeComponent();
            application.Run();
        }

        bool ISingleInstanceApp.SignalExternalCommandLineArgs(IList<string> args)
        {
            if (MainWindow != null)
            {
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }

            return true;
        }
    }
}
