using System.Collections.Generic;

namespace YouCast.Helpers
{
    public class FirewallRule
    {
        public string RuleName { get; }
        public Dictionary<string, string> Data { get; }
        public int LocalPort { get; set; }

        public FirewallRule(string ruleName)
        {
            RuleName = ruleName;
            Data = new Dictionary<string, string>();
        }
    }
}