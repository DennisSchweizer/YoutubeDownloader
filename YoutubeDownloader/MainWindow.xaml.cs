using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Collections.Concurrent;
using System.Windows.Threading;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Playlists;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;


namespace YoutubeDownloader
{
#pragma warning disable CA1416 // Plattformkompatibilität überprüfen
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
        }

        #region Properties / Global variables

        CancellationTokenSource cancellationToken = new CancellationTokenSource();
        bool cancelCurrentDownload = false;
        bool cancelAllDownloads = false;

        readonly TaskbarManager taskbar = TaskbarManager.Instance;

        readonly Stopwatch sw = new Stopwatch();

        // If youtube videos cannot be accessed they need to be removed otherwise wrong links for a download may be shown
        readonly List<string> invalidYouTubeLinks = new List<string>();

        // For estimating remaining time with parallel downloads 
        (IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) largestMedium;


        #endregion

        #region Click events
        /// <summary>
        /// Used in order to paste the contents of the clipboard by double clicking on the textbox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDoubleClick(object sender, RoutedEventArgs e)
        {
            VideoList.Foreground = Brushes.Black;
            if (VideoList.Text.Equals("Links zu den Videos hier in je eine Zeile einfügen"))
            {
                VideoList.Text = string.Empty;
            }

            // In Testing
            List<string> videosToBeDownloaded = FilterForYoutubeLinks(System.Windows.Clipboard.GetText());

            foreach (string videoLink in videosToBeDownloaded)
            {
                VideoList.Text = VideoList.Text.Insert(0, $"{videoLink}\n");
            }

        }

        private void BrowseSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            using var fbd = new FolderBrowserDialog();
            DialogResult result = fbd.ShowDialog();

            if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                DownloadDirectory.Text = $"{fbd.SelectedPath}\\";
            }
        }
        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            cancelCurrentDownload = true;
            taskbar.SetProgressState(TaskbarProgressBarState.Paused);
            DownloadProgress.Foreground = Brushes.Yellow;
        }
        private void CancelAllDownloads_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            cancelAllDownloads = true;
            taskbar.SetProgressState(TaskbarProgressBarState.Error);
            taskbar.SetProgressValue(100, 100);

        }

        private void MaxParallelDownloads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaxParallelDownloads.Value == 0)
            {
                MaxParallelDownloads.Value = -1;
            }
        }
        #endregion

        #region Sequential Download

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();
            DownloadList.Content = "Download läuft...";
            CurrentDownload.Visibility = Visibility.Visible;

            List<(IStreamInfo[] vids, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = await GenerateListOfDownloads();
            vidsWithPathsAndLinks = vidsWithPathsAndLinks.OrderBy(video => video.vids[0].Size).ToList();

            // Initialize variables for progress bar
            uint downloadedVideos = 0;
            ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";


            foreach ((IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) video in vidsWithPathsAndLinks)
            {
                sw.Reset();
                CurrentDownload.Text = "Aktueller Download: ";
                CurrentDownload.Text += $"{video.video.Url.ReplaceLineEndings(string.Empty)}";

                try
                {
                    await DownloadYoutubeVideoAsync(video, cancellationToken.Token);

                    
                } 

                catch (OperationCanceledException)
                {
                    await HandleCanceledDownload(video);

                    //Sepcial treatment for single download
                    if (cancelCurrentDownload)
                    {
                        cancelCurrentDownload = false;
                        cancellationToken.TryReset();
                        cancellationToken = new CancellationTokenSource();
                        continue;
                    }
                    else if (cancelAllDownloads)
                    {
                        cancelAllDownloads = false;
                        break;
                    }
                }

                // Refresh progress bar for whole download process
                finally
                {
                    downloadedVideos++;
                    DownloadProgress.Value = downloadedVideos * 100 / (uint)vidsWithPathsAndLinks.Count;
                    ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";
                }

 
                // Remove current download text from label 
                CurrentDownload.Text = CurrentDownload.Text.Replace($"{video.video.Url.ReplaceLineEndings(string.Empty)}", string.Empty);

                // Remove unnecessary data from memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(1000);
            }
            CurrentDownload.Visibility = Visibility.Hidden;
            FinalizeDownloads();
        }

        private async Task DownloadYoutubeVideoAsync((IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) mediaToBeLoaded, CancellationToken cts)
        {   
            try
            {
                CurrentDownload.Text += $"\nDateiname: {mediaToBeLoaded.path.Split('\\').Last()}";
                sw.Start();

                await DownloadVideo(mediaToBeLoaded, cts);

                if (cancelCurrentDownload || cancelAllDownloads)
                {
                    throw new OperationCanceledException();
                }

            }

            catch (ArgumentException)
            {
                System.Windows.MessageBox.Show("Ungültiger Link! Gebe einen Link zu einem Youtube Video ein!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                
            }

            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("403") || ex.Message.Contains("303")){
                    System.Windows.MessageBox.Show("Youtube hat die Verbindung verweigert. Warten Sie kurz, bis die Verbindung wieder möglich ist!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    System.Windows.MessageBox.Show("Die Verbindung zu Youtube konnte nicht hergestellt werden. Überprüfen Sie die Internetverbindung!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // If single download is canceled or successfully finished refresh GUI
            finally
            {
                taskbar.SetProgressValue(0, 100);
                CurrentDownload.Text = CurrentDownload.Text.Replace($"\nDateiname: {mediaToBeLoaded.path.Split('\\').Last()}", string.Empty);
            }
        }

        private async Task DownloadVideo((IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) vid, CancellationToken cts)
        {
            sw.Start();
            var youtube = new YoutubeClient();
            Progress<double> progressHandler = null;
            await Dispatcher.BeginInvoke(() => 
            {
                taskbar.SetProgressState(TaskbarProgressBarState.Normal);
                progressHandler = new Progress<double>(p => RefreshGuiCurrentDownload(p));
            });
           
            if (vid.streams[1] != null)
            { 
                await youtube.Videos.DownloadAsync(vid.streams, new ConversionRequestBuilder(vid.path).Build(), progressHandler, cts); 
            }
            else // Video with separate audio stream
            {
                if ((bool)Audio.IsChecked)
                {
                    await youtube.Videos.DownloadAsync(vid.video.Id, new ConversionRequestBuilder(vid.path).SetContainer(Container.Mp3).SetPreset(ConversionPreset.Medium).Build(), progressHandler, cts);
                }
                else
                {
                    await youtube.Videos.Streams.DownloadAsync(vid.streams[0], vid.path, progressHandler, cts);
                }
                
            }
        }

        #endregion

        #region Parallel Download
        private async void ParallelDownload_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();
            ParallelDownload.Content = "Download läuft...";

            // Single download cannot be cancelled if download parallel is started
            CancelOperation.IsEnabled = false;

            List<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = await GenerateListOfDownloads();

            // no valid Youtube video detected
            if(vidsWithPathsAndLinks.Count == 0)
            {
                FinalizeDownloads();
                return;
            }

            ConcurrentBag<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)> concurrentVids = new ConcurrentBag<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)>(vidsWithPathsAndLinks);

            // used in order to get largest donwload and use it as estimation for whole download time
            largestMedium = concurrentVids.MaxBy(medium => medium.streams[0].Size);

            // Initialize variables for progress bars and time labels
            uint downloadedVideos = 0;
            ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {concurrentVids.Count} Dateien";
            taskbar.SetProgressState(TaskbarProgressBarState.Normal);
            taskbar.SetProgressValue(0, concurrentVids.Count);

            sw.Reset();
            sw.Start();


            // Parallel for each or Task.WhenAll()
            // MaxDegreeOfParallelism = 7 since laptop has 8 cores, leave 1 core alone
            var maxDegreeOfParallelism = new ParallelOptions() { MaxDegreeOfParallelism = (int) MaxParallelDownloads.Value };
            await Parallel.ForEachAsync(concurrentVids, maxDegreeOfParallelism, async (media, _) =>
            {
                await DownloadVideosParallel(media, cancellationToken.Token);

                downloadedVideos++;
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadProgress.Value = downloadedVideos * 100 / (uint)concurrentVids.Count;
                    ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {concurrentVids.Count} Dateien";
                    taskbar.SetProgressValue((int)downloadedVideos, concurrentVids.Count);
                }));
            });


            //List<Task> tasks = new List<Task>();
            //foreach((YouTubeVideo, string, string) medium in concurrentVids)
            //{
            //    tasks.Add(Task.Run(() => DownloadVideosParallel(medium, CancellationToken.None)));
            //}
            //await Task.WhenAll(tasks);

            sw.Stop();
            ParallelDownload.Content = "Paralleler Download";
            FinalizeDownloads();
        }

        private async Task DownloadVideosParallel((IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) media, CancellationToken cts)
        {
            try
            {
                var youtubeClient = new YoutubeClient();

                Progress<double> progressHandler = null;
                if(media == largestMedium)
                {
                    await Dispatcher.BeginInvoke(() =>
                    {
                        taskbar.SetProgressState(TaskbarProgressBarState.Normal);
                        progressHandler = new Progress<double>(p => RefreshGuiCurrentDownload(p));
                    });
                }

                if (media.streams[1] != null)
                {
                    await youtubeClient.Videos.DownloadAsync(media.streams, new ConversionRequestBuilder(media.path).Build(), progressHandler, cts);
                }
                else
                {
                    bool audioIsChecked = true;
                    await Dispatcher.BeginInvoke(() => audioIsChecked = (bool)Audio.IsChecked);
                    if (audioIsChecked)
                    {
                        await youtubeClient.Videos.DownloadAsync(media.video.Id, new ConversionRequestBuilder(media.path).SetContainer(Container.Mp3).SetPreset(ConversionPreset.Medium).Build(), progressHandler, cts);
                    }
                    else
                    {
                        await youtubeClient.Videos.Streams.DownloadAsync(media.streams[0], media.path, progressHandler, cts);
                    }
                }
                
            }
            catch (OperationCanceledException)
            {
                await HandleCanceledDownload(media);
            }
        }

        #endregion

        #region HelperMethods

        private async Task<(IStreamInfo[], YoutubeExplode.Videos.Video)> GetMediaInformation(string mediaToBeLoaded, CancellationToken cts)
        {
            var youtube = new YoutubeClient();
            StreamManifest allStreamInfos;
            YoutubeExplode.Videos.Video videoData = await youtube.Videos.GetAsync(mediaToBeLoaded, cts);

        TryAgain: 
            try
            {
                allStreamInfos = await youtube.Videos.Streams.GetManifestAsync(mediaToBeLoaded, cts);
            }
            catch(HttpRequestException)
            {
                await Task.Delay(3000, CancellationToken.None);
                goto TryAgain;
            }

            IStreamInfo audioManifest;
            IStreamInfo videoManifest;
            IStreamInfo[] streamInfos = new IStreamInfo[2];
            // Decide whether audio or video file is loaded.
            if ((bool)Audio.IsChecked)
            {
                // LINQ necessary because the default audio format is opus which is incompatible with mp3
                audioManifest = allStreamInfos.GetAudioOnlyStreams().Where(format => format.Container == YoutubeExplode.Videos.Streams.Container.WebM || format.Container == Container.Mp4 || format.Container == Container.Mp3).GetWithHighestBitrate();
                streamInfos[0] = audioManifest;
            }

            else
            {
                bool IsPathToFfmpegAvailable = Environment.ExpandEnvironmentVariables("%Path%").Contains("ffmpeg");
                if (!IsPathToFfmpegAvailable)
                {
                    System.Windows.Forms.MessageBox.Show("ffmpeg ist nicht installiert, deshalb wird das Video in Standardqualität geladen. Überprüfen Sie, ob ffmpeg.exe in einem Ordner der Umgebungsvariable Path liegt.", "ffmpeg nicht verfügbar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    audioManifest = allStreamInfos.GetAudioOnlyStreams().Where(format => format.Container == YoutubeExplode.Videos.Streams.Container.Mp4).GetWithHighestBitrate();
                    videoManifest = allStreamInfos.GetVideoOnlyStreams().Where(format => format.Container == YoutubeExplode.Videos.Streams.Container.Mp4).GetWithHighestVideoQuality();
                    streamInfos[0] = audioManifest;
                    streamInfos[1] = videoManifest;
                }
            }

            return (streamInfos, videoData);
        }

        private async Task<List<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)>> GenerateListOfDownloads()
        {
            List<string> videosToBeDownloaded = FilterForYoutubeLinks(VideoList.Text);


            List<(IStreamInfo[], YoutubeExplode.Videos.Video)> allMediaData = await YouTubeVideosToBeLoaded(videosToBeDownloaded);
            List<string> paths = GenerateFullFileNameList(allMediaData);


            videosToBeDownloaded= videosToBeDownloaded.Where(link => !invalidYouTubeLinks.Contains(link)).ToList();


            List<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = new List<(IStreamInfo[], YoutubeExplode.Videos.Video, string)>();
            
            for(int i = 0; i < allMediaData.Count; i++) { 
            
                (IStreamInfo[], YoutubeExplode.Videos.Video, string) tripel = (allMediaData[i].Item1, allMediaData[i].Item2, paths[i]);
                vidsWithPathsAndLinks.Add(tripel);
            }

            List<int> remainingElements = CheckForAlreadyLoadedFile(vidsWithPathsAndLinks);
            return vidsWithPathsAndLinks = remainingElements.Select(index => vidsWithPathsAndLinks[index]).ToList();
        }


        private async Task HandleCanceledDownload((IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path) video)
        {
            // Remove unnecessary data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            cancellationToken = new CancellationTokenSource();

            taskbar.SetProgressState(TaskbarProgressBarState.Paused);
            taskbar.SetProgressValue(100, 100);
            System.Windows.MessageBox.Show($"Der Download der Datei {video.path.Split('\\').Last()} wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation,MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);

            await Dispatcher.BeginInvoke(() => {
                DownloadProgress.Foreground = Brushes.Yellow;
                CurrentDownload.Text = "Aktueller Download: ";
            });

            // Download was started and file did not exist before current download session
            if (File.Exists(video.path) && (cancelAllDownloads || cancelCurrentDownload))
            {
                await Task.Delay(1000); // necessary for deleting an existing file otherwise an exception will be thrown
                try
                {
                    File.Delete(video.path);
                }
                catch (IOException) { /*For now exception will be caught since DownloadAsync is not cancelled right now*/ }
            }
        }

        private string GenerateFullFileName(YoutubeExplode.Videos.Video video)
        {
            // Remove invalid characters from Youtube video title -> filename
            string videoTitle = string.Join('_', video.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            return (bool)Audio.IsChecked ? videoTitle  + ".mp3" : videoTitle + ".mp4";
        }


        private List<string> GenerateFullFileNameList(List<(IStreamInfo[], YoutubeExplode.Videos.Video)> youTubeVideos)
        {
            List<string> fullFilePaths = new List<string>();
            foreach (var video in youTubeVideos)
            {
                fullFilePaths.Add(DownloadDirectory.Text + GenerateFullFileName(video.Item2));
            }
            return fullFilePaths;
        }

        private List<int> CheckForAlreadyLoadedFile(List<(IStreamInfo[] streams, YoutubeExplode.Videos.Video video, string path)> videosWithPaths)
        {
            List<int> indicesToBeDownloaded = new List<int>();
            for (int i = 0; i < videosWithPaths.Count; i++)
            {
                // Check if file name already exists in directory before downloading
                if (File.Exists(videosWithPaths[i].path ))
                {
                    taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
                    DialogResult overwriteAlreadyDownloadedFile = System.Windows.Forms.MessageBox.Show($"Die Datei \n{videosWithPaths[i].path} \nexistiert bereits. Soll der Download übersprungen werden?", "Datei existiert bereits!", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly);

                    if (overwriteAlreadyDownloadedFile == System.Windows.Forms.DialogResult.No)
                    {
                        indicesToBeDownloaded.Add(i);

                        //Backup copy if file is unintentionally overwritten
                        CreateBackUpForDownloadedFiles(videosWithPaths[i].path);
                    } 
                }
                else
                {
                    indicesToBeDownloaded.Add(i);
                }
            }
            return indicesToBeDownloaded;
        }


        private static void CreateBackUpForDownloadedFiles(string downloadTargetDirectory)
        {
            //Backup copy if file is unintentionally overwritten
            string backupFolder = Path.GetTempPath() + "YoutubeDownloaderBackup";
            string fileName = downloadTargetDirectory[downloadTargetDirectory.LastIndexOf('\\')..];
            if (!Path.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
            }

            if (!File.Exists(backupFolder + fileName))
            {
                File.Copy(downloadTargetDirectory, backupFolder + fileName);
            }
        }

        private async Task<List<(IStreamInfo[], YoutubeExplode.Videos.Video)>> YouTubeVideosToBeLoaded(List<string> youTubeLinks)
        {
            List<(IStreamInfo[], YoutubeExplode.Videos.Video)> youTubeVideosToBeLoaded = new List<(IStreamInfo[], YoutubeExplode.Videos.Video)>();
            foreach(string video in youTubeLinks)
            {
                (IStreamInfo[], YoutubeExplode.Videos.Video) youTubeVideo = new();
                
                if (!video.Contains("playlist"))
                {
                    youTubeVideo = await GetMediaInformation(video, cancellationToken.Token);
                    if (youTubeVideo.Item1 != null && youTubeVideo.Item2 != null)
                    {
                        youTubeVideosToBeLoaded.Add(youTubeVideo);
                    }
                    else
                    {
                        invalidYouTubeLinks.Add(video);
                    }
                }
                else // at least one playlist is in the textbox
                {
                    List<(IStreamInfo[], YoutubeExplode.Videos.Video)> videoPlayList = new();
                    var youtubeClient = new YoutubeClient();
                    var videos = await youtubeClient.Playlists.GetVideosAsync(video);
                    foreach(PlaylistVideo playlistVideo in videos)
                    {
                        youTubeVideo = await GetMediaInformation(playlistVideo.Url, cancellationToken.Token);
                        if (youTubeVideo.Item1 != null && youTubeVideo.Item2 != null)
                        {
                            videoPlayList.Add(youTubeVideo);
                        }
                        else
                        {
                            invalidYouTubeLinks.Add(playlistVideo.Url);
                        }

                        
                    }
                    youTubeVideosToBeLoaded.AddRange(videoPlayList);
                }
            }

            return youTubeVideosToBeLoaded;
        }


        private void RefreshGuiCurrentDownload(double p)
        {
            // Refresh estimated remaining time and duration
            DownloadingIndicatorBar.Value = p * 100;
            taskbar.SetProgressValue((int)(p * 100), 100);
            CurrentDownloadProgressLabel.Text = $"{p * 100:0.##}%";
            TimeSpan duration = sw.Elapsed;
            Duration.Text = $"Vergangene Zeit: {duration:h\\:mm\\:ss}";
            double bytesLeft = (1.0 - p);
            TimeSpan calced = duration.Multiply(bytesLeft) / p;
            EstimatedTime.Text = $"Verbleibende Zeit: {calced:h\\:mm\\:ss}";
        }

        private static List<string> FilterForYoutubeLinks(string textToBeFiltered)
        {
            Regex youtubePattern = new Regex(@"https?://www\.youtube\.com/(watch|shorts|playlist)\S*");
            MatchCollection matches = youtubePattern.Matches(textToBeFiltered);

            //Convert MatchCollection to Hashset (for filtering duplicates) then convert it back to a list 
            List<string> videosToBeDownloaded = matches.Cast<Match>().Select(item => item.Value.Trim('\r').Trim('\n').Trim(' ')).ToHashSet<string>().ToList();
            return videosToBeDownloaded = videosToBeDownloaded.Select(element => element = element.Trim('\r').Trim('\n').Trim(' ')).ToHashSet<string>().ToList();
        }
        #endregion

        #region Controlling GUI elements before and after download
        private void InitializeAppForDownloading()
        {
            DownloadList.IsEnabled = false;
            ParallelDownload.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;
            VideoList.IsEnabled = false;
            CancelOperation.IsEnabled = true;
            DownloadingIndicatorBar.Visibility = Visibility.Visible;
            DownloadProgress.Visibility = Visibility.Visible;
            CurrentDownload.Visibility = Visibility.Visible;
            CancelAll.IsEnabled = true;
            ProgressIndicator.Visibility = Visibility.Visible;
            DownloadingIndicatorBar.Foreground = Brushes.Green;
            DownloadProgress.Foreground = Brushes.Green;
            DownloadProgress.Value = 0;
            taskbar.SetProgressState(TaskbarProgressBarState.Normal);
        }

        private void FinalizeDownloads()
        {
            // Enable all controls after download
            DownloadList.IsEnabled = true;
            ParallelDownload.IsEnabled = true;
            Audio.IsEnabled = true;
            Video.IsEnabled = true;
            BrowseSaveDirectory.IsEnabled = true;
            VideoList.IsEnabled = true;
            CancelOperation.IsEnabled = false;
            CancelAll.IsEnabled = false;
            DownloadProgress.Value = 100;
            cancelCurrentDownload = false;
            cancelAllDownloads = false;

            // Cancel running tasks (loop for cancel downloads) and create a new cancellationToken for new download sessions
            CurrentDownload.Visibility = Visibility.Hidden;
            CurrentDownload.Text = "";
            ParallelDownload.Content = "Paralleler Download";
            DownloadList.Content = "Dateien herunterladen";
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
            taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
            DownloadProgress.Value = 100;
            ProgressIndicator.Text = $"Gesamtfortschritt: ";
            System.Windows.MessageBox.Show("Alle Vorgänge abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            taskbar.SetProgressState(TaskbarProgressBarState.NoProgress);
        }
        #endregion
    }
}
