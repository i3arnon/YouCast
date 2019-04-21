using System;
using System.Diagnostics.CodeAnalysis;
using System.ServiceModel.Syndication;
using System.Xml;

namespace Service
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class ItunesFeed : SyndicationFeed
    {
        private const string _namespace = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        private const string _prefix = "itunes";

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

        protected override void WriteAttributeExtensions(XmlWriter writer, string version) =>
            writer.WriteAttributeString("xmlns", _prefix, null, _namespace);

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

            writer.WriteStartElement(_prefix, "owner", _namespace);
            WriteItunesElement(writer, "name", OwnerName);
            WriteItunesElement(writer, "email", OwnerEmail);
            writer.WriteEndElement();
        }

        private static void WriteItunesElement(XmlWriter writer, string name, string value)
        {
            if (value == null)
            {
                return;
            }

            writer.WriteStartElement(_prefix, name, _namespace);
            writer.WriteValue(value);
            writer.WriteEndElement();
        }
    }
}
