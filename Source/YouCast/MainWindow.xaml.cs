using SyndicationService;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YouCast.Properties;

namespace YouCast
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly string _baseAddress = "http://{0}:{1}/FeedService";
        private readonly System.Windows.Forms.NotifyIcon _myNotifyIcon;
        private const string DefaultPort = "22703";
        private readonly string _localIp;

        public MainWindow()
        {
            InitializeComponent();
            
            _myNotifyIcon = new System.Windows.Forms.NotifyIcon {Icon = new System.Drawing.Icon("rss.ico")};
            _myNotifyIcon.MouseDoubleClick += (a, b) => WindowState = WindowState.Normal;
            var items = new List<System.Windows.Forms.MenuItem>();

            var item1 = new System.Windows.Forms.MenuItem("Exit", (a,b) =>
                {
                    _myNotifyIcon.Visible = false;
                    Close(); 
                });
            var item2 = new System.Windows.Forms.MenuItem("-");
            var item3 = new System.Windows.Forms.MenuItem("Open", (a, b) => WindowState = WindowState.Normal);

            items.Add(item3);
            items.Add(item2);
            items.Add(item1);

            _myNotifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(items.ToArray());

            PopulateQualities();

            _localIp = Dns.GetHostEntry(Dns.GetHostName()).
                AddressList.First(ip => ip.AddressFamily == AddressFamily.InterNetwork).ToString();

            var address = Settings.Default.OverrideNetworkSettings
                ? Settings.Default.HostName
                : _localIp;
            var port = Settings.Default.OverrideNetworkSettings
                ? Settings.Default.PortNumber
                : DefaultPort;
            IpAddressLabel.Text = address;
            PortLabel.Text = port;
            _baseAddress = string.Format(_baseAddress, address, port);

            Generate.Content = "Generate & Copy URL";
        }

        private void PopulateQualities()
        {
            foreach (var value in Enum.GetValues(typeof(YouTubeEncoding)))
            {
                Quality.Items.Add(value.ToString().Replace("_", "@"));
            }
            Quality.SelectedIndex = 0;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Copy.IsEnabled = true;

            string userId = Input.Text.Trim();
            var encoding =
                (YouTubeEncoding)
                    Enum.Parse(typeof (YouTubeEncoding), (Quality.SelectedItem as string).Replace("@", "_"));
            int maxLength;
            bool isPopular = CheckBox.IsChecked.HasValue && CheckBox.IsChecked.Value;

            int.TryParse(MaxLength.Text, out maxLength);
            if (maxLength < 0)
            {
                maxLength = 0;
            }

            string url = GenerateURL(userId, encoding, maxLength, isPopular);
            
            Output.Text = url;
            Clipboard.SetText(url);
        }

        private string GenerateURL(string userId, YouTubeEncoding encoding, int maxLength, bool isPopular)
        {
            var selectedItem = ComboBox.SelectedItem as ListBoxItem;
            if (Equals(selectedItem, UserNameItem))
            {
                return _baseAddress + string.Format(
                   "/GetUserFeed?userId={0}&encoding={1}&maxLength={2}&isPopular={3}",
                    userId,
                    encoding,
                    maxLength,
                    isPopular);
            }

            if (Equals(selectedItem, PlaylistItem))
            {
                return _baseAddress + string.Format(
                   "/GetPlaylistFeed?playlistId={0}&encoding={1}&maxLength={2}&isPopular={3}",
                    userId,
                    encoding
                    ,maxLength,
                    isPopular);
            }

            return null;
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Output.Text);
        }

        private void Window_Loaded_1(object sender, RoutedEventArgs e)
        {
            AddFirewallRule();
            OpenService();
        }

        private static void AddFirewallRule()
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = string.Format("advfirewall firewall show rule name=\"{0}\"", GeneralInformation.ApplicationName),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                
            };
            
            var port = Settings.Default.OverrideNetworkSettings
                ? Settings.Default.PortNumber
                : DefaultPort;

            if (Process.Start(processStartInfo).StandardOutput.ReadToEnd().Contains("No rules match"))
            {
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = string.Format("advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1}", GeneralInformation.ApplicationName, port),
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
            }
            else
            {
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = string.Format("advfirewall firewall set rule name=\"{0}\" new localport={1}", GeneralInformation.ApplicationName, port),
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
            }

            Process.Start(processStartInfo).WaitForExit();
        }

        private void OpenService()
        {
            var baseAddress = new Uri(_baseAddress);
            var svcHost = new WebServiceHost(typeof(Feed), baseAddress);

            try
            {
                svcHost.Open();

                if (svcHost.State != System.ServiceModel.CommunicationState.Opened &&
                    svcHost.State != System.ServiceModel.CommunicationState.Opening)
                {
                    MessageBox.Show("Failed to register the WCF service. Try running as administrator");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
                svcHost.Close();
            }
        }

        private void Window_StateChanged_1(object sender, EventArgs e)
        {
            switch (WindowState)
            {
                case WindowState.Minimized:
                    _myNotifyIcon.Visible = true;
                    ShowInTaskbar = false;
                    break;
                case WindowState.Normal:
                    _myNotifyIcon.Visible = false;
                    ShowInTaskbar = true;
                    break;
            }
        }

        private bool _gotFocus;

        private void _input_GotFocus_1(object sender, RoutedEventArgs e)
        {
            if (_gotFocus) return;
            Generate.IsEnabled = true;
            Input.Text = string.Empty;
            _gotFocus = true;
        }

        private void _input_TextChanged_1(object sender, TextChangedEventArgs e)
        {
            if (Generate == null) return;
            Generate.IsEnabled = !string.IsNullOrWhiteSpace(Input.Text);
        }

        private void ComboBox_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            if (e.AddedItems.Contains(UserNameItem))
            {
                Input.Text = "for example: I3arnon";
            }

            if (e.AddedItems.Contains(PlaylistItem))
            {
                Input.Text = "for example: PL950C8AEC6CC3E6FE";
            }
            
            _gotFocus = false;
        }
        private bool _maxLengthFocus;

        private void _maxLength_GotFocus_1(object sender, RoutedEventArgs e)
        {
            if (_maxLengthFocus) return;
            MaxLength.Text = string.Empty;
            _maxLengthFocus = true;
        }

        private void _maxLength_PreviewTextInput_1(object sender, TextCompositionEventArgs e)
        {
            int result;
            
            if (!int.TryParse(e.Text,out result))
            {
                e.Handled = true;
            }
        }

        private void Change_Click(object sender, RoutedEventArgs e)
        {
            var host = IpAddressLabel.Text;
            var port = PortLabel.Text;

            int portNumber;
            if (!int.TryParse(port, out portNumber) || portNumber < 1025 || portNumber > 65535)
            {
                MessageBox.Show("Port must be a number between 1025 and 65535.", "Invalid Port Number",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PortLabel.Text = DefaultPort;
                return;
            }

            if (!Settings.Default.OverrideNetworkSettings && port == DefaultPort && host == _localIp)
            {
                return;
            }

            if (port != Settings.Default.PortNumber)
            {
                MessageBox.Show(
                    string.Format("The new port will take affect the next time you open {0}.", GeneralInformation.ApplicationName),
                    string.Format("Reopen {0}", GeneralInformation.ApplicationName),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            UpdateNetworkSettings(host, port);
        }

        private static void UpdateNetworkSettings(string host, string port)
        {
            Settings.Default.HostName = host;
            Settings.Default.PortNumber = port;
            Settings.Default.OverrideNetworkSettings = true;
            Settings.Default.Save();
        }
    }
}
