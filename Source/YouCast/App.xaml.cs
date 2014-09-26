using Microsoft.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
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
            if (!IsRunAsAdministrator())
            {
                // It is not possible to launch a ClickOnce app as administrator directly, so instead we launch the
                // app as administrator in a new process.
                var processInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().CodeBase)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                // The following properties run the new process as administrator

                // Start the new process
                try
                {
                    Process.Start(processInfo);
                }
                catch (Exception)
                {
                    // The user did not allow the application to run as administrator
                    MessageBox.Show(
                        string.Format("Sorry, {0} must run as Administrator to be able to retrieve YouTube Podcasts.", GeneralInformation.ApplicationName),
                        string.Format("{0} can't start.", GeneralInformation.ApplicationName),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
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
        }

        private static void RunApplication()
        {
            var application = new App();
            application.InitializeComponent();
            application.Run();
        }

        bool ISingleInstanceApp.SignalExternalCommandLineArgs(IList<string> args)
        {
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
            return true;
        }

        private static bool IsRunAsAdministrator()
        {
            var windowsPrincipal = new WindowsPrincipal(WindowsIdentity.GetCurrent());

            return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
