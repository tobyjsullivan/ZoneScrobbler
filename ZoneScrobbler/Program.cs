using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web;
using System.Security.Cryptography;
using System.Xml;

namespace ZoneScrobbler
{
    class Program
    {
        private static List<Track> s_trackHistory = new List<Track>();
        private static string s_sSessionId = string.Empty;
        private static string s_sNowPlayingUrl = string.Empty;
        private static string s_sSubmissionUrl = string.Empty;
        private static DateTime s_dtLastRun = DateTime.Now;

        private const string HISTORY_FILE = "TrackHistory.zsh";

        static void Main(string[] args)
        {
            LoadHistory();

            if (!Authenticate()) return;

            while (true)
            {

                Track[] tracks = FetchZoneTracks();

                if (tracks.Length > 50)
                {
                    Queue<Track> trackQueue = new Queue<Track>(tracks);

                    while (trackQueue.Count > 50)
                    {
                        tracks = new Track[50];
                        for (int i = 0; i < tracks.Length; i++)
                        {
                            tracks[i] = trackQueue.Dequeue();
                        }

                        if (!ScrobbleTracks(tracks))
                        {
                            LoadHistory();
                            trackQueue.Clear();
                            break;
                        }
                    }

                    tracks = new Track[trackQueue.Count];
                    for (int i = 0; i < tracks.Length; i++)
                    {
                        tracks[i] = trackQueue.Dequeue();
                    }
                }

                if (tracks.Length > 0)
                {
                    if (ScrobbleTracks(tracks))
                    {
                        SaveHistory();
                    }
                    else
                    {
                        LoadHistory();
                    }
                }

                Thread.Sleep(20000);
            }
        }

        private static string GetMD5Hash(string sInput)
        {
            byte[] input = Encoding.ASCII.GetBytes(sInput);

            byte[] output = MD5.Create().ComputeHash(input, 0, input.Length);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < output.Length; i++)
            {
                sb.Append(output[i].ToString("X2"));
            }
            return sb.ToString().ToLower();
        }

        private static bool Authenticate()
        {
            string sApiKey = "";
            string sApiSecret = "";

            string sApiTokenSigInput = "api_key" + sApiKey + "methodauth.getToken";
            string sApiTokenSig = GetMD5Hash(sApiTokenSigInput);

            string sTokenRestQuery = "method=auth.getToken&api_key=" + sApiKey + "&api_sig=" + sApiTokenSig;

            XmlDocument docToken = new XmlDocument();
            HttpWebRequest restTokenReq = (HttpWebRequest)HttpWebRequest.Create("http://ws.audioscrobbler.com/2.0/?" + sTokenRestQuery);
            HttpWebResponse restTokenResp = (HttpWebResponse)restTokenReq.GetResponse();
            Stream restTokenRespStream = restTokenResp.GetResponseStream();
            docToken.Load(restTokenRespStream);
            restTokenRespStream.Close();

            XmlNode nodeToken = docToken.DocumentElement.SelectSingleNode("/lfm/token");
            if (nodeToken == null)
            {
                Console.WriteLine("REST Failed (Token)");
                return false;
            }

            string sToken = nodeToken.InnerText;

            Console.WriteLine("Authenticate at http://www.last.fm/api/auth/?api_key=" + sApiKey + "&token=" + sToken);
            System.Diagnostics.Process.Start("http://www.last.fm/api/auth/?api_key=" + sApiKey + "&token=" + sToken);
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();

            string sApiSigInput = "api_key" + sApiKey + "methodauth.getSessiontoken" + sToken + sApiSecret;
            string sApiSig = GetMD5Hash(sApiSigInput);

            string sRestQuery = "method=auth.getSession&api_key=" + sApiKey + "&token=" + sToken + "&api_sig=" + sApiSig;

            XmlDocument doc = new XmlDocument();
            HttpWebRequest restReq = (HttpWebRequest)HttpWebRequest.Create("http://ws.audioscrobbler.com/2.0/?" + sRestQuery);
            HttpWebResponse restResp = (HttpWebResponse)restReq.GetResponse();
            Stream restRespStream = restResp.GetResponseStream();
            doc.Load(restRespStream);
            restRespStream.Close();

            XmlNode nodeSessionKey = doc.DocumentElement.SelectSingleNode("/lfm/session/key");
            if (nodeSessionKey == null)
            {
                Console.WriteLine("REST Failed (SessionKey)");
                return false;
            }

            string sSessionKey = nodeSessionKey.InnerText;

            // Handshake
            string sUrl = "http://post.audioscrobbler.com/";

            string timestamp = DateTimeToUnixTimestamp(DateTime.Now);

            string sAuthInput = sApiSecret + timestamp;

            string sAuth = GetMD5Hash(sAuthInput);

            

            string sQuery = "hs=true&p=1.2.1&c=tst&v=1.0&u=thezone913&t=" + timestamp.ToString() + "&a=" + sAuth + "&api_key=" + sApiKey + "&sk=" + sSessionKey;

            string sQueryUrl = sUrl + "?" + sQuery;

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(sQueryUrl);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();

            Stream respStream = response.GetResponseStream();
            byte[] buffer = new byte[1024];
            int iCount = 0;
            string sData = string.Empty;

            while ((iCount = respStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sData += Encoding.ASCII.GetString(buffer, 0, iCount);
            }

            respStream.Close();

            string[] sResponsePieces = sData.Split('\n');
            if (sResponsePieces.Length < 1) return false;

            if (sResponsePieces[0].Trim() != "OK")
            {
                Console.WriteLine("Authentication Failed: " + sResponsePieces[0]);
                return false;
            }

            s_sSessionId = sResponsePieces[1].Trim();
            s_sNowPlayingUrl = sResponsePieces[2].Trim();
            s_sSubmissionUrl = sResponsePieces[3].Trim();

            return true;
        }

        private static string GetHistoryFilePath()
        {
            return Application.LocalUserAppDataPath + "\\" + HISTORY_FILE;
        }

        private static void LoadHistory()
        {
            s_trackHistory.Clear();

            string sHistoryFile = GetHistoryFilePath();

            FileInfo fi = new FileInfo(sHistoryFile);

            if (!fi.Exists) return;

            FileStream stream = fi.OpenRead();
            string sData = string.Empty;
            byte[] buffer = new byte[1024];
            int iCount = 0;

            while ((iCount = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sData += Encoding.ASCII.GetString(buffer, 0, iCount);
            }

            stream.Close();

            string[] sTracks = sData.Split('\n');
            foreach (string sTrack in sTracks)
            {
                string[] sPieces = sTrack.Split('|');
                if (sPieces.Length < 2) continue;

                Track track = new Track(sPieces[1], sPieces[0], DateTime.Parse(sPieces[2]));
                s_trackHistory.Add(track);
            }
        }

        private static void SaveHistory()
        {
            string sData = string.Empty;

            foreach (Track track in s_trackHistory)
            {
                sData += track.Artist + "|" + track.Title + "|" + track.StartTime.ToString() + "\n";
            }

            string sHistoryFile = GetHistoryFilePath();

            FileInfo fi = new FileInfo(sHistoryFile);

            FileStream stream = fi.OpenWrite();

            byte[] data = Encoding.ASCII.GetBytes(sData);
            stream.Write(data, 0, data.Length);

            stream.Close();
        }

        private static Track[] FetchZoneTracks()
        {
            WebClient wc = new WebClient();

            string sFullSource = string.Empty;

            StreamReader reader = null;
            try
            {
                reader = new StreamReader(wc.OpenRead("http://www.thezone.fm/playlist/"));

                sFullSource = reader.ReadToEnd();
            }
            catch
            {
                return new Track[0];
            }

            Regex trackRegex = new Regex("bitty\">(?<start>.*)</span> :: (?<artist>.*) - (?<title>.*) <span");
            MatchCollection trackMatches = trackRegex.Matches(sFullSource);

            List<Track> tracks = new List<Track>();

            int i = 0;
            foreach (Match match in trackMatches)
            {
                if (i++ > 5) break;

                string sTitle = match.Groups["title"].Success ? match.Groups["title"].Value : string.Empty;
                string sArtist = match.Groups["artist"].Success ? match.Groups["artist"].Value : string.Empty;
                DateTime dtStart;

                if (!DateTime.TryParseExact(match.Groups["start"].Value, "MMM dd, yyyy - h:mm tt", System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out dtStart))
                {
                    dtStart = DateTime.Now;
                }

                Track track = new Track(sTitle, sArtist, dtStart);

                s_trackHistory.Sort();

                if (s_trackHistory.Contains(track) && s_trackHistory.IndexOf(track) > s_trackHistory.Count - 50 && s_trackHistory.IndexOf(track) > -1) continue;

                tracks.Add(track);
                try
                {
                    s_trackHistory.Remove(track);
                }
                catch { }
                s_trackHistory.Add(track);

                System.Console.WriteLine(string.Format("{2}: Found track: {0} - {1}", track.Artist, track.Title, track.StartTime));
            }

            s_dtLastRun = DateTime.Now;

            return tracks.ToArray();
        }

        private static bool ScrobbleTracks(Track[] tracks)
        {
            if (tracks.Length == 0) return true;

            // Submissions (max 50)
            string sSubmissionQuery = "s=" + s_sSessionId;
            for (int i = 0; i < tracks.Length && i < 50; i++)
            {
                sSubmissionQuery += string.Format("&a[{0}]={1}", i, HttpUtility.UrlEncode(tracks[i].Artist));
                sSubmissionQuery += string.Format("&t[{0}]={1}", i, HttpUtility.UrlEncode(tracks[i].Title));
                string sStartTime = DateTimeToUnixTimestamp(tracks[i].StartTime);
                sSubmissionQuery += string.Format("&i[{0}]={1}", i, sStartTime);
                sSubmissionQuery += string.Format("&o[{0}]={1}", i, "R");
                sSubmissionQuery += string.Format("&r[{0}]={1}", i, string.Empty);
                sSubmissionQuery += string.Format("&l[{0}]={1}", i, string.Empty);
                sSubmissionQuery += string.Format("&b[{0}]={1}", i, string.Empty);
                sSubmissionQuery += string.Format("&n[{0}]={1}", i, string.Empty);
                sSubmissionQuery += string.Format("&m[{0}]={1}", i, string.Empty);
            }

            byte[] dataSubmit = Encoding.ASCII.GetBytes(sSubmissionQuery);

            HttpWebRequest reqSubmit = null;
            bool bSuccess = false;
            for (int i = 0; !bSuccess && i < 5; i++)
            {
                try
                {
                    reqSubmit = (HttpWebRequest)HttpWebRequest.Create(s_sSubmissionUrl);
                    reqSubmit.Method = "POST";
                    reqSubmit.ContentType = "application/x-www-form-urlencoded";
                    reqSubmit.ContentLength = dataSubmit.Length;
                    Stream reqStream = reqSubmit.GetRequestStream();
                    reqStream.Write(dataSubmit, 0, dataSubmit.Length);
                    reqStream.Close();
                    bSuccess = true;
                }
                catch { }
            }

            if (!bSuccess)
            {
                Console.WriteLine("Submission Failed: POST Fail.");
                return false;
            }

            HttpWebResponse resSubmit = (HttpWebResponse)reqSubmit.GetResponse();
            Stream resStream = resSubmit.GetResponseStream();
            byte[] buffer = new byte[1024];
            int iCount = 0;
            string sData = string.Empty;

            while ((iCount = resStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sData += Encoding.ASCII.GetString(buffer, 0, iCount);
            }

            if (sData.Trim() != "OK")
            {
                Console.WriteLine("Scrobble Failed: " + sData.Trim());
                return false;
            }

            return true;
        }

        private static string DateTimeToUnixTimestamp(DateTime dtInput)
        {
            TimeSpan ts = (dtInput.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0));
            long timestamp = (long)ts.TotalSeconds;

            return timestamp.ToString();
        }
    }
}
