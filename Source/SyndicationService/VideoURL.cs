using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SyndicationService
{
    /// <summary>
    ///* The expected format for rawURL (without the new lines):
    ///*   type=video%2Fwebm%3B+codecs%3D%22vp8.0%2C+vorbis%22\u0026
    ///*   itag=45\u0026
    ///*   url=http%3A%2F%2Fr5---sn-nuj-g0il.c.youtube.com%2Fvideoplayback%3Fexpire%3D1359772705%26sver%3D3%26itag%3D45%26id%3Daf7c8e8b14a72445%26cp%3DU0hUTldSUF9FT0NONF9PTFRIOjBWbzQ0bU5ZWllw%26ms%3Dau%26mt%3D1359751214%26sparams%3Dcp%252Cid%252Cip%252Cipbits%252Citag%252Cratebypass%252Csource%252Cupn%252Cexpire%26mv%3Dm%26source%3Dyoutube%26fexp%3D913606%252C901700%252C916612%252C922910%252C928006%252C920704%252C912806%252C922403%252C922405%252C929901%252C913605%252C925710%252C920201%252C913302%252C919009%252C911116%252C926403%252C910221%252C901451%252C919114%26upn%3Dbsvgwu996kc%26newshard%3Dyes%26ipbits%3D8%26ratebypass%3Dyes%26ip%3D1.11.1.111%26key%3Dyt1\u0026
    ///*   sig=502B8CB8901B3D9F993CA700679A1B2D1001AE33.38E86E9E9231C98C2C80FF67C48FED16E8AEA259\u0026
    ///*   fallback_host=tc.v20.cache4.c.youtube.com\u0026
    ///*   quality=hd720
    /// </summary>
    class VideoURL
    {
        #region Data Members

        private const int URLRegexGroup = 1;
        private const int ItagRegexGroup = 2;
        private const int SigRegexGroup = 3;
        private const string MainSplitStr = "\u0026";
        private const string SigRegex = "(sig|signature)(=|%3D)([0-9a-zA-Z]+\\.[0-9a-zA-Z]+)";
        private const string ItagRegex = "itag(=|%3D)([0-9]+)";

        private readonly string _rawURL;
        private readonly YouTubeEncoding _format = YouTubeEncoding.MP4_360p;
        // The following regexes will be used only if the expected format has changed.
        private readonly String[] _urlRegexes =
        {
            // Should be sorted by reliablility, where the most reliable at the top
            // and the least reliable at the bottom
            "url=(http.+?videoplayback.+id=.+?)(\\\\u0026|&)(quality|fallback_host|$)=",
            "(http.+?videoplayback.+id=.+?)(\\\\u0026|&|$)"
        };

        #endregion

        #region Methods

        public VideoURL(string raw)
        {
            _rawURL = raw;
            var splits = _rawURL.Split(new[] {MainSplitStr}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var currString in splits)
            {
                var match = Regex.Match(currString, ItagRegex, RegexOptions.IgnoreCase);

                if (!match.Success) continue;
                var fmt = int.Parse(match.Groups[ItagRegexGroup].Value);
                _format = (YouTubeEncoding)fmt;
                break;
            }
        }

        public int GetFormat()
        {
            return (int)_format;
        }

        internal string GetURL()
        {
            Debug.WriteLine("Cleaning URL.");
            string[] splits = _rawURL.Split(new[] { MainSplitStr }, StringSplitOptions.RemoveEmptyEntries);
            var mainParams = new Dictionary<string, string>();
            foreach (string currString in splits)
            {
                string[] keyAndVal = GetKeyAndVal(currString);
                if (keyAndVal != null)
                {
                    Debug.Assert(keyAndVal.Length == 2);
                    Debug.WriteLine("Found main parameter " + keyAndVal[0] + " - " + keyAndVal[1]);
                    mainParams[keyAndVal[0]] = keyAndVal[1];
                }
                else
                {
                    Debug.WriteLine("Could not parse key and value from " + currString);
                }
            }

            string url = null;
            if (mainParams.ContainsKey("url"))
            {
                url = HttpUtility.UrlDecode(mainParams["url"], Encoding.UTF8);
            }
            else
            {
                // Use regex on the raw url
                Debug.WriteLine("URL not found. Trying to parse the URL using a regular expression.");
                for (var i = 0; i < _urlRegexes.Length; i++)
                {
                    var match = Regex.Match(_rawURL, _urlRegexes[i], RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        url = HttpUtility.UrlDecode(match.Groups[URLRegexGroup].Value, Encoding.UTF8);
                        Debug.WriteLine("Found URL using regex number " + i);
                        break;
                    }
                }
            }

            if (url != null)
            {
                url = ValidateParametersInURL(url, mainParams);
            }
            else
            {
                Debug.WriteLine("Could not find URL in: " + _rawURL);
            }
            return url;
        }

        private string[] GetKeyAndVal(string currString)
        {
            var match = Regex.Match(currString, "^([^=]*)=(.*)$", RegexOptions.IgnoreCase);
            return match.Success ? new[] { match.Groups[1].Value, match.Groups[2].Value } : null;
        }

        private string ValidateParametersInURL(string url, Dictionary<string, string> mainParams)
        {
            // check that it contains a signature and an itag
            // 1. signature. Three possible cases:
            // 1.1 url may not contain a signature
            // 1.2 url may contain a signature with the name "sig"
            // 1.3 url may contain a signature with the correct name "signature"
            if (!url.Contains("signature=") && !url.Contains("sig="))
            {
                string sig = null;
                // Case 1:
                // Check if mainParams contains one, otherwise use a regex.
                if (mainParams.ContainsKey("signature"))
                {
                    sig = mainParams["signature"];
                }
                else if (mainParams.ContainsKey("sig"))
                {
                    sig = mainParams["sig"];
                }
                else
                {
                    // Fallback to a regex.
                    var match = Regex.Match(_rawURL, SigRegex, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        sig = match.Groups[SigRegexGroup].Value;
                    }
                    else
                    {
                        Debug.WriteLine("Could not find signature for URL: " + _rawURL);
                    }
                }

                url = url + "&signature=" + sig;
            }
            else if (url.Contains("sig="))
            {
                // Case 2: replace sig with signature
                url = url.Replace("sig=", "signature=");
            }
            else
            {
                // Case 3:
                // just check that there isn't a mismatch between it and the
                // one in mainParams, if any.
                string sig = null;
                if (mainParams.ContainsKey("signature"))
                {
                    sig = mainParams["signature"];
                }
                else if (mainParams.ContainsKey("sig"))
                {
                    sig = mainParams["sig"];
                }

                if (sig != null)
                {
                    int indexOfSig = url.IndexOf(sig, StringComparison.Ordinal);
                    if (indexOfSig < 0)
                        // mismatch between the signatures
                        Debug.WriteLine(string.Format("Mismatch between signature {0} and the one in the URL: {1}", sig, url));
                }
            }
            
            // 2. itag. Just check that it exists, and that it doesn't conflict
            //    with the one in mainParams, if any.
            string itag = mainParams["itag"];
            if (url.Contains("itag="))
            {
                // check that it doesn't conflict with the one in mainParams, if any.
                if (itag != null && url.IndexOf("itag=" + itag, StringComparison.Ordinal) == -1)
                    Debug.WriteLine(string.Format("Mismatch between itag {0} and the one in the URL: {1}", itag, url));
            }
            else
            {
                // add the one in mainParams, if any
                if (itag != null)
                    url = url + "&itag=" + itag;
                else
                {
                    // url and mainParams has no itag, use the value returned by getFormat
                    int fmt = GetFormat();
                    Debug.WriteLine("Could not find itag. Using " + fmt);
                    url = url + "&itag=" + fmt;
                }
            }

            return url;
        }

        #endregion
    }
}
