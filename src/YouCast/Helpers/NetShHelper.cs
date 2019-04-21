using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace YouCast.Helpers
{
    public class NetShHelper
    {
        private const string FirewallShowRuleCommand = "advfirewall firewall show rule name=\"{0}\"";
        private const string FirewallSetRuleCommand = "advfirewall firewall set rule name=\"{0}\" new localport={1}";
        private const string FirewallAddRuleCommand = "advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1}";
        private const string FirewallDeleteRuleCommand = "advfirewall firewall delete rule name=\"{0}\"";

        private const string UrlAclShowCommand = "http show urlacl url={0}";
        private const string UrlAclAddCommand = "http add urlacl url={0} user={1} listen=yes";
        private const string UrlAclDeleteRuleCommand = "http delete urlacl url={0}";

        private const string NoOutputRedirected = "NO OUTPUT REDIRECTED";

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private string NetShRun(string command)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            if (!PermissionsHelper.IsRunAsAdministrator() && (command.Contains(" set ") || command.Contains(" add ") || command.Contains(" delete ")))
            {
                processStartInfo.Verb = "runas";
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.UseShellExecute = true;
            }

            string processResult;

            // Start the new process
            try
            {
                var process = Process.Start(
                    processStartInfo);

                process.WaitForExit();
                if (processStartInfo.RedirectStandardOutput)
                {
                    processResult = process.
                        StandardOutput.
                        ReadToEnd();
                }
                else
                {
                    processResult = NoOutputRedirected;
                }
            }
            catch (Exception)
            {
                return "requires elevation";
            }

            return processResult;
        }

        public FirewallRules GetFirewallRule(string applicationName)
        {
            var result = NetShRun(string.Format(FirewallShowRuleCommand, applicationName));

            if (result.Contains("requires elevation"))
                return null;

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("ok"))
                return new FirewallRules(result);

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("no rules match the specified criteria"))
                return new FirewallRules(result);

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public bool CreateFirewallRule(string name, int port)
        {
            var result = NetShRun(string.Format(FirewallAddRuleCommand, name, port));

            if (result.Contains("requires elevation"))
                return false;

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("ok"))
                return true;

            if (result.Trim() == NoOutputRedirected)
                return true;

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public bool DeleteFirewallRule(string name)
        {
            var result = NetShRun(string.Format(FirewallDeleteRuleCommand, name));

            if (result.Contains("requires elevation"))
                return false;

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("ok"))
                return true;

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("no rules match the specified criteria"))
                return true;

            if (result.Trim() == NoOutputRedirected)
                return true;

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public bool UpdateFirewallRule(string name, int port)
        {
            var result = NetShRun(string.Format(FirewallSetRuleCommand, name, port));

            if (result.Contains("requires elevation"))
                return false;

            if (result.Trim().Trim('.').ToLowerInvariant().EndsWith("ok"))
                return true;

            if (result.Trim() == NoOutputRedirected)
                return true;

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public UrlReservations GetUrlAcl(string url)
        {
            var result = NetShRun(string.Format(UrlAclShowCommand, url));

            if (result.Contains("requires elevation"))
                return null;

            if (result.Contains("URL Reservations:"))
                return new UrlReservations(result);

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public bool CreateUrlAcl(string url)
        {
            var result = NetShRun(string.Format(UrlAclAddCommand, url, $"{Environment.UserDomainName}\\{Environment.UserName}"));

            if (result.Contains("requires elevation"))
                return false;

            if (result.Contains("reservation add failed"))
                return false;

            if (result.Trim().Trim('.').EndsWith("URL reservation successfully added"))
                return true;

            if (result.Trim() == NoOutputRedirected)
                return true;

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }

        public bool DeleteUrlAcl(string url)
        {
            var result = NetShRun(string.Format(UrlAclDeleteRuleCommand, url));

            if (result.Contains("requires elevation"))
                return false;

            if (result.Trim() == "URL reservation successfully deleted")
                return true;

            if (result.Contains("URL reservation delete failed"))
                return false;

            if (result.Trim() == NoOutputRedirected)
                return true;

            throw new InvalidOperationException($"Unknown result from netsh: {result}");
        }
    }
}