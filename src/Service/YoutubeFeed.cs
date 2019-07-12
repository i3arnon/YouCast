using Google.Apis.Services;
using Google.Apis.YouTube.v3.Data;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;
using System.Threading.Tasks;
using System.Xml.Linq;
using YoutubeExplode;
using YoutubeExplode.Models.MediaStreams;
using Video = Google.Apis.YouTube.v3.Data.Video;
using YouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace Service
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    public sealed class YoutubeFeed : IYoutubeFeed
    {
        private const string _channelUrlFormat = "http://www.youtube.com/channel/{0}";
        private const string _videoUrlFormat = "http://www.youtube.com/watch?v={0}";
        private const string _playlistUrlFormat = "http://www.youtube.com/playlist?list={0}";

        private readonly YoutubeClient _youtubeClient;
        private readonly YouTubeService _youtubeService;

        public YoutubeFeed()
        {
            _youtubeClient = new YoutubeClient();
            _youtubeService =
                new YouTubeService(
                    new BaseClientService.Initializer
                    {
                        ApiKey = "AIzaSyD0E4ozDor6cgdyQKHvOgLCrrQMEX226Qc",
                        ApplicationName = "YouCast2",
                    });
        }

        public async Task<SyndicationFeedFormatter> GetUserFeedAsync(
            string userId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            var baseAddress = GetBaseAddress();

            const string fields = "items(contentDetails,id,snippet)";
            var listRequestForUsername = _youtubeService.Channels.List("snippet,contentDetails");
            listRequestForUsername.ForUsername = userId;
            listRequestForUsername.MaxResults = 1;
            listRequestForUsername.Fields = fields;

            var listRequestForId = _youtubeService.Channels.List("snippet,contentDetails");
            listRequestForId.Id = userId;
            listRequestForId.MaxResults = 1;
            listRequestForId.Fields = fields;

            var channel = (await Task.WhenAll(listRequestForUsername.ExecuteAsync(), listRequestForId.ExecuteAsync())).
                SelectMany(_ => _.Items).
                First();

            var arguments = new Arguments(
                channel.ContentDetails.RelatedPlaylists.Uploads,
                encoding,
                maxLength,
                isPopular);

            var feed = new ItunesFeed(
                GetTitle(channel.Snippet.Title, arguments),
                channel.Snippet.Description,
                new Uri(string.Format(_channelUrlFormat, channel.Id)))
            {
                ImageUrl = new Uri(channel.Snippet.Thumbnails.Medium.Url),
                Items = await GenerateItemsAsync(
                    baseAddress,
                    channel.Snippet.PublishedAt.GetValueOrDefault(),
                    arguments),
            };

            return GetFormatter(feed);
        }

        public async Task<SyndicationFeedFormatter> GetPlaylistFeedAsync(
            string playlistId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            var baseAddress = GetBaseAddress();

            var arguments = new Arguments(
                playlistId,
                encoding,
                maxLength,
                isPopular);

            var playlistRequest = _youtubeService.Playlists.List("snippet");
            playlistRequest.Id = playlistId;
            playlistRequest.MaxResults = 1;

            var playlist = (await playlistRequest.ExecuteAsync()).Items.First();

            var feed = new ItunesFeed(
                GetTitle(playlist.Snippet.Title, arguments),
                playlist.Snippet.Description,
                new Uri(string.Format(_playlistUrlFormat, playlist.Id)))
            {
                ImageUrl = new Uri(playlist.Snippet.Thumbnails.Medium.Url),
                Items = await GenerateItemsAsync(
                    baseAddress,
                    playlist.Snippet.PublishedAt.GetValueOrDefault(),
                    arguments),
            };

            return GetFormatter(feed);
        }

        public async Task GetVideoAsync(string videoId, string encoding)
        {
            var context = WebOperationContext.Current;
            
            var resolution = 720;
            try
            {
                resolution = int.Parse(encoding.Remove(encoding.Length - 1).Substring(startIndex: 4));
            }
            catch
            {
            }

            try
            {
                var streamInfoSet = await _youtubeClient.GetVideoMediaStreamInfosAsync(videoId);
                var muxedStreamInfo =
                    streamInfoSet.Muxed.FirstOrDefault(_ => _.VideoQuality.GetResolution() == resolution) ??
                    streamInfoSet.Muxed.WithHighestVideoQuality();

                var redirectUri = muxedStreamInfo?.Url;
                if (redirectUri != null)
                {
                    context.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
                    context.OutgoingResponse.Headers["Location"] = redirectUri;
                    return;
                }
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle(exception);
            }

            context.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
        }

        public async Task GetAudioAsync(string videoId)
        {
            var context = WebOperationContext.Current;

            var streamInfoSet = await _youtubeClient.GetVideoMediaStreamInfosAsync(videoId);
            var audios = streamInfoSet.Audio.Where(audio => audio.AudioEncoding == AudioEncoding.Aac).ToList();
            if (audios.Count > 0)
            {
                var redirectUri = audios.MaxBy(audio => audio.Bitrate).Url;
                context.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
                context.OutgoingResponse.Headers["Location"] = redirectUri;
            }
            else
            {
                context.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
            }
        }

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
                playlistItemsListRequest.Fields = "items(id,snippet),nextPageToken";

                var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();
                playlistItems.AddRange(playlistItemsListResponse.Items);
                nextPageToken = playlistItemsListResponse.NextPageToken;
            }

            return playlistItems.Take(arguments.MaxLength);
        }

        private static SyndicationItem GenerateItem(PlaylistItem playlistItem, string baseAddress, Arguments arguments)
        {
            var item = new SyndicationItem(
                playlistItem.Snippet.Title,
                string.Empty,
                new Uri(string.Format(_videoUrlFormat, playlistItem.Snippet.ResourceId.VideoId)))
            {
                Id = playlistItem.Snippet.ResourceId.VideoId,
                PublishDate = playlistItem.Snippet.PublishedAt.GetValueOrDefault(),
                Summary = new TextSyndicationContent(playlistItem.Snippet.Description),
            };

            if (arguments.Encoding == "Audio")
            {
                item.ElementExtensions.Add(
                    new XElement(
                        "enclosure",
                        new XAttribute("type", "audio/mp4"),
                        new XAttribute(
                            "url",
                            baseAddress + $"/Audio.m4a?videoId={playlistItem.Snippet.ResourceId.VideoId}")).CreateReader());
            }
            else
            {
                item.ElementExtensions.Add(
                    new XElement(
                        "enclosure",
                        new XAttribute("type", "video/mp4"),
                        new XAttribute(
                            "url",
                            baseAddress + $"/Video.mp4?videoId={playlistItem.Snippet.ResourceId.VideoId}&encoding={arguments.Encoding}")).CreateReader());
            }

            return item;
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
                userVideo.Title = new TextSyndicationContent($"{i}. {userVideo.Title.Text}");
            }

            return userVideos;
        }

        private async Task<IEnumerable<Video>> GetVideosAsync(IEnumerable<string> videoIds) =>
            (await Task.WhenAll(videoIds.Batch(50).Select(GetVideoBatchAsync))).SelectMany(_ => _);

        private async Task<IEnumerable<Video>> GetVideoBatchAsync(IEnumerable<string> videoIds)
        {
            var statisticsRequest = _youtubeService.Videos.List("statistics");
            statisticsRequest.Id = string.Join(",", videoIds);
            statisticsRequest.MaxResults = 50;
            statisticsRequest.Fields = "items(id,statistics)";
            return (await statisticsRequest.ExecuteAsync()).Items;
        }

        private static string GetTitle(string title, Arguments arguments) =>
            arguments.IsPopular ? $"{title} (By Popularity)" : title;

        private static SyndicationFeedFormatter GetFormatter(SyndicationFeed syndicationFeed) =>
            new Rss20FeedFormatter(syndicationFeed);

        private static string GetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            return $"http://{transportAddress.DnsSafeHost}:{transportAddress.Port}/FeedService";
        }
    }
}