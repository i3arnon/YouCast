using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;
using System.Threading.Tasks;
using System.Xml.Linq;
using YoutubeExtractor;

namespace Service
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    public sealed class YoutubeFeed : IYoutubeFeed
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
            _youtubeService =
                new YouTubeService(
                    new BaseClientService.Initializer
                    {
                        ApiKey = "AIzaSyD0E4ozDor6cgdyQKHvOgLCrrQMEX226Qc",
                        ApplicationName = "YouCast2",
                    });
        }

        #endregion

        #region IYoutubeFeed

        public async Task<SyndicationFeedFormatter> GetUserFeedAsync(
            string userId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            var baseAddress = GetBaseAddress();

            var listRequestForUsername = _youtubeService.Channels.List("snippet,contentDetails");
            listRequestForUsername.ForUsername = userId;
            listRequestForUsername.MaxResults = 1;

            var listRequestForId = _youtubeService.Channels.List("snippet,contentDetails");
            listRequestForId.Id = userId;
            listRequestForId.MaxResults = 1;

            var channel = (await Task.WhenAll(
                listRequestForUsername.ExecuteAsync(),
                listRequestForId.ExecuteAsync())).SelectMany(_ => _.Items).First();

            var arguemnts = new Arguments(
                channel.ContentDetails.RelatedPlaylists.Uploads,
                encoding,
                maxLength,
                isPopular);

            var userFeed = new ItunesFeed(
                GetTitle(channel.Snippet.Title, arguemnts),
                channel.Snippet.Description,
                new Uri(string.Format(ChannelUrlFormat, channel.Id)))
            {
                ImageUrl = new Uri(channel.Snippet.Thumbnails.Medium.Url),
                Items = await GenerateItemsAsync(
                    baseAddress,
                    channel.Snippet.PublishedAt.GetValueOrDefault(),
                    arguemnts),
            };

            return GetFormatter(userFeed);
        }

        public async Task<SyndicationFeedFormatter> GetPlaylistFeedAsync(
            string playlistId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            var baseAddress = GetBaseAddress();

            var arguemnts = new Arguments(
                playlistId,
                encoding,
                maxLength,
                isPopular);

            var playlistRequest = _youtubeService.Playlists.List("snippet");
            playlistRequest.Id = playlistId;
            playlistRequest.MaxResults = 1;

            var playlist = (await playlistRequest.ExecuteAsync()).Items.First();
            var userFeed = new ItunesFeed(
                GetTitle(playlist.Snippet.Title, arguemnts),
                playlist.Snippet.Description,
                new Uri(string.Format(PlaylistUrlFormat, playlist.Id)))
            {
                ImageUrl = new Uri(playlist.Snippet.Thumbnails.Medium.Url),
                Items = await GenerateItemsAsync(
                    baseAddress,
                    playlist.Snippet.PublishedAt.GetValueOrDefault(),
                    arguemnts),
            };

            return GetFormatter(userFeed);
        }

        public void GetVideo(string videoId, string encoding)
        {
            var resoultion = int.Parse(encoding.Remove(encoding.Length - 1).Substring(4));
            var orderedVideos = DownloadUrlResolver.GetDownloadUrls(string.Format(VideoUrlFormat, videoId), false).
                Where(_ => _.VideoType == VideoType.Mp4).
                OrderByDescending(_ => _.Resolution).
                ToList();
            var video = orderedVideos.FirstOrDefault(_ => _.Resolution == resoultion) ?? orderedVideos.First();
            if (video.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(video);
            }

            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers["Location"] = video.DownloadUrl;
        }

        #endregion

        #region Methods

        private async Task<IEnumerable<SyndicationItem>> GenerateItemsAsync(
            string baseAddress,
            DateTime startDate,
            Arguments arguments)
        {
            IEnumerable<PlaylistItem> playlistItems = (await GetPlaylistItemsAsync(arguments)).ToList();
            var userVideos = playlistItems.Select(_ => GenerateItem(_, baseAddress, arguments));
            if (arguments.IsPopular)
            {
                userVideos = await SortByPopularityAsync(userVideos, playlistItems, startDate);
            }

            return userVideos;
        }

        private async Task<IEnumerable<SyndicationItem>> SortByPopularityAsync(
            IEnumerable<SyndicationItem> userVideos,
            IEnumerable<PlaylistItem> playlistItems,
            DateTime startDate)
        {
            var videos = await GetVideosAsync(playlistItems.Select(_ => _.Snippet.ResourceId.VideoId).Distinct());
            var videoDictionary = videos.ToDictionary(_ => _.Id, _ => _);
            userVideos = userVideos.
                OrderByDescending(_ => videoDictionary[_.Id].Statistics.ViewCount.GetValueOrDefault()).
                ToList();
            var i = 0;
            foreach (var userVideo in userVideos)
            {
                userVideo.PublishDate = startDate.AddDays(i);
                i++;
                userVideo.Title = new TextSyndicationContent(string.Format("{0}. {1}", i, userVideo.Title.Text));
            }

            return userVideos;
        }

        private static SyndicationItem GenerateItem(PlaylistItem playlistItem, string baseAddress, Arguments arguments)
        {
            var item = new SyndicationItem(
                playlistItem.Snippet.Title,
                string.Empty,
                new Uri(string.Format(VideoUrlFormat, playlistItem.Snippet.ResourceId.VideoId)))
            {
                Id = playlistItem.Snippet.ResourceId.VideoId,
                PublishDate = playlistItem.Snippet.PublishedAt.GetValueOrDefault(),
                Summary = new TextSyndicationContent(playlistItem.Snippet.Description),
            };

            item.ElementExtensions.Add(
                new XElement(
                    "enclosure",
                    new XAttribute("type", "video/mp4"),
                    new XAttribute(
                        "url",
                        baseAddress + string.Format(
                            "/{0}?videoId={1}&encoding={2}",
                            "Video.mp4",
                            playlistItem.Snippet.ResourceId.VideoId,
                            arguments.Encoding))).CreateReader());
            return item;
        }

        private async Task<IEnumerable<PlaylistItem>> GetPlaylistItemsAsync(Arguments arguments)
        {
            var playlistItems = new List<PlaylistItem>();
            var nextPageToken = string.Empty;
            while (nextPageToken != null && playlistItems.Count < arguments.MaxLength)
            {
                var playlistItemsListRequest = _youtubeService.PlaylistItems.List("snippet");
                playlistItemsListRequest.PlaylistId = arguments.PlaylistId;
                playlistItemsListRequest.MaxResults = 50;
                playlistItemsListRequest.PageToken = nextPageToken;

                var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();
                playlistItems.AddRange(playlistItemsListResponse.Items);
                nextPageToken = playlistItemsListResponse.NextPageToken;
            }

            return playlistItems.Take(arguments.MaxLength);
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

        private static string GetTitle(string title, Arguments arguments)
        {
            return arguments.IsPopular ? string.Format("{0} (By Popularity)", title) : title;
        }

        private static SyndicationFeedFormatter GetFormatter(SyndicationFeed syndicationFeed)
        {
            return new Rss20FeedFormatter(syndicationFeed);
        }

        private static string GetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            return string.Format("http://{0}:{1}/FeedService", transportAddress.DnsSafeHost, transportAddress.Port);
        }

        #endregion
    }
}