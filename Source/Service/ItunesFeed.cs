using System;
using System.ServiceModel.Syndication;
using System.Xml;

namespace Service
{
    public class ItunesFeed : SyndicationFeed
    {
        private const string Namespace = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        private const string Prefix = "itunes";

        public string Subtitle { get; set; }
        public string Author { get; set; }
        public string Summary { get; set; }
        public string OwnerName { get; set; }
        public string OwnerEmail { get; set; }
        public bool Explicit { get; set; }

        public ItunesFeed(string title, string description, Uri feedAlternateLink)
            : base(title, description, feedAlternateLink)
        {
        }

        protected override void WriteAttributeExtensions(XmlWriter writer, string version)
        {
            writer.WriteAttributeString("xmlns", Prefix, null, Namespace);
        }

        protected override void WriteElementExtensions(XmlWriter writer, string version)
        {
            WriteItunesElement(writer, "subtitle", Subtitle);
            WriteItunesElement(writer, "author", Author);
            WriteItunesElement(writer, "summary", Summary);
            if (ImageUrl != null)
            {
                WriteItunesElement(writer, "image", ImageUrl.ToString());
            }
            WriteItunesElement(writer, "explicit", Explicit ? "yes" : "no");

            writer.WriteStartElement(Prefix, "owner", Namespace);
            WriteItunesElement(writer, "name", OwnerName);
            WriteItunesElement(writer, "email", OwnerEmail);
            writer.WriteEndElement();
        }

        private static void WriteItunesElement(XmlWriter writer, string name, string value)
        {
            if (value == null) return;

            writer.WriteStartElement(Prefix, name, Namespace);
            writer.WriteValue(value);
            writer.WriteEndElement();
        }
    }
}
