using System.Collections.Generic;

namespace YouCast.Helpers
{
    public class UrlReservation
    {
        public Dictionary<string, string> Data { get; }
        public string Url { get; }

        public UrlReservation(string url)
        {
            Url = url;
            Data = new Dictionary<string, string>();
        }
    }
}