using Google.GData.Client;
using Google.GData.Extensions;
using Google.GData.YouTube;
using Google.YouTube;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        private const string UserURL = "http://gdata.youtube.com/feeds/api/users/{0}";
        private const string PlaylistURL = "https://gdata.youtube.com/feeds/api/playlists/{0}?v=2";
        private const string VideoURL = "http://www.youtube.com/watch?v={0}";
        private const string DeveloperKey ="AI39si6kaGnRDF4m-BzWNLIfrVP5O0MNS2Up5dfEpy0PnOZ9vhsI6Ro1wLOWhIPohT0CdZa_WiWBRzZCMJ8INxXT_0pyRPOmBA";

        private readonly YouTubeService _service = new YouTubeService(GeneralInformation.ApplicationName, DeveloperKey);
        private readonly VideoServlet _videoServlet = new VideoServlet();

        private string _baseAddress;

        #endregion

        #region Constructors

        #endregion

        #region IFeed

        public SyndicationFeedFormatter GetUserFeed(string userId, string encoding, int maxLength, bool isPopular)
        {
            SetBaseAddress();
            
            var youtubeEncoding = ParseEncoding(encoding);
            var profile = _service.Get(string.Format(UserURL, userId)) as ProfileEntry;
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

                syndicationFeed = CreateSyndicationFeed(profile, youtubeEncoding, isPopular);
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

        public SyndicationFeedFormatter GetPlaylistFeed(string playlistId, string encoding, int maxLength, bool isPopular)
        {
            SetBaseAddress();
            var youtubeEncoding = ParseEncoding(encoding);

            SyndicationFeed syndicationFeed;
            var request = new YouTubeRequest(GetRequestSettings(maxLength));
            var playlist = request.Get<PlayListMember>(new Uri(string.Format(PlaylistURL, playlistId)));
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
            var videoURL = _videoServlet.GetVideoURL(videoId, ParseEncoding(encoding));

            Debug.WriteLine("final video url: " + videoURL);

            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers["Location"] = videoURL;
        }

        public Stream ExtractMp3Audio(string videoId, string encoding)
        {
            return ExtractAudio(videoId, encoding);
        }

        public Stream ExtractAacAudio(string videoId, string encoding)
        {
            return ExtractAudio(videoId, encoding);
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
            string query = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.QueryParameters["format"];
            SyndicationFeedFormatter formatter;
            if (query == "atom")
            {
                formatter = new Atom10FeedFormatter(syndicationFeed);
            }
            else
            {
                formatter = new Rss20FeedFormatter(syndicationFeed);
            }
            return formatter;
        }

        private static Stream ExtractAudio(string videoId, string encoding)
        {
            var youtubeEncoding = ParseEncoding(encoding);
            if (!IsAudio(youtubeEncoding))
            {
                Debug.WriteLine("Extract audio. Wrong encoding");
                return null;
            }

            var audioType = youtubeEncoding == YouTubeEncoding.MP3_Best ? AudioType.Mp3 : AudioType.Aac;
            VideoInfo video = DownloadUrlResolver.GetDownloadUrls(string.Format(VideoURL, videoId))
                .Where(info => info.CanExtractAudio && info.AudioType == audioType)
                .OrderByDescending(info => info.AudioBitrate)
                .First();

            var tempPath = Path.GetTempFileName();
            FileStream stream = null;
            try
            {
                new AudioDownloader(video, tempPath).Execute();
                stream = File.OpenRead(tempPath);
                stream.Seek(0, SeekOrigin.Begin);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }

            return stream;
        }

        private SyndicationFeed CreatePlaylistFeed(Feed<PlayListMember> playlist, YouTubeEncoding encoding, bool isPopular)
        {
            var link = playlist.AtomFeed.Links.FirstOrDefault(l => l.Rel == "alternate");

            var audio = IsAudio(encoding) ? " (Audio)" : string.Empty;
            var popular = isPopular ? " (By Popularity)" : string.Empty;

            var syndicationFeed = new ItunesFeed(
                playlist.AtomFeed.Title.Text + audio + popular,
                playlist.AtomFeed.Subtitle.Text,
                new Uri(link.HRef.Content))
            {
                ImageUrl = new Uri(playlist.AtomFeed.Logo.Uri.Content)
            };

            var videos = playlist.Entries;
            if (isPopular)
            {
                videos = videos.OrderByDescending(video => video.ViewCount);
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

        private static bool IsAudio(YouTubeEncoding encoding)
        {
            return (encoding == YouTubeEncoding.AAC_Best || encoding == YouTubeEncoding.MP3_Best);
        }

        private static ItunesFeed CreateSyndicationFeed(ProfileEntry profile, YouTubeEncoding encoding, bool isPopular)
        {
            var link = profile.Links.FirstOrDefault(l => l.Rel == "alternate");

            var audio = IsAudio(encoding) ? " (Audio)" : string.Empty;
            var popular = isPopular ? " (By Popularity)" : string.Empty;
            var title = Regex.IsMatch(profile.Title.Text.Substring(0, 1), "[a-z]|[A-Z]")
                ? profile.Title.Text
                : profile.UserName;
            var feed = new ItunesFeed(
                title + audio + popular,
                GetSummary(profile),
                new Uri(link.HRef.Content));

            var thumbnail = (from e in profile.ExtensionElements
                where e.XmlName == "thumbnail"
                select (XmlExtension) e).SingleOrDefault();
            if (thumbnail == null) return feed;
            var thumbnailUrl = thumbnail.Node.Attributes["url"].Value.Replace("https://", "http://");
            feed.ImageUrl = new Uri(thumbnailUrl);
            return feed;
        }

        private static string GetSummary(AtomEntry profile)
        {
            string title = profile.Summary.Text;
            if (title.Length < 1) return title;

            title = title.Substring(0, 1);
            if (Regex.IsMatch(title, "[a-z]|[A-Z]"))
            {
                // Assumes its english
                return profile.Summary.Text;
            }
            return string.Empty;
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
            if (maxLength <= 0) return settings;

            if (maxLength < settings.PageSize)
            {
                settings.PageSize = maxLength;
            }
            settings.Maximum = maxLength;
            return settings;
        }

        private IEnumerable<SyndicationItem> GetVideos(string userId, YouTubeEncoding encoding, int maxLength, bool isPopular)
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
            var item = new SyndicationItem(
                video.Title,
                string.Empty,
                video.WatchPage)
            {
                Id = video.Id,
                PublishDate = video.YouTubeEntry.Published,
                Summary = new TextSyndicationContent(video.Summary)
            };

            var categories = video.Categories.Where(c =>
                c.Scheme.Content.ToLower().Contains("categories"));
            foreach (var category in categories)
            {
                item.Categories.Add(new SyndicationCategory(category.Term));
            }

            var mediaType = string.Empty;
            switch (encoding)
            {
                case YouTubeEncoding.MP4_360p:
                case YouTubeEncoding.MP4_720p:
                case YouTubeEncoding.MP4_1080p:
                case YouTubeEncoding.MP4_3072p:
                case YouTubeEncoding.MP4_360p_3D:
                case YouTubeEncoding.MP4_240p_3D:
                case YouTubeEncoding.MP4_720p_3D:
                case YouTubeEncoding.MP4_520p_3D:
                    mediaType = "video/mp4";
                    break;
                case YouTubeEncoding.MP3_Best:
                    mediaType = "audio/mp3";
                    break;
                case YouTubeEncoding.AAC_Best:
                    mediaType = "audio/aac";
                    break;
            }
                
            item.ElementExtensions.Add(
                    new XElement("enclosure",
                        new XAttribute("type", mediaType),
                        new XAttribute("url", CreateProxyURL(video.VideoId, encoding))).CreateReader());
            item.ElementExtensions.Add(new XElement("description", video.Description));
            return item;
        }

        private void SetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            _baseAddress = string.Format("http://{0}:{1}/FeedService", transportAddress.DnsSafeHost, transportAddress.Port);
        }

        private string CreateProxyURL(string videoId, YouTubeEncoding encoding)
        {
            var fileName = string.Empty;
            switch (encoding)
            {
                case YouTubeEncoding.MP4_360p:
                case YouTubeEncoding.MP4_720p:
                case YouTubeEncoding.MP4_1080p:
                case YouTubeEncoding.MP4_3072p:
                case YouTubeEncoding.MP4_360p_3D:
                case YouTubeEncoding.MP4_240p_3D:
                case YouTubeEncoding.MP4_720p_3D:
                case YouTubeEncoding.MP4_520p_3D:
                    fileName = "Video.mp4";
                    break;
                case YouTubeEncoding.AAC_Best:
                    fileName = GeneralInformation.ApplicationName+ ".aac";
                    break;
                case YouTubeEncoding.MP3_Best:
                    fileName = GeneralInformation.ApplicationName + ".mp3";
                    break;
            }

            return _baseAddress + string.Format("/{0}?videoId={1}&encoding={2}", fileName, videoId, encoding);
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
