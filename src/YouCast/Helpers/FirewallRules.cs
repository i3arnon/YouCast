using System.Collections.Generic;
using System.Linq;

namespace YouCast.Helpers
{
    public class FirewallRules : NetShResult
    {
        public FirewallRules(string result) : base(result)
        {
            FirewallRule rule = null;
            var rules = new List<FirewallRule>();
            var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
            foreach (var line in lines)
            {
                var data = line.Split(':').Select(d => d.Trim()).ToArray();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("-"))
                    continue;

                if (data.Length == 1)
                    continue;

                switch (data[0])
                {
                    case "Rule Name":
                        if (rule != null)
                        {
                            rules.Add(rule);
                        }
                        rule = new FirewallRule(data[1]);
                        break;
                    case "Ok.":
                        rules.Add(rule);
                        rule = null;
                        break;
                    case "LocalPort":
                        if (int.TryParse(data[1], out var port))
                            if (rule != null)
                                rule.LocalPort = port;
                        break;
                    default:
                        if (rule != null)
                            rule.Data[data[0]] = data[1];
                        break;
                }
            }

            if (rule != null)
                rules.Add(rule);

            Rules = rules.ToArray();
        }

        public FirewallRule[] Rules { get; }
    }
}