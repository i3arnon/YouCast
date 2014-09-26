using System.ServiceModel;
using System.ServiceModel.Syndication;
using System.ServiceModel.Web;

namespace SyndicationService
{
    [ServiceContract(SessionMode=SessionMode.NotAllowed) ]
    [ServiceKnownType(typeof(Atom10FeedFormatter))]
    [ServiceKnownType(typeof(Rss20FeedFormatter))]
    public interface IFeed
    {
        [OperationContract]
        [WebGet(UriTemplate = "GetUserFeed?userId={userId}&encoding={encoding}&maxLength={maxLength}&isPopular={isPopular}", BodyStyle = WebMessageBodyStyle.Bare)]
        SyndicationFeedFormatter GetUserFeed(string userId, string encoding, int maxLength, bool isPopular = false);

        [OperationContract]
        [WebGet(UriTemplate = "GetPlaylistFeed?playlistId={playlistId}&encoding={encoding}&maxLength={maxLength}&isPopular={isPopular}", BodyStyle = WebMessageBodyStyle.Bare)]
        SyndicationFeedFormatter GetPlaylistFeed(string playlistId, string encoding, int maxLength, bool isPopular = false);

        [OperationContract]
        [WebGet(UriTemplate = "Video.mp4?videoId={videoId}&encoding={encoding}")]
        void GetVideo(string videoId, string encoding);
    }
}
