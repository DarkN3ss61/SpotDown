using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Newtonsoft.Json.Linq;
using SpotDown_V2.Classes;
using TagLib;
using SpotDown_V2.Properties;
using File = System.IO.File;

namespace SpotDown_V2
{
    /*
    Please give credit to me if you use this or any part of it.
    
    HF: http://www.hackforums.net/member.php?action=profile&uid=1389752
    GitHub: https://github.com/DarkN3ss61
    Website: http://jlynx.net/
    Twitter: https://twitter.com/jLynx_DarkN3ss

    ToDo
    - Fix this mess of code.
    - Does not always start downloading first time. Restart program and try again.
    - Progress bar can be glitchy.
    */

    public partial class MainForm : Form
    {
        private const string ApiKey = "{Enter Your YouTube Key Here}";
        private string Dir = "songs/";
        private string TempDir = "songs/temp/";
        private const int MaxRunning = 10;
        private int Running;
        private int Songs;
        private int YoutubeNum;
        private int YoutubeDownloadedNum;
        private int Mp3ClanNum;
        private int Mp3ClanDownloadedNum;
        private int TotalQuedNum;
        private int TotalFinished;
        private int Current;
        private bool Debug;
        private string FfmpegPath;

        private readonly ListViewData[] DownloadData = new ListViewData[10000];
        private readonly int[] SongsArray = new int[10000];
        private readonly PassArguments[][] SongArray = new PassArguments[10000][];

        private const string Website = @"http://jlynx.net/download/spotdown/SpotDown.exe";
        //private readonly string SpotDownUa = "SpotDown " + Assembly.GetExecutingAssembly().GetName().Version + " " + Environment.OSVersion;

        private readonly YouTubeDownloader YouTubeDownloader = new YouTubeDownloader();

        public MainForm()
        {
            CheckForIllegalCrossThreadCalls = false;
            InitializeComponent();

            CheckUpdate();
            Size = new Size(597, 448);
            SongListView.SmallImageList = imageList1;

            SetupFfmpeg();
            SetupDir();

            Log("Started");
        }

        private void SetupDir()
        {
            downloadDirTextBox.Text = Settings.Default.SaveDir.Length < 1
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : Settings.Default.SaveDir;
            Dir = downloadDirTextBox.Text + "/";
            TempDir = Dir + "temp/";
            if (Directory.Exists(TempDir)) return;
            DirectoryInfo Di = Directory.CreateDirectory(TempDir);
            Di.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
        }

        private void SetupFfmpeg()
        {
            try
            {
                FfmpegPath = Path.Combine(Path.GetTempPath(), "ffmpeg.exe");
                File.WriteAllBytes(FfmpegPath, Resources.ffmpeg);
            }
            catch (Exception)
            {

            }
        }

        public void Log(string text, bool DebugLog = false)
        {
            const string logFormat = "[{0}] >> {1}\n";

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Log text can not be empty!");

            var LogText = text;
            if (DebugLog && Debug)
            {
                textBox1.AppendText(string.Format(logFormat, DateTime.Now.ToLongTimeString(), LogText));
            }
            else if (DebugLog == false)
            {
                textBox1.AppendText(string.Format(logFormat, DateTime.Now.ToLongTimeString(), LogText));
            }
        }

        private void CheckUpdate()
        {
            WebClient Wc = new WebClient();
            int Latest = 0;
            try
            {
                Latest = Convert.ToInt32(Wc.DownloadString("http://jlynx.net/download/spotdown/version.txt"));
            }
            catch (WebException)
            {
                Log("Could not reach update server.");
            }

            int CurrentVersion = Convert.ToInt32(Application.ProductVersion.Replace(".", ""));
            labelVersion.Text = @"Version: " + Assembly.GetExecutingAssembly().GetName().Version;
            if (CurrentVersion >= Latest) return;
            if (
                MessageBox.Show(@"There is a newer version of SpotDown available. Would you like to upgrade?",
                    @"SpotDown", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Process.Start(Website);
            Application.Exit();
        }

        private void MainForm_FormClosing(object Sender, FormClosingEventArgs E)
        {
            DialogResult DialogResult =
                MessageBox.Show(@"Are you sure you want to quit? Some songs are sill being downloaded.",
                    @"Are you sure?", MessageBoxButtons.YesNo);
            if (Running != 0)
            {
                switch (DialogResult)
                {
                    case DialogResult.Yes:
                        Process.GetCurrentProcess().Kill();
                        break;
                    case DialogResult.No:
                        E.Cancel = true;
                        break;
                }
            }
            else
            {
                if (Directory.Exists(TempDir))
                {
                    Directory.Delete(TempDir, true);
                }
            }
            try
            {
                File.Delete(FfmpegPath);
            }
            catch (Exception)
            {
            }
        }

        private void DownloadDirTextBox_TextChanged(object Sender, EventArgs E)
        {
            Dir = downloadDirTextBox.Text + "/";
            TempDir = Dir + "temp/";
            if (Directory.Exists(TempDir)) return;
            DirectoryInfo Di = Directory.CreateDirectory(TempDir);
            Di.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
        }

        private void SongListView_ItemSelectionChanged(object Sender, ListViewItemSelectionChangedEventArgs E)
        {
            if (E.IsSelected)
                E.Item.Selected = false;
        }

        private void SongListView_DragEnter(object Sender, DragEventArgs E)
        {
            if (E.Data.GetDataPresent(DataFormats.StringFormat))
            {
                E.Effect = DragDropEffects.Copy;
            }
        }

        private void SongListView_DragDrop(object Sender, DragEventArgs E)
        {
            try
            {
                if (!E.Data.GetDataPresent(DataFormats.StringFormat)) return;
                Current++;
                SongArray[Current] = new PassArguments[10000];
                string Data = (string) E.Data.GetData(DataFormats.StringFormat);
                Data = Data.Replace("http://open.spotify.com/track/", "");
                string[] StrArrays = Data.Split('\n');
                Songs = Songs + StrArrays.Length;
                SongsArray[Current] = StrArrays.Length;
                Log("Loading " + StrArrays.Length + " songs...");
                foreach (string Str in StrArrays)
                {
                    if (Str.Length <= 1) continue;
                    BackgroundWorker BackgroundWorkerStart = new BackgroundWorker();
                    BackgroundWorkerStart.DoWork += BackgroundWorkerStart_DoWork;
                    BackgroundWorkerStart.RunWorkerAsync(new PassArguments
                    {
                        PassedSpotCode = Str,
                        PassedSession = Current
                    });
                }
            }
            catch (Exception Ex)
            {
                MessageBox.Show(this, Ex.Message, Ex.Source, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DownloadDirOpenButton_Click(object Sender, EventArgs E)
        {
            Process.Start(downloadDirTextBox.Text);
        }

        private void DownloadDirBrowseButton_Click(object Sender, EventArgs E)
        {
            using (var Fbd = new FolderBrowserDialog())
            {
                if (Fbd.ShowDialog() != DialogResult.OK) return;
                var Directory = Fbd.SelectedPath;

                downloadDirTextBox.Text = Directory;
                Settings.Default["SaveDir"] = downloadDirTextBox.Text;
                Settings.Default.Save();
            }
        }

        private void BackgroundWorkerStart_DoWork(object Sender, DoWorkEventArgs E)
        {
            PassArguments Result = (PassArguments) E.Argument;
            E.Result = Result;
            newProgressBar1.CustomText = "Loading...";
            SearchSpotify(Result.PassedSpotCode, Result.PassedSession);
        }

        private void SearchSpotify(string Code, int Session)
        {
            int Num = 0;
            PassArguments SpotifyName = GetSpotifyName(Code);
            bool Add = true;

            foreach (var SongThing in from SongArrayInArray in SongArray
                where SongArrayInArray != null
                from SongThing in SongArrayInArray
                where SongThing != null
                where SongThing.PassedFileName.Equals(SpotifyName.PassedSong + " - " + SpotifyName.PassedArtist)
                select SongThing)
            {
                //File already in list
                Songs--;
                SongsArray[Current]--;
                Add = false;
                Log("[Attention] The song " + SpotifyName.PassedSong + " - " + SpotifyName.PassedArtist +
                    " was already added.");
            }
            if (File.Exists(Dir + EscapeFilename(SpotifyName.PassedFileName) + ".mp3"))
            {
                //File already exsists/Downloaded
                Songs--;
                SongsArray[Current]--;
                Add = false;
            }

            try
            {
                if (Add)
                {
                    {
                        SongListView.BeginUpdate();
                        string[] Row = {"Waiting", SpotifyName.PassedSong + " - " + SpotifyName.PassedArtist};
                        var ListViewItem = new ListViewItem(Row) {ImageIndex = 1};
                        SongListView.Items.Add(ListViewItem);
                        SetLabelVisible(false);
                        Num = ListViewItem.Index;
                        SongListView.EndUpdate();

                        SongArray[Session][Num] = (new PassArguments
                        {
                            PassedSong = SpotifyName.PassedSong,
                            PassedArtist = SpotifyName.PassedArtist,
                            PassedNum = Num,
                            PassedFileName = SpotifyName.PassedSong + " - " + SpotifyName.PassedArtist,
                            PassedAlbum = SpotifyName.PassedAlbum,
                            PassedAlbumId = SpotifyName.PassedAlbumId,
                            PassedLength = SpotifyName.PassedLength,
                            PassedLengthMs = SpotifyName.PassedLengthMs,
                            PassedimageURL = SpotifyName.PassedimageURL
                        });
                    }
                }

//                if (SongListView.Items.Count == songs)
                int Result = SongArray[Session].Count(S => S != null);
//                Log(result + " | " + songsArray[current]);
                if (Result == SongsArray[Current])
                {
                    Log(SongsArray[Current] + " songs added. Total songs: " + Songs);
                    SearchSongArray(Session);
                }
            }
            catch (Exception Ex)
            {
                Log("[Error: x1] " + Ex.Message + Environment.NewLine + Num, true);
            }
        }

        public void SearchSongArray(int Session)
        {
            foreach (PassArguments SongInfo in SongArray[Session])
            {
                if (SongInfo != null)
                {
                    try
                    {
                        DownloadMp3Clan(SongInfo.PassedNum, Session);
                    }
                    catch (Exception Ex)
                    {
                        Log("[Error: x2] " + Ex.Message + " " + SongInfo.PassedNum + " | " + SongInfo.PassedFileName,
                            true);
                    }
                }
            }
            Log("All song platforms found.");
        }

        public void DownloadMp3Clan(int Num, int Session)
        {
            PassArguments Result = SongArray[Session][Num];

            EditList("Loading...", Result.PassedFileName, Result.PassedNum, 0);

            int HighestBitrateNum = 0;
            Mp3ClanTrack HighestBitrateTrack = null;

            const string searchBaseUrl = "http://mp3clan.com/mp3_source.php?q={0}";
            var SearchUrl = new Uri(string.Format(searchBaseUrl, Uri.EscapeDataString(Result.PassedSong)));
            string PageSource;

            while (true)
            {
                try
                {
                    PageSource = new MyWebClient().DownloadString(SearchUrl);
                    break;
                }
                catch (WebException E)
                {
                    if (E.Status == WebExceptionStatus.ProtocolError)
                    {
                        EditList("Queued", Result.PassedFileName, Result.PassedNum, 1);
                            //In Que so when its less than 5 it will start 
                        YoutubeNum++;
                        TotalQuedNum++;
                        var Th = new Thread(Unused => Search(Num, Session));
                        Th.Start();
                        return;
                    }
                    Log("[Error: x3] " + E.Message + " | " + Result.PassedFileName, true);
                }
            }


            IEnumerable<Mp3ClanTrack> TrackResult;
            if (Mp3ClanTrack.TryParseFromSource(PageSource, out TrackResult))
            {
                var Tracks = TrackResult.ToList();
                foreach (var Track in Tracks)
                {
                    if (!Track.Artist.ToLower().Trim().Contains(Result.PassedArtist.ToLower()) ||
                        !Track.Name.ToLower().Trim().Equals(Result.PassedSong.ToLower())) continue;
                    string BitrateString;
                    int Attempts = 0;
                    while (true)
                    {
                        try
                        {
                            BitrateString =
                                new MyWebClient().DownloadString("http://mp3clan.com/bitrate.php?tid=" +
                                                                 Track.Mp3ClanUrl.Replace(
                                                                     "http://mp3clan.com/app/get.php?mp3=", ""));
                            break;
                        }
                        catch (Exception Ex)
                        {
                            Attempts++;
                            if (Attempts <= 2) continue;
                            Log("[Infomation: x4] " + Result.PassedFileName + " " + Ex.Message);
                            BitrateString = "0 kbps";
                            break;
                        }
                    }

                    int Bitrate = Int32.Parse(GetKbps(BitrateString));
                    if (Bitrate < 192) continue;
                    if (Bitrate <= HighestBitrateNum) continue;
                    double Persentage = (GetLength(BitrateString)/Result.PassedLength)*100;
//                                double durationMS = TimeSpan.FromMinutes(getLength(bitrateString)).TotalMilliseconds;
//                                double persentage = (durationMS/result.passedLengthMS)*100;
                    if (!(Persentage >= 85) || !(Persentage <= 115)) continue;
//                                    Log("Length acc: " + string.Format("{0:0.00}", persentage) + "%");
                    HighestBitrateNum = Bitrate;
                    HighestBitrateTrack = Track;
                }
            }
            //=======For testing================
//            EditList("Queued", result.passedFileName, result.passedNum, 1);
//            youtubeNum++;
//            totalQuedNum++;
//            var th = new Thread(unused => Search(num));
//            th.Start();
            //==================================

            if (HighestBitrateTrack == null)
            {
//Youtube
                EditList("Queued", Result.PassedFileName, Result.PassedNum, 1);
                YoutubeNum++;
                TotalQuedNum++;
                var Th = new Thread(Unused => Search(Num, Session));
                Th.Start();
            }
            else
            {
//MP3Clan
                SongArray[Session][Num].PassedTrack = HighestBitrateTrack;
                EditList("Queued", Result.PassedFileName, Result.PassedNum, 1);
                Mp3ClanNum++;
                TotalQuedNum++;

                var Th = new Thread(Unused => StartDownloadMp3Clan(Num, Session));
                Th.Start();
            }
        }

        public async void Search(int Num, int Session, int Retry = 0, string VideoId = null)
        {
            PassArguments Result = SongArray[Session][Num];

            while (Running >= MaxRunning)
            {
                Thread.Sleep(500);
            }
            TotalQuedNum--;
            Running++;
            EditList("Loading...", Result.PassedFileName, Result.PassedNum, 0);

            string Url = String.Empty;
            var YoutubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = ApiKey,
                ApplicationName = GetType().ToString()
            });

            var SearchListRequest = YoutubeService.Search.List("snippet");

            switch (Retry)
            {
                case 0:
                    SearchListRequest.Q = Result.PassedFileName;
                    break;
                case 1:
                {
                    string NewName;
                    try
                    {
                        NewName =
                            Result.PassedSong.Substring(0, Result.PassedSong.IndexOf("-", StringComparison.Ordinal)) +
                            " - " + Result.PassedArtist;
                    }
                    catch (Exception)
                    {
                        NewName = Result.PassedFileName;
                    }
                    SearchListRequest.Q = NewName;
                }
                    break;
                case 2:
                {
                    string NewName;
                    try
                    {
                        NewName =
                            Result.PassedSong.Substring(0, Result.PassedSong.IndexOf("(", StringComparison.Ordinal)) +
                            " - " + Result.PassedArtist;
                    }
                    catch (Exception)
                    {
                        NewName = Result.PassedFileName;
                    }
                    SearchListRequest.Q = NewName;
                }
                    break;
                case 3:
                {
                    string NewName;
                    try
                    {
                        NewName =
                            Result.PassedSong.Substring(0, Result.PassedSong.IndexOf("/", StringComparison.Ordinal)) +
                            " - " + Result.PassedArtist;
                    }
                    catch (Exception)
                    {
                        NewName = Result.PassedFileName;
                    }
                    SearchListRequest.Q = NewName;
                }
                    break;
                case 4:
                    SearchListRequest.Q = Result.PassedFileName;
                    break;
            }
            SearchListRequest.MaxResults = 5;
            SearchListRequest.Order = SearchResource.ListRequest.OrderEnum.Relevance;

            var SearchListResponse = await SearchListRequest.ExecuteAsync();
            List<string> Videos = new List<string>();

            string[] ExcludeStrings = {"live", "cover"};
            string[] IncludeStrings = {"hd", "official"};
            string[] IncludeChannelStrings = {"vevo"};
            double Highpersentage = 99999999.0;

            for (int I = 0; I < ExcludeStrings.Length; I++)
            {
                if (Result.PassedFileName.ToLower().Contains(ExcludeStrings[I]))
                {
                    ExcludeStrings = ExcludeStrings.Where(W => W != ExcludeStrings[I]).ToArray();
                }
            }

            foreach (var Word in ExcludeStrings.Where(Word => (Result.PassedFileName).ToLower().Contains(Word)))
            {
                ExcludeStrings = ExcludeStrings.Where(Str => Str != Word).ToArray();
            }

            bool Keepgoing = true;
            foreach (var SearchResult in SearchListResponse.Items)
            {
                if (!Keepgoing) continue;
                if (SearchResult.Id.VideoId == null ||
                    "https://www.youtube.com/watch?v=" + SearchResult.Id.VideoId == VideoId) continue;

                if (Retry == 4)
                {
                    Log("[Infomation x13] Downloaded song may be incorrect for " + Result.PassedFileName);
                    Videos.Add(String.Format("{0} ({1})", SearchResult.Snippet.Title, SearchResult.Id.VideoId));
                    Url = "https://www.youtube.com/watch?v=" + SearchResult.Id.VideoId;
                    break;
                }
                if (ExcludeStrings.Any(SearchResult.Snippet.Title.ToLower().Contains) ||
                    ExcludeStrings.Any(SearchResult.Snippet.Description.ToLower().Contains))
                {
//                        MessageBox.Show("ERROR IT CONTAINS BAD STUFF");
                }
                else
                {
                    var SearchListRequest2 = YoutubeService.Videos.List("contentDetails");
                    SearchListRequest2.Id = SearchResult.Id.VideoId;
                    var SearchListResponse2 = await SearchListRequest2.ExecuteAsync();

                    foreach (var SearchResult2 in SearchListResponse2.Items)
                    {
                        string DurationTimeSpan = SearchResult2.ContentDetails.Duration;
                        TimeSpan YouTubeDuration = XmlConvert.ToTimeSpan(DurationTimeSpan);
                        double DurationMs = (YouTubeDuration).TotalMilliseconds;
                        double Persentage = (DurationMs/Result.PassedLengthMs)*100;

                        if (!(Persentage >= 90) || !(Persentage <= 110)) continue;
                        double Number = Math.Abs(DurationMs - Result.PassedLengthMs);
                        if (Number < Highpersentage)
                        {
//                                        Log(string.Format("{0:0.00}", persentage) + "% from the original and number is " + number + " | " + searchResult.Id.VideoId);
                            Videos.Add(String.Format("{0} ({1})", SearchResult.Snippet.Title, SearchResult.Id.VideoId));
                            Url = "https://www.youtube.com/watch?v=" + SearchResult.Id.VideoId;
                            Highpersentage = Number;
                        }

                        if (IncludeChannelStrings.Any(SearchResult.Snippet.ChannelTitle.ToLower().Contains) ||
                            SearchResult.Snippet.ChannelTitle.ToLower()
                                .Contains(Result.PassedArtist.Replace(" ", "").ToLower()))
                        {
//                                        Log("using Official | " + searchResult.Id.VideoId);
                            Videos.Add(String.Format("{0} ({1})", SearchResult.Snippet.Title, SearchResult.Id.VideoId));
                            Url = "https://www.youtube.com/watch?v=" + SearchResult.Id.VideoId;
                            Keepgoing = false;
                            break;
                        }

                        if (!IncludeStrings.Any(SearchResult.Snippet.Description.ToLower().Contains) &&
                            !IncludeStrings.Any(SearchResult.Snippet.Title.ToLower().Contains)) continue;
//                                        Log("using Original " + string.Format("{0:0.00}", persentage) + "% from the original| " + searchResult.Id.VideoId);
                        Videos.Add(String.Format("{0} ({1})", SearchResult.Snippet.Title, SearchResult.Id.VideoId));
                        Url = "https://www.youtube.com/watch?v=" + SearchResult.Id.VideoId;
                        Keepgoing = false;
                        break;
                    }
                }
            }

            if (Url != String.Empty)
            {
                SongArray[Session][Num].PassedURL = Url;
            }
            else
            {
                switch (Retry)
                {
                    case 0:
                        Running--;
                        TotalQuedNum++;
                        if (Running < 0)
                        {
                            Running = 0;
                        }
                        Search(Num, Session, 1);
                        return;
                    case 1:
                        Running--;
                        TotalQuedNum++;
                        if (Running < 0)
                        {
                            Running = 0;
                        }
                        Search(Num, Session, 2);
                        return;
                    case 2:
                        Running--;
                        TotalQuedNum++;
                        if (Running < 0)
                        {
                            Running = 0;
                        }
                        Search(Num, Session, 3);
                        return;
                    case 3:
                        Running--;
                        TotalQuedNum++;
                        if (Running < 0)
                        {
                            Running = 0;
                        }
                        Search(Num, Session, 4);
                        return;
                    case 4:
                        Done(Result.PassedFileName, Result.PassedNum, "NotFound", 5); //Youtube not found
                        Log("[Error x9] Video not found for: " + Result.PassedFileName, true);
                        Running--;
                        if (Running < 0)
                        {
                            Running = 0;
                        }
                        return;
                }
                Log("[Error x10] " + Result.PassedFileName, true);
                Running--;
                TotalQuedNum++;
                if (Running < 0)
                {
                    Running = 0;
                }
                return;
            }

            SongArray[Session][Num].YouTubeVideoQuality = YouTubeDownloader.GetYouTubeVideoUrls(Result.PassedURL);
            if (SongArray[Session][Num].YouTubeVideoQuality == null)
            {
//                Log("Cant download " + result.passedFileName + " because of age restriction on video");
                Running--;
                if (Running < 0)
                {
                    Running = 0;
                }
                TotalQuedNum++;
                Search(Num, Session, 0, Url);
                return;
            }
            YouTubeDownload(Num, Session);
        }

        private void YouTubeDownload(int Num, int Session)
        {
            PassArguments Result = SongArray[Session][Num];
//            while (true)
//            {
            try
            {
                List<YouTubeVideoQuality> Urls = Result.YouTubeVideoQuality;
                YouTubeVideoQuality HighestQual = new YouTubeVideoQuality();

                if (Urls.Any(url => url.Extention == "mp4"))
                {
                    HighestQual = Urls[0];
                }
                string Url = String.Empty;
                string SaveTo = String.Empty;
                try
                {
                    YouTubeVideoQuality TempItem = HighestQual;
                    Url = TempItem.DownloadUrl;
                    SaveTo = EscapeFilename(Result.PassedFileName) + ".mp4";
                }
                catch (Exception Ex)
                {
                    Log("[Error x11] " + Ex.InnerException, true);
                }
                if (Result.PassedFileName == null)
                {
                    MessageBox.Show(@"Somthing null");
                }
                EditList("Downloading...", Result.PassedFileName, Result.PassedNum, 2);
                var Folder = Path.GetDirectoryName(TempDir + SaveTo);
                string file = Path.GetFileName(TempDir + SaveTo);

                var Client = new WebClient();
                Uri Address = new Uri(Url);
                Client.DownloadFile(Address, Folder + "\\" + file);
                EditList("Converting...", Result.PassedFileName, Result.PassedNum, 3);
                StartConvert(Result.PassedFileName);
                MusicTags(Num, Session);
                YoutubeDownloadedNum++;
                Done(Result.PassedFileName, Num);
                Running--;
                if (Running < 0)
                {
                    Running = 0;
                }
//                    break;
            }
            catch (Exception Ex)
            {
                Log("[Error x12] " + "|" + Ex.Message + "| " + Ex.InnerException + " | " + Result.PassedFileName, true);
            }
//            }
        }


        private void StartConvert(string SongName)
        {
//            string output = String.Empty;
            Process Process = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = FfmpegPath,
                    Arguments =
                        " -i \"" + TempDir + EscapeFilename(SongName) + ".mp4\" -vn -f mp3 -ab 320k \"" + Dir +
                        EscapeFilename(SongName) + ".mp3\""
                }
            };
            //            _process.StartInfo.RedirectStandardInput = true;
//            _process.StartInfo.RedirectStandardOutput = true;
//            _process.StartInfo.RedirectStandardError = true;
            //            _process.StartInfo.FileName = "ffmpeg";
            //            _process.StartInfo.Arguments = " -i \"" + SongName + ".mp4\" -vn -f mp3 -ab 192k \"" + SongName + ".mp3\"";
            Process.Start();
//            _process.StandardOutput.ReadToEnd();
//            output = _process.StandardError.ReadToEnd();
            Process.WaitForExit();
            if (File.Exists(TempDir + EscapeFilename(SongName) + ".mp4"))
            {
                File.Delete(TempDir + EscapeFilename(SongName) + ".mp4");
            }
        }

        private void StartDownloadMp3Clan(int Num, int Session)
        {
            PassArguments Result = SongArray[Session][Num];

            while (Running >= MaxRunning)
            {
                Thread.Sleep(500);
            }
            TotalQuedNum--;
            Running++;
            Mp3ClanDownloadedNum++;

            EditList("Downloading...", Result.PassedFileName, Result.PassedNum, 2);

            int ErrorTimesX6 = 0;
            while (true)
            {
                try
                {
                    var DownloadUrl = new Uri(Result.PassedTrack.Mp3ClanUrl);
                    var FileName = Dir + "\\" + EscapeFilename(Result.PassedFileName) + ".mp3";

                    int ErrorTimes = 0;
                    while (true)
                    {
                        using (var Client = new WebClient())
                        {
                            Client.DownloadFile(DownloadUrl, FileName);
                            Client.Dispose();
                        }

                        long FileSize = new FileInfo(FileName).Length;
                        if (FileSize < 1000)
                            //Possible improvement. get file size from online fore download and check that its a 5% acc to approve the download
                        {
                            ErrorTimes++;
                            if (ErrorTimes >= 3)
                            {
                                Log("[Infomation: x5] " + Result.PassedFileName + " failed, re-downloading");
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    break;
                }
                catch (Exception Ex)
                {
                    ErrorTimesX6++;
                    if (ErrorTimesX6 < 3) continue;
                    Log("[Infomation: x6] " + Ex.Message + " | " + Ex.InnerException + " | " + Result.PassedFileName);
                    ErrorTimesX6 = 0;
                    Thread.Sleep(500);
                }
            }
            MusicTags(Num, Session);
            Done(Result.PassedFileName, Result.PassedNum);
            Running--;
            if (Running < 0)
            {
                Running = 0;
            }
        }

        public void MusicTags(int Num, int Session)
        {
            PassArguments Result = SongArray[Session][Num];
            try
            {
                //===edit tags====
                TagLib.File F = TagLib.File.Create(Dir + EscapeFilename(Result.PassedFileName) + ".mp3");
                F.Tag.Clear();
                F.Tag.AlbumArtists = new[] {Result.PassedArtist};
                F.Tag.Performers = new[] {Result.PassedArtist};
                F.Tag.Title = Result.PassedSong;
                F.Tag.Album = Result.PassedAlbum;
//                //                Log(result.passedFileName + " and " + result.passedAlbumID);
                Image CurrentImage = GetAlbumArt(Num, Session);
                Picture Pic = new Picture
                {
                    Type = PictureType.FrontCover,
                    MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg,
                    Description = "Cover"
                };
                MemoryStream Ms = new MemoryStream();
                CurrentImage.Save(Ms, System.Drawing.Imaging.ImageFormat.Jpeg); // <-- Error doesn't occur anymore
                Ms.Position = 0;
                Pic.Data = ByteVector.FromStream(Ms);
                F.Tag.Pictures = new IPicture[] {Pic};
                F.Save();
                Ms.Close();
            }
            catch (Exception Ex)
            {
                Log("[Error: x7] " + Ex.Message + Environment.NewLine + Environment.NewLine + Result.PassedFileName,
                    true);
            }
        }

        private Bitmap GetAlbumArt(int Num, int Session)
        {
            PassArguments Result = SongArray[Session][Num];
            Bitmap Bitmap2 = null;
            try
            {
                WebRequest Request = WebRequest.Create(Result.PassedimageURL);
                WebResponse Response = Request.GetResponse();
                Stream ResponseStream = Response.GetResponseStream();
                if (ResponseStream != null) Bitmap2 = new Bitmap(ResponseStream);
            }
            catch (Exception Ex)
            {
                Log("[Error: x8] " + Ex.Message, true);
                Bitmap2 = null;
            }

            return Bitmap2;
        }

        public string GetKbps(string S)
        {
            int L = S.IndexOf(" kbps", StringComparison.Ordinal);
            return L > 0 ? S.Substring(0, L) : "";
        }

        public double GetLength(string S)
        {
            int PFrom = S.IndexOf("<br>", StringComparison.Ordinal) + "<br>".Length;
            int PTo = S.IndexOf(" min", StringComparison.Ordinal);

            string Result = S.Substring(PFrom, PTo - PFrom).Replace(":", ".");
            double SongLength = double.Parse(Result, CultureInfo.InvariantCulture);
            return SongLength;
        }

        private void Done(string PassedFileName, int PassedNum, string Message = "Done!", int Num = 4)
        {
            EditList(Message, PassedFileName, PassedNum, Num);
            TotalFinished++;

            newProgressBar1.Maximum = Songs;
            newProgressBar1.Value = TotalFinished;
            newProgressBar1.CustomText = newProgressBar1.Value + "/" + newProgressBar1.Maximum;
//            double percent = ((TotalFinished - 1)/songs)*100;
//            harrProgressBar1.FillDegree = (int)percent;
            if (newProgressBar1.Value == newProgressBar1.Maximum)
            {
                newProgressBar1.CustomText = "Done!";
            }
        }

        public double MillisecondsTimeSpanToHms(double S)
        {
            S = TimeSpan.FromMilliseconds(S).TotalSeconds;
            var H = Math.Floor(S/3600); //Get whole hours
            S -= H*3600;
            var M = Math.Floor(S/60); //Get remaining minutes
            S -= M*60;
            S = Math.Round(S);
            string StringLength = (M + "." + S);
            return double.Parse(StringLength, CultureInfo.InvariantCulture);
        }

        private delegate void SetLabelVisibleDelegate(bool status);

        private void SetLabelVisible(bool status)
        {
            if (label3.InvokeRequired)
                label3.Invoke(new SetLabelVisibleDelegate(SetLabelVisible), status);
            else
                label3.Visible = status;
        }

        public PassArguments GetSpotifyName(string Query) //Uptaded to use new API
        {
            string GetData;
            while (true)
            {
                try
                {
                    WebClient C = new WebClient();
                    GetData = C.DownloadString("https://api.spotify.com/v1/tracks/" + Query);
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }

            JObject O = JObject.Parse(GetData);
            string Title = O["name"].ToString();
            string Artist = O["artists"][0]["name"].ToString();
            string AlbumId = O["artists"][0]["id"].ToString();
            string Album = O["album"]["name"].ToString();
            string ImageUrl = O["album"]["images"][0]["url"].ToString();
            double LengthMs = double.Parse(O["duration_ms"].ToString());
            double Length = MillisecondsTimeSpanToHms(double.Parse(O["duration_ms"].ToString()));

            Title = Encoding.UTF8.GetString(Encoding.Default.GetBytes(Title));
            Artist = Encoding.UTF8.GetString(Encoding.Default.GetBytes(Artist));
            Album = Encoding.UTF8.GetString(Encoding.Default.GetBytes(Album));

            PassArguments Pass = new PassArguments
            {
                PassedSong = Title,
                PassedArtist = Artist,
                PassedAlbum = Album,
                PassedAlbumId = AlbumId,
                PassedFileName = Title + " - " + Artist,
                PassedLength = Length,
                PassedLengthMs = LengthMs,
                PassedimageURL = ImageUrl
            };
            return Pass;
        }

        private static string EscapeFilename(string name)
        {
            return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c.ToString(), "_"));
        }

        public void EditList(string Message, string FileName, int Number, int Image)
        {
            var Data = new ListViewData {Message = Message, FileName = FileName, Number = Number, Image = Image};
            DownloadData[Number] = (Data);
        }

        private void SongListUpdateTimer_Tick(object Sender, EventArgs E)
        {
            SongListView.BeginUpdate();
            foreach (var DownloadInfo in DownloadData.Where(DownloadInfo => DownloadInfo != null))
            {
                string[] Row = {DownloadInfo.Message, DownloadInfo.FileName};
                var ListViewItem = new ListViewItem(Row) {ImageIndex = DownloadInfo.Image};
                if (SongListView.Items[DownloadInfo.Number] == null)
                {
                    SongListView.Items.Add(ListViewItem);
                }
                else
                {
                    SongListView.Items[DownloadInfo.Number] = (ListViewItem);
                }
            }
            SongListView.EndUpdate();
        }

        private bool LogStyle;

        private void Button1_Click(object Sender, EventArgs E)
        {
            if (LogStyle)
            {
                button1.Text = @"Log >";
                Size = new Size(597, 448);
                LogStyle = false;
            }
            else
            {
                button1.Text = @"Log <";
                Size = new Size(1245, 448);
                LogStyle = true;
            }
        }

        private void CheckBox1_CheckedChanged(object Sender, EventArgs E)
        {
            Debug = checkBox1.Checked;
        }

        private void LabelUpdateTimer_Tick(object Sender, EventArgs E)
        {
            label9.Text = Running.ToString();
            label5.Text = YoutubeNum.ToString();
            label4.Text = Mp3ClanNum.ToString();
            label17.Text = TotalQuedNum.ToString();
            label10.Text = Mp3ClanDownloadedNum.ToString();
            label19.Text = TotalFinished.ToString();
            label15.Text = YoutubeDownloadedNum.ToString();
        }
    }
}
