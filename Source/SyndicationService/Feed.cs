using Google.GData.Client;
using Google.GData.Extensions;
using Google.GData.YouTube;
using Google.YouTube;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YoutubeExtractor;

namespace SyndicationService
{
    [ServiceBehavior(
        ConcurrencyMode = ConcurrencyMode.Multiple,
        UseSynchronizationContext = false)]
    public class Feed : IFeed
    {
        #region Data Members

        private const string UserUrl = "http://gdata.youtube.com/feeds/api/users/{0}";
        private const string PlaylistUrl = "https://gdata.youtube.com/feeds/api/playlists/{0}?v=2";
        private const string VideoUrl = "http://www.youtube.com/watch?v={0}";

        private const string DeveloperKey =
            "AI39si6kaGnRDF4m-BzWNLIfrVP5O0MNS2Up5dfEpy0PnOZ9vhsI6Ro1wLOWhIPohT0CdZa_WiWBRzZCMJ8INxXT_0pyRPOmBA";

        private readonly YouTubeService _service = new YouTubeService(GeneralInformation.ApplicationName, DeveloperKey);



        private string _baseAddress;

        #endregion

        #region IFeed

        public SyndicationFeedFormatter GetUserFeed(string userId, string encoding, int maxLength, bool isPopular)
        {
            SetBaseAddress();

            var youtubeEncoding = ParseEncoding(encoding);
            var profile = _service.Get(string.Format(UserUrl, userId)) as ProfileEntry;
            var lastUpdated = profile.Updated;

            SyndicationFeed syndicationFeed;
            var key = userId + encoding + maxLength + isPopular;
            var cache = MemoryCache.Default[key] as FeedCacheItem;
            if (cache != null && cache.LastUpdate >= lastUpdated)
            {
                syndicationFeed = cache.Feed;
            }
            else
            {
                MemoryCache.Default.Remove(key);

                syndicationFeed = CreateSyndicationFeed(profile, isPopular);
                var syndicationItems = GetVideos(userId, youtubeEncoding, maxLength, isPopular).ToList();
                if (isPopular)
                {
                    SortByPopularity(syndicationItems, profile.Published);
                }

                foreach (var item in syndicationItems)
                {
                    item.SourceFeed = syndicationFeed;
                }
                syndicationFeed.Items = syndicationItems;

                MemoryCache.Default[key] = new FeedCacheItem
                {
                    Feed = syndicationFeed,
                    LastUpdate = lastUpdated
                };
            }

            return GetFormatter(syndicationFeed);
        }

        public SyndicationFeedFormatter GetPlaylistFeed(string playlistId, string encoding, int maxLength,
            bool isPopular)
        {
            SetBaseAddress();
            var youtubeEncoding = ParseEncoding(encoding);

            SyndicationFeed syndicationFeed;
            var request = new YouTubeRequest(GetRequestSettings(maxLength));
            var playlist = request.Get<PlayListMember>(new Uri(string.Format(PlaylistUrl, playlistId)));
            var lastUpdated = playlist.AtomFeed.Updated;
            var key = playlistId + encoding + maxLength;

            var cache = MemoryCache.Default[key] as FeedCacheItem;
            if (cache != null && cache.LastUpdate >= lastUpdated)
            {
                syndicationFeed = cache.Feed;
            }
            else
            {
                MemoryCache.Default.Remove(key);
                syndicationFeed = CreatePlaylistFeed(playlist, youtubeEncoding, isPopular);

                MemoryCache.Default[key] = new FeedCacheItem
                {
                    Feed = syndicationFeed,
                    LastUpdate = lastUpdated
                };
            }

            return GetFormatter(syndicationFeed);
        }

        public void GetVideo(string videoId, string encoding)
        {
            var resoultion = int.Parse(ParseEncoding(encoding).ToString().Substring(4).Replace("p", string.Empty));
            var orderedVideos = DownloadUrlResolver.GetDownloadUrls(string.Format(VideoUrl, videoId), false).
                Where(_ => _.VideoType == VideoType.Mp4).OrderByDescending(_ => _.Resolution).
                ToList();
            var video = orderedVideos.FirstOrDefault(_ => _.Resolution == resoultion) ?? orderedVideos.First();
            if (video.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(video);
            }

            Debug.WriteLine("final video url: " + video.DownloadUrl);
            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers["Location"] = video.DownloadUrl;
        }

        #endregion

        #region Methods

        private static void SortByPopularity(IEnumerable<SyndicationItem> syndicationItems, DateTime startDate)
        {
            var i = 0;
            foreach (var item in syndicationItems)
            {
                item.PublishDate = startDate.AddDays(i);
                i++;
                item.Title = new TextSyndicationContent(string.Format("{0}. {1}", i, item.Title.Text));
            }
        }

        private static YouTubeEncoding ParseEncoding(string encoding)
        {
            YouTubeEncoding youtubeEncoding;
            if (!Enum.TryParse(encoding, out youtubeEncoding))
            {
                youtubeEncoding = YouTubeEncoding.MP4_360p;
            }
            return youtubeEncoding;
        }

        private static SyndicationFeedFormatter GetFormatter(SyndicationFeed syndicationFeed)
        {
            // Return ATOM or RSS based on query string
            // rss -> http://localhost:8733/Design_Time_Addresses/SyndicationService/Feed1/
            // atom -> http://localhost:8733/Design_Time_Addresses/SyndicationService/Feed1/?format=atom
            var query = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.QueryParameters["format"];
            return query == "atom"
                ? (SyndicationFeedFormatter) new Atom10FeedFormatter(syndicationFeed)
                : new Rss20FeedFormatter(syndicationFeed);
        }

        private SyndicationFeed CreatePlaylistFeed(Feed<PlayListMember> playlist, YouTubeEncoding encoding,
            bool isPopular)
        {
            var syndicationFeed = new ItunesFeed(
                playlist.AtomFeed.Title.Text + (isPopular ? " (By Popularity)" : string.Empty),
                playlist.AtomFeed.Subtitle.Text,
                new Uri(playlist.AtomFeed.Links.FirstOrDefault(l => l.Rel == "alternate").HRef.Content))
            {
                ImageUrl = new Uri(playlist.AtomFeed.Logo.Uri.Content)
            };

            var videos = playlist.Entries.ToList();
            if (isPopular)
            {
                videos = videos.OrderByDescending(video => video.ViewCount).ToList();
            }
            var firstDate = videos.First().YouTubeEntry.Published;
            var items = videos.Select(playlistMember => CreateItem(playlistMember, encoding)).ToList();
            if (isPopular)
            {
                SortByPopularity(items, firstDate);
            }
            syndicationFeed.Items = items;
            return syndicationFeed;
        }

        private static ItunesFeed CreateSyndicationFeed(ProfileEntry profile, bool isPopular)
        {
            var title = Regex.IsMatch(profile.Title.Text.Substring(0, 1), "[a-z]|[A-Z]")
                ? profile.Title.Text
                : profile.UserName;
            var feed = new ItunesFeed(
                title + (isPopular ? " (By Popularity)" : string.Empty),
                GetSummary(profile),
                new Uri(profile.Links.FirstOrDefault(l => l.Rel == "alternate").HRef.Content));
            var thumbnail = profile.ExtensionElements.Where(e => e.XmlName == "thumbnail").
                Cast<XmlExtension>().
                SingleOrDefault();
            if (thumbnail != null)
            {
                feed.ImageUrl = new Uri(thumbnail.Node.Attributes["url"].Value.Replace("https://", "http://"));
            }

            return feed;
        }

        private static string GetSummary(AtomEntry profile)
        {
            var title = profile.Summary.Text;
            if (title.Length < 1) return title;

            // Assume it's english
            return Regex.IsMatch(title.Substring(0, 1), "[a-z]|[A-Z]") ? profile.Summary.Text : string.Empty;
        }

        private static YouTubeRequestSettings GetRequestSettings(int maxLength)
        {
            var settings = new YouTubeRequestSettings(
                GeneralInformation.ApplicationName,
                DeveloperKey)
            {
                PageSize = 50,
                UseSSL = false,
                AutoPaging = true
            };
            if (maxLength > 0)
            {
                if (maxLength < settings.PageSize)
                {
                    settings.PageSize = maxLength;
                }
                settings.Maximum = maxLength;
            }

            return settings;
        }

        private IEnumerable<SyndicationItem> GetVideos(string userId, YouTubeEncoding encoding, int maxLength,
            bool isPopular)
        {
            var videos = new YouTubeRequest(GetRequestSettings(maxLength)).GetVideoFeed(userId).Entries;
            if (isPopular)
            {
                videos = videos.OrderByDescending(video => video.ViewCount);
            }
            return videos.Select(video => CreateItem(video, encoding));
        }

        private SyndicationItem CreateItem(Video video, YouTubeEncoding encoding)
        {
            var item = new SyndicationItem(video.Title, string.Empty, video.WatchPage)
            {
                Id = video.Id,
                PublishDate = video.YouTubeEntry.Published,
                Summary = new TextSyndicationContent(video.Summary)
            };
            foreach (var category in video.Categories.Where(_ => _.Scheme.Content.ToLower().Contains("categories")))
            {
                item.Categories.Add(new SyndicationCategory(category.Term));
            }

            item.ElementExtensions.Add(
                new XElement("enclosure",
                    new XAttribute("type", "video/mp4"),
                    new XAttribute("url",
                        _baseAddress +
                        string.Format("/{0}?videoId={1}&encoding={2}", "Video.mp4", video.VideoId, encoding)))
                    .CreateReader());
            item.ElementExtensions.Add(new XElement("description", video.Description));
            return item;
        }

        private void SetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            _baseAddress = string.Format("http://{0}:{1}/FeedService", transportAddress.DnsSafeHost,
                transportAddress.Port);
        }

        #endregion

        #region Types

        private class FeedCacheItem
        {
            public SyndicationFeed Feed { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        #endregion
    }
}
