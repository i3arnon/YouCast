using System;
using YoutubeExplode.Models.MediaStreams;

namespace Service
{
    public static class VideoQualityExtensions
    {
        public static int GetResolution(this VideoQuality videoQuality)
        {
            switch (videoQuality)
            {
                case VideoQuality.Low144:
                    return 144;
                case VideoQuality.Low240:
                    return 240;
                case VideoQuality.Medium360:
                    return 360;
                case VideoQuality.Medium480:
                    return 480;
                case VideoQuality.High720:
                    return 720;
                case VideoQuality.High1080:
                    return 1080;
                case VideoQuality.High1440:
                    return 1440;
                case VideoQuality.High2160:
                    return 2160;
                case VideoQuality.High3072:
                    return 3072;
                case VideoQuality.High4320:
                    return 4320;
                default:
                    throw new ArgumentOutOfRangeException(nameof(videoQuality), videoQuality, message: null);
            }
        }
    }
}