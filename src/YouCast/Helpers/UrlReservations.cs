using System.Collections.Generic;
using System.Linq;

namespace YouCast.Helpers
{
    public class UrlReservations : NetShResult
    {
        public UrlReservations(string result) : base(result)
        {
            UrlReservation rule = null;
            var rules = new List<UrlReservation>();
            var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
            foreach (var line in lines)
            {
                var data = line.Split(':').Select(d => d.Trim()).ToArray();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("-"))
                    continue;

                if (line.StartsWith("URL Reservations"))
                    continue;

                if (data.Length == 1)
                    continue;

                switch (data[0])
                {
                    case "Reserved URL":
                        if (rule != null)
                            rules.Add(rule);
                        var url = line.Replace("Reserved URL", string.Empty).Trim().TrimStart(':').Trim();
                        rule = new UrlReservation(url);
                        break;
                    default:
                        if (rule != null)
                            rule.Data[data[0]] = data[1];
                        break;
                }
            }

            if (rule != null)
                rules.Add(rule);

            Reservations = rules.ToArray();
        }

        public UrlReservation[] Reservations { get; }
    }
}
