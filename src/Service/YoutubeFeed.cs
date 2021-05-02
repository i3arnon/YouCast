using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;
using System.Threading.Tasks;
using System.Xml.Linq;
using Google.Apis.Services;
using Google.Apis.YouTube.v3.Data;
using MoreLinq;
using YoutubeExplode;
using Video = Google.Apis.YouTube.v3.Data.Video;
using YouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace Service
{
    [ServiceBehavior(
        ConcurrencyMode = ConcurrencyMode.Multiple,
        InstanceContextMode = InstanceContextMode.Single,
        UseSynchronizationContext = false)]
    public sealed class YoutubeFeed : IYoutubeFeed
    {
        private const string _channelUrlFormat = "http://www.youtube.com/channel/{0}";
        private const string _videoUrlFormat = "http://www.youtube.com/watch?v={0}";
        private const string _playlistUrlFormat = "http://www.youtube.com/playlist?list={0}";

        private readonly YoutubeClient _youtubeClient;
        private readonly YouTubeService _youtubeService;

        public YoutubeFeed(string applicationName, string apiKey)
        {
            _youtubeClient = new YoutubeClient();
            _youtubeService =
                new YouTubeService(
                    new BaseClientService.Initializer
                    {
                        ApiKey = apiKey,
                        ApplicationName = applicationName
                    });
        }

        public async Task<SyndicationFeedFormatter> GetUserFeedAsync(
            string userId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            return await GetFeedFormatterAsync(GetFeedAsync);

            async Task<ItunesFeed> GetFeedAsync(string baseAddress)
            {
                var channel =
                    await GetChannelAsync(userId) ??
                    await FindChannelAsync(userId);

                var arguments = new Arguments(
                    channel.ContentDetails.RelatedPlaylists.Uploads,
                    encoding,
                    maxLength,
                    isPopular);

                return new ItunesFeed(
                    GetTitle(channel.Snippet.Title, arguments),
                    channel.Snippet.Description,
                    new Uri(string.Format(_channelUrlFormat, channel.Id)))
                {
                    ImageUrl = new Uri(channel.Snippet.Thumbnails.Medium.Url),
                    Items = await GenerateItemsAsync(
                        baseAddress,
                        channel.Snippet.PublishedAt.GetValueOrDefault(),
                        arguments)
                };
            }

            async Task<Channel> GetChannelAsync(string id)
            {
                var listRequestForId = _youtubeService.Channels.List("snippet,contentDetails");
                listRequestForId.Id = id;
                listRequestForId.MaxResults = 1;
                listRequestForId.Fields = "items(contentDetails,id,snippet)";

                var channelListResponse = await listRequestForId.ExecuteAsync();
                return channelListResponse.Items?.Single();
            }

            async Task<Channel> FindChannelAsync(string username)
            {
                var listRequestForUsername = _youtubeService.Channels.List("snippet,contentDetails");
                listRequestForUsername.ForUsername = username;
                listRequestForUsername.MaxResults = 1;
                listRequestForUsername.Fields = "items(contentDetails,id,snippet)";

                var channelListResponse = await listRequestForUsername.ExecuteAsync();
                return channelListResponse.Items?.Single();
            }
        }

        public async Task<SyndicationFeedFormatter> GetPlaylistFeedAsync(
            string playlistId,
            string encoding,
            int maxLength,
            bool isPopular)
        {
            return await GetFeedFormatterAsync(GetFeedAsync);

            async Task<ItunesFeed> GetFeedAsync(string baseAddress)
            {
                var arguments =
                    new Arguments(
                        playlistId,
                        encoding,
                        maxLength,
                        isPopular);

                var playlistRequest = _youtubeService.Playlists.List("snippet");
                playlistRequest.Id = playlistId;
                playlistRequest.MaxResults = 1;

                var playlist = (await playlistRequest.ExecuteAsync()).Items.First();

                return new ItunesFeed(
                    GetTitle(playlist.Snippet.Title, arguments),
                    playlist.Snippet.Description,
                    new Uri(string.Format(_playlistUrlFormat, playlist.Id)))
                {
                    ImageUrl = new Uri(playlist.Snippet.Thumbnails.Medium.Url),
                    Items = await GenerateItemsAsync(
                        baseAddress,
                        playlist.Snippet.PublishedAt.GetValueOrDefault(),
                        arguments)
                };
            }
        }

        public async Task GetVideoAsync(string videoId, string encoding)
        {
            await GetContentAsync(GetVideoUriAsync);

            async Task<string> GetVideoUriAsync()
            {
                var resolution = 720;
                try
                {
                    resolution = int.Parse(encoding.Remove(encoding.Length - 1).Substring(startIndex: 4));
                }
                catch
                {
                }

                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
                var muxedStreamInfos = streamManifest.GetMuxedStreams().ToList();
                var muxedStreamInfo =
                    muxedStreamInfos.FirstOrDefault(_ => _.VideoResolution.Height == resolution) ??
                    muxedStreamInfos.MaxBy(_ => _.VideoQuality);

                return muxedStreamInfo?.Url;
            }
        }

        public async Task GetAudioAsync(string videoId)
        {
            await GetContentAsync(GetAudioUriAsync);

            async Task<string> GetAudioUriAsync()
            {
                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
                var audios = streamManifest.GetAudioOnlyStreams().ToList();
                return audios.Count > 0
                    ? audios.MaxBy(audio => audio.Bitrate).Url
                    : null;
            }
        }

        private async Task<SyndicationFeedFormatter> GetFeedFormatterAsync(Func<string, Task<ItunesFeed>> getFeedAsync)
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            var baseAddress = $"http://{transportAddress.DnsSafeHost}:{transportAddress.Port}/FeedService";

            WebOperationContext.Current.OutgoingResponse.ContentType = "application/rss+xml; charset=utf-8";

            var feed = await getFeedAsync(baseAddress);
            return feed.GetRss20Formatter();
        }

        private async Task GetContentAsync(Func<Task<string>> getContentUriAsync)
        {
            var context = WebOperationContext.Current;

            string redirectUri;
            try
            {
                redirectUri = await getContentUriAsync();
            }
            catch
            {
                redirectUri = null;
            }

            context.OutgoingResponse.RedirectTo(redirectUri);
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

        private static string GetBaseAddress()
        {
            var transportAddress = OperationContext.Current.IncomingMessageProperties.Via;
            return $"http://{transportAddress.DnsSafeHost}:{transportAddress.Port}/FeedService";
        }
    }

    public static class OutgoingWebResponseContextExtension
    {
        public static void RedirectTo(this OutgoingWebResponseContext context, string redirectUri)
        {
            if (redirectUri != null)
            {
                context.StatusCode = HttpStatusCode.Redirect;
                context.Headers[nameof(HttpResponseHeader.Location)] = redirectUri;
            }
            else
            {
                context.StatusCode = HttpStatusCode.NotFound;
            }
        }
    }
}