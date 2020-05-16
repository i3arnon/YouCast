using System;
using YoutubeExplode.Videos.Streams;
using static YoutubeExplode.Videos.Streams.VideoQuality;

namespace Service
{
    public static class VideoQualityExtensions
    {
        public static int GetResolution(this VideoQuality videoQuality)
        {
            switch (videoQuality)
            {
                case Low144:
                    return 144;
                case Low240:
                    return 240;
                case Medium360:
                    return 360;
                case Medium480:
                    return 480;
                case High720:
                    return 720;
                case High1080:
                    return 1080;
                case High1440:
                    return 1440;
                case High2160:
                    return 2160;
                case High3072:
                    return 3072;
                case High4320:
                    return 4320;
                default:
                    throw new ArgumentOutOfRangeException(nameof(videoQuality), videoQuality, message: null);
            }
        }
    }
}