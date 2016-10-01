using Service;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YouCast.Properties;
using MenuItem = System.Windows.Forms.MenuItem;

namespace YouCast
{
    public partial class MainWindow
    {
        private const string _cloudHostName = "youcast.cloudapp.net";
        private const int _cloudPort = 80;
        private const int _defaultPort = 22703;

        private readonly System.Windows.Forms.NotifyIcon _myNotifyIcon;
        private readonly string _localIp;

        private string _baseAddress;
        private bool _gotFocus;
        private bool _maxLengthFocus;
        private WebServiceHost _serviceHost;

        public MainWindow()
        {
            InitializeComponent();

            _myNotifyIcon = new System.Windows.Forms.NotifyIcon { Icon = new System.Drawing.Icon("rss.ico") };
            _myNotifyIcon.MouseDoubleClick += (a, b) => WindowState = WindowState.Normal;
            _myNotifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(
                new[]
                {
                    new MenuItem("Open", (a, b) => WindowState = WindowState.Normal),
                    new MenuItem("-"),
                    new MenuItem(
                        "Exit",
                        (a, b) =>
                        {
                            _myNotifyIcon.Visible = false;
                            Close();
                        })
                });

            _localIp = Dns.GetHostEntry(Dns.GetHostName()).
                AddressList.First(ip => ip.AddressFamily == AddressFamily.InterNetwork).ToString();

            PopulateQualities();
            LoadNetworkSettings();
        }

        private void PopulateQualities()
        {
            foreach (var value in Enum.GetValues(typeof(YouTubeEncoding)))
            {
                Quality.Items.Add(value.ToString().Replace("_", "@"));
            }

            Quality.SelectedIndex = 0;
        }

        private void LoadNetworkSettings()
        {
            IpAddressLabel.IsEnabled = true;
            PortLabel.IsEnabled = true;

            string hostName;
            int port;
            if (Settings.Default.UseCloudService)
            {
                hostName = _cloudHostName;
                port = _cloudPort;
                IpAddressLabel.IsEnabled = false;
                PortLabel.IsEnabled = false;
                UseCloud.IsChecked = true;
            }
            else if (Settings.Default.OverrideNetworkSettings)
            {
                hostName = Settings.Default.HostName;
                port = int.Parse(Settings.Default.PortNumber);
            }
            else
            {
                hostName = _localIp;
                port = _defaultPort;
            }
            IpAddressLabel.Text = hostName;
            PortLabel.Text = port.ToString();
            _baseAddress = new UriBuilder("HTTP", hostName, port == 80 ? -1 : port, "FeedService").ToString();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            Copy.IsEnabled = true;

            var encoding = (YouTubeEncoding)Enum.Parse(
                typeof(YouTubeEncoding),
                ((string)Quality.SelectedItem).Replace("@", "_"));

            int maxLength;
            int.TryParse(MaxLength.Text, out maxLength);
            if (maxLength < 0)
            {
                maxLength = 0;
            }

            var url = GenerateUrl(
                Input.Text.Trim(),
                encoding,
                maxLength,
                CheckBox.IsChecked.HasValue && CheckBox.IsChecked.Value);

            Output.Text = url;
            Clipboard.SetDataObject(url);
        }

        private string GenerateUrl(string userId, YouTubeEncoding encoding, int maxLength, bool isPopular)
        {
            userId = WebUtility.UrlEncode(userId);
            var selectedItem = ComboBox.SelectedItem as ListBoxItem;
            if (Equals(selectedItem, UserNameItem))
            {
                return $"{_baseAddress}/GetUserFeed?userId={userId}&encoding={encoding}&maxLength={maxLength}&isPopular={isPopular}";
            }

            if (Equals(selectedItem, PlaylistItem))
            {
                return $"{_baseAddress}/GetPlaylistFeed?playlistId={userId}&encoding={encoding}&maxLength={maxLength}&isPopular={isPopular}";
            }

            return null;
        }

        private void Copy_Click(object sender, RoutedEventArgs e) =>
            Clipboard.SetDataObject(Output.Text);

        private void Window_Loaded_1(object sender, RoutedEventArgs e)
        {
            WindowState = Settings.Default.StartupWindowState;
            if (Settings.Default.StartupWindowState == WindowState.Minimized)
            {
                Window_StateChanged_1(null, EventArgs.Empty);
                StartMinimized.IsChecked = true;
            }

            UpdateLocalService();
        }

        private void UpdateLocalService()
        {
            if (Settings.Default.UseCloudService) return;

            CloseServiceHost();
            SetFirewallRule();
            OpenServiceHost();
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private static void SetFirewallRule()
        {
            var isExists = !Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments =
                            $"advfirewall firewall show rule name=\"{GeneralInformation.ApplicationName}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                    }).
                StandardOutput.
                ReadToEnd().
                Contains("No rules match");

            var port = Settings.Default.OverrideNetworkSettings
                ? int.Parse(Settings.Default.PortNumber)
                : _defaultPort;

            Process.Start(
                    isExists
                        ? new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"advfirewall firewall set rule name=\"{GeneralInformation.ApplicationName}\" new localport={port}",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        }
                        : new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments =
                                $"advfirewall firewall add rule name=\"{GeneralInformation.ApplicationName}\" dir=in action=allow protocol=TCP localport={port}",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        }).
                WaitForExit();
        }

        private void OpenServiceHost()
        {
            _serviceHost = new WebServiceHost(typeof(YoutubeFeed));
            _serviceHost.AddServiceEndpoint(typeof(IYoutubeFeed), new WebHttpBinding(), new Uri(_baseAddress));

            try
            {
                _serviceHost.Open();

                if (_serviceHost.State != CommunicationState.Opened &&
                    _serviceHost.State != CommunicationState.Opening)
                {
                    MessageBox.Show("Failed to register the WCF service. Try running as administrator");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
                _serviceHost.Close();
            }
        }

        private void CloseServiceHost()
        {
            if (_serviceHost == null) return;

            try
            {
                _serviceHost.Close();
            }
            catch (Exception)
            {
                _serviceHost.Abort();
            }

            _serviceHost = null;
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
                Input.Text = "for example: i3arnon";
            }
            else if (e.AddedItems.Contains(PlaylistItem))
            {
                Input.Text = "for example: PL950C8AEC6CC3E6FE";
            }

            _gotFocus = false;
        }

        private void _maxLength_GotFocus_1(object sender, RoutedEventArgs e)
        {
            if (_maxLengthFocus) return;
            MaxLength.Text = string.Empty;
            _maxLengthFocus = true;
        }

        private void _maxLength_PreviewTextInput_1(object sender, TextCompositionEventArgs e)
        {
            int result;

            if (!int.TryParse(e.Text, out result))
            {
                e.Handled = true;
            }
        }

        private void UseCloud_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            Settings.Default.UseCloudService = UseCloud.IsChecked.GetValueOrDefault();
            Settings.Default.Save();
            LoadNetworkSettings();
            UpdateLocalService();
        }

        private void Change_Click(object sender, RoutedEventArgs e)
        {
            var host = IpAddressLabel.Text;
            var port = PortLabel.Text;

            int portNumber;
            if (!int.TryParse(port, out portNumber) || portNumber < 1 || portNumber > 65535)
            {
                MessageBox.Show(
                    "Port must be a number between 1 and 65535.",
                    "Invalid Port Number",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                PortLabel.Text = _defaultPort.ToString();
                return;
            }

            if (!Settings.Default.OverrideNetworkSettings && portNumber == _defaultPort && host == _localIp)
            {
                return;
            }

            SetNetworkSettings(host, port);
            LoadNetworkSettings();
            UpdateLocalService();
        }

        private static void SetNetworkSettings(string host, string port)
        {
            Settings.Default.HostName = host;
            Settings.Default.PortNumber = port;
            Settings.Default.OverrideNetworkSettings = true;
            Settings.Default.Save();
        }

        private void Save_OnClick(object sender, RoutedEventArgs e)
        {
            if (!StartMinimized.IsChecked.HasValue)
            {
                return;
            }

            Settings.Default.StartupWindowState = StartMinimized.IsChecked.Value
                ? WindowState.Minimized
                : WindowState.Normal;
            Settings.Default.Save();
        }

        private void YoucastLink_OnClick(object sender, RoutedEventArgs e) =>
            Process.Start("http://youcast.i3arnon.com/");

        private void TwitterLink_OnClick(object sender, RoutedEventArgs e) =>
            Process.Start("https://twitter.com/i3arnon");

        private void GplLink_OnClick(object sender, RoutedEventArgs e) =>
            Process.Start("https://github.com/i3arnon/YouCast/blob/master/LICENSE");
    }
}