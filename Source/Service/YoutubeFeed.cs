using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using MoreLinq;
using SyndicationService;
using YoutubeExtractor;

namespace Service
{
    [ServiceBehavior(
        ConcurrencyMode = ConcurrencyMode.Multiple,
        UseSynchronizationContext = false)]
    public sealed class YoutubeFeed : ISyndicationFeed
    {
        #region Data Members

        private const string ChannelUrlFormat = "http://www.youtube.com/channel/{0}";
        private const string VideoUrlFormat = "http://www.youtube.com/watch?v={0}";
        private const string PlaylistUrlFormat = "http://www.youtube.com/playlist?list={0}";

        private readonly YouTubeService _youtubeService;

        #endregion

        #region Constructors

        public YoutubeFeed()
        {
            _youtubeService = new YouTubeService(new BaseClientService.Initializer
            {
                ApiKey = "AIzaSyD0E4ozDor6cgdyQKHvOgLCrrQMEX226Qc",
                ApplicationName = "YouCast2",
            });
        }

        #endregion

        #region ISyndicationFeed

        public async Task<SyndicationFeedFormatter> GetUserFeedAsync(string userId, string encoding, int maxLength,
            bool isPopular = false)
        {
            if (maxLength <= 0)
            {
                maxLength = int.MaxValue;
            }

            var baseAddress = GetBaseAddress();

            var listRequest = _youtubeService.Channels.List("snippet,contentDetails");
            listRequest.ForUsername = userId;
            listRequest.MaxResults = 1;

            var channel = (await listRequest.ExecuteAsync()).Items.FirstOrDefault();
            if (channel == null)
            {
                listRequest.ForUsername = null;
                listRequest.Id = userId;
                channel = (await listRequest.ExecuteAsync()).Items.First();
            }

            var userFeed = new ItunesFeed(
                channel.Snippet.Title + (isPopular ? " (By Popularity)" : string.Empty),
                channel.Snippet.Description,
                new Uri(string.Format(ChannelUrlFormat, channel.Id)))
            {
                ImageUrl = new Uri(channel.Snippet.Thumbnails.Medium.Url),
            };

            IEnumerable<PlaylistItem> playlistItems = (await GetPlaylistItemsAsync(channel.ContentDetails.RelatedPlaylists.Uploads, maxLength)).ToList();
            var userVideos = playlistItems.Select(_ => GenerateItem(_, baseAddress, encoding));
            if (isPopular)
            {
                userVideos = await SortByPopularityAsync(userVideos, playlistItems, channel.Snippet.PublishedAt.GetValueOrDefault());
            }
            userFeed.Items = userVideos;
            return GetFormatter(userFeed);
        }

        public async Task<SyndicationFeedFormatter> GetPlaylistFeedAsync(string playlistId, string encoding, int maxLength,
            bool isPopular = false)
        {
            if (maxLength <= 0)
            {
                maxLength = int.MaxValue;
            }

            var baseAddress = GetBaseAddress();

            var playlistRequest = _youtubeService.Playlists.List("snippet");
            playlistRequest.Id = playlistId;
            playlistRequest.MaxResults = 1;

            var playlist = (await playlistRequest.ExecuteAsync()).Items.First();

            var userFeed = new ItunesFeed(
                playlist.Snippet.Title + (isPopular ? " (By Popularity)" : string.Empty),
                playlist.Snippet.Description,
                new Uri(string.Format(PlaylistUrlFormat, playlist.Id)))
            {
                ImageUrl = new Uri(playlist.Snippet.Thumbnails.Medium.Url),
            };

            IEnumerable<PlaylistItem> playlistItems = (await GetPlaylistItemsAsync(playlistId, maxLength)).ToList();
            var userVideos = playlistItems.Select(_ => GenerateItem(_, baseAddress, encoding));
            if (isPopular)
            {
                userVideos = await SortByPopularityAsync(userVideos, playlistItems, playlist.Snippet.PublishedAt.GetValueOrDefault());
            }
            userFeed.Items = userVideos;
            return GetFormatter(userFeed);
        }

        public void GetVideo(string videoId, string encoding)
        {
            var resoultion = int.Parse(encoding.Remove(encoding.Length-1).Substring(4));
            var orderedVideos = DownloadUrlResolver.GetDownloadUrls(string.Format(VideoUrlFormat, videoId), false).
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

        private async Task<IEnumerable<SyndicationItem>> SortByPopularityAsync(
            IEnumerable<SyndicationItem> userVideos, 
            IEnumerable<PlaylistItem> playlistItems, 
            DateTime startDate)
        {
            var videoIds = playlistItems.Select(_ => _.Snippet.ResourceId.VideoId).ToHashSet();
            var videos = await Task.WhenAll(videoIds.Batch(50).Select(GetVideosAsync));
            var videoDictionary = videos.SelectMany(_ => _).ToDictionary(_ => _.Id, _ => _);
            userVideos =
                userVideos.OrderByDescending(
                    _ => videoDictionary[_.Id].Statistics.ViewCount.GetValueOrDefault()).ToList();
            var i = 0;
            foreach (var userVideo in userVideos)
            {
                userVideo.PublishDate = startDate.AddDays(i);
                i++;
                userVideo.Title = new TextSyndicationContent(string.Format("{0}. {1}", i, userVideo.Title.Text));
            }

            return userVideos;
        }

        private static SyndicationItem GenerateItem(PlaylistItem playlistItem, string baseAddress, string encoding)
        {
            var item = new SyndicationItem(playlistItem.Snippet.Title, string.Empty,
                   new Uri(string.Format(VideoUrlFormat, playlistItem.Snippet.ResourceId.VideoId)))
            {
                Id = playlistItem.Snippet.ResourceId.VideoId,
                PublishDate = playlistItem.Snippet.PublishedAt.GetValueOrDefault(),
                Summary = new TextSyndicationContent(playlistItem.Snippet.Description),
            };

            item.ElementExtensions.Add(
                new XElement("enclosure",
                    new XAttribute("type", "video/mp4"),
                    new XAttribute("url",
                        baseAddress + string.Format(
                            "/{0}?videoId={1}&encoding={2}", "Video.mp4", playlistItem.Snippet.ResourceId.VideoId,
                            encoding)))
                    .CreateReader());

            return item;
        }

        private async Task<IEnumerable<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, int maxResults)
        {
            var playlistItems = new List<PlaylistItem>();
            var nextPageToken = string.Empty;
            while (nextPageToken != null && playlistItems.Count < maxResults)
            {
                var playlistItemsListRequest = _youtubeService.PlaylistItems.List("snippet");
                playlistItemsListRequest.PlaylistId = playlistId;
                playlistItemsListRequest.MaxResults = 50;
                playlistItemsListRequest.PageToken = nextPageToken;

                var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();
                playlistItems.AddRange(playlistItemsListResponse.Items);
                nextPageToken = playlistItemsListResponse.NextPageToken;
            }

            return playlistItems.Take(maxResults);
        }

        private async Task<IEnumerable<Video>> GetVideosAsync(IEnumerable<string> videoIds)
        {
            return (await Task.WhenAll(videoIds.Batch(50).Select(GetVideoBatchAsync))).SelectMany(_ => _);
        }

        private async Task<IEnumerable<Video>> GetVideoBatchAsync(IEnumerable<string> videoIds)
        {
            var statisticsRequest = _youtubeService.Videos.List("statistics");
            statisticsRequest.Id = string.Join(",", videoIds);
            statisticsRequest.MaxResults = 50;

            return (await statisticsRequest.ExecuteAsync()).Items;
        }

        private static SyndicationFeedFormatter GetFormatter(SyndicationFeed syndicationFeed)
        {
            return new Rss20FeedFormatter(syndicationFeed);
        }

        private string GetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            return string.Format("http://{0}:{1}/FeedService", transportAddress.DnsSafeHost,
                transportAddress.Port);
        }

        #endregion
    }
}
