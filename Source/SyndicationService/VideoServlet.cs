using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace SyndicationService
{
    class VideoServlet
    {
        private readonly HttpClient _client;
        private readonly List<YouTubeEncoding> _encodings = new List<YouTubeEncoding>();
        public const string VideoURLFormat = "http://www.youtube.com/watch?v={0}";

        public VideoServlet()
        {
            _encodings.Add(YouTubeEncoding.MP4_360p);
            _encodings.Add(YouTubeEncoding.MP4_720p);
            _encodings.Add(YouTubeEncoding.MP4_1080p);
            _encodings.Add(YouTubeEncoding.MP4_3072p);

            _encodings.Add(YouTubeEncoding.MP4_240p_3D);
            _encodings.Add(YouTubeEncoding.MP4_360p_3D);
            _encodings.Add(YouTubeEncoding.MP4_520p_3D);
            _encodings.Add(YouTubeEncoding.MP4_720p_3D);

            _client = new HttpClient();
        }

        public string GetVideoURL(string videoId, YouTubeEncoding encoding)
        {
            return GetVideoLink(
                _client.GetStringAsync(
                    new Uri(string.Format(VideoURLFormat, videoId))).Result,
                encoding);
        }

        private string GetVideoLink(string content, YouTubeEncoding format)
        {
            string urlMap = ExtractURLMap(content);
            if (string.IsNullOrWhiteSpace(urlMap))
            {
                urlMap = content;
            }

            var videoUrls = ParseUrls(urlMap);
            if (videoUrls == null || videoUrls.Length == 0)
            {
                if (urlMap.IndexOf("liveplay", StringComparison.Ordinal) >= 0)
                {
                    Debug.Fail("Can't handle live videos.");
                }
                else
                {
                    Debug.Fail("Can't find URLs in the URL map: " + urlMap);
                }
            }

            var url = GeURLByEncoding(videoUrls, format);
            if (url == null)
            {
                Debug.Fail("Can't find url");
            }

            string cleanURL = url.GetURL();
            if (string.IsNullOrWhiteSpace(cleanURL))
            {
                Debug.WriteLine("Couldn't parse URL from " + url);
            }

            return cleanURL;
        }
        private static string ExtractURLMap(string content)
        {
            int mapStartIndex = content.IndexOf("\"url_encoded_fmt_stream_map\"", StringComparison.Ordinal);
            if (mapStartIndex < 0)
            {
                return string.Empty;
            }

            content = content.Substring(mapStartIndex);
            int mapEndIndex = content.IndexOf("\",", StringComparison.Ordinal);
            if (mapEndIndex > 0)
            {
                content = content.Substring(0, mapEndIndex);
            }
            return content.Replace("\\u0026", "&");
        }
        
        private static VideoURL[] ParseUrls(string urlMap)
        {
            string[] rawUrls = urlMap.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawUrls.Length == 1)
            {
                // the URLs couldn't be split by a comma. YT have changed the source?
                Trace.WriteLine("Couldn't parse the URL map. Falling back to regular expressions.");
                
                // assuming that the urlMap starts with a key (e.g. type, itag, sig, url, etc.)
                // get this key, and count its occurrences in the urlMap. If we have more
                // than 1, then we'll use this key to build a dynamic regex to extract the URLs
                int indexOfEqualsSign = urlMap.IndexOf("=", StringComparison.Ordinal);
                
                if (indexOfEqualsSign < 0)
                    indexOfEqualsSign = urlMap.IndexOf("%3D", StringComparison.Ordinal);
                
                if (indexOfEqualsSign != -1)
                {
                    string key = urlMap.Substring(0, indexOfEqualsSign);
                    Trace.WriteLine("Found term: " + key);
                    MatchCollection matches = Regex.Matches(urlMap, key, RegexOptions.IgnoreCase);

                    Trace.WriteLine(string.Format("{0} occurrences found for term {1} in the URL map.",matches.Count,key));
                    if (matches.Count == 1)
                    {
                        Trace.WriteLine(key + " not suitable for use in a regex.");
                        return null;
                    }

                    string regex = "(" + key + "=.*?)(?=[^0-9a-zA-Z]" + key + "|$)";
                    Trace.WriteLine("Using Regex " + regex);

                    matches = Regex.Matches(urlMap, regex, RegexOptions.IgnoreCase);
                    Trace.WriteLine("Found matches: " + matches.Count);
                    
                    if (matches.Count == 0) return null;

                    rawUrls = new string[matches.Count];
                    int i = 0;
                    foreach (Match match in matches)
                    {
                        rawUrls[i] = match.Groups[1].Value;
                        i++;
                    }
                }
                else
                {
                    Trace.WriteLine("Failed to parse the URL map: " + urlMap);
                }
            }

            var videoUrLs = new VideoURL[rawUrls.Length];
            for (int i = 0; i < videoUrLs.Length; i++)
            videoUrLs[i] = new VideoURL(rawUrls[i]);

            Debug.WriteLine("Found download URLs: " + videoUrLs.Length);
            
            return videoUrLs;
        }

        private VideoURL GeURLByEncoding(VideoURL[] urls, YouTubeEncoding encoding)
        {
            VideoURL url = null;
            int index = _encodings.IndexOf(encoding);

            while (url == null && index >= 0)
            {
                encoding = _encodings[index];
                url = getVideoURLWithFormat(urls, encoding);
                index--;
            }

            return url;
        }
        private VideoURL getVideoURLWithFormat(VideoURL[] urls, YouTubeEncoding encoding)
        {
            VideoURL url = null;
            for (int i = 0; i < urls.Length; i++)
            {
                int fmt = urls[i].GetFormat();
                if (fmt == (int)encoding)
                {
                    url = urls[i];
                    break;
                }
            }

            if (url != null)
            {
                Debug.WriteLine("Found URL for format: " + encoding.ToString());
            }
            else
            {
                Trace.WriteLine("Couldn't find URL for format: " + encoding.ToString());
            }

            return url;
        }
    }
}
