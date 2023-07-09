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
using AngleSharp.Common;

namespace YoutubeDownloader
{

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
        Stream streamForSequentialDownload;

        // If youtube videos cannot be accessed they need to be removed otherwise wrong links for a download may be shown
        List<string> invalidYouTubeLinks = new List<string>();

        // For estimating remaining time with parallel downloads 
        (IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) largestMedium;


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

            List<(IStreamInfo vids, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = await GenerateListOfDownloads();


            // Initialize variables for progress bar
            uint downloadedVideos = 0;
            ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";


            foreach ((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) video in vidsWithPathsAndLinks)
            {
                sw.Reset();

                CurrentDownload.Text += $"{video.video.Url.ReplaceLineEndings(string.Empty)}";

                try
                {
                    await DownloadYoutubeVideoAsync(video, cancellationToken.Token);
                } // After the exception in DownloadYoutubeVideoAsync the exception is not caught on this level. Instead this methods does not go to the next element in the list

                catch (OperationCanceledException)
                {

                    await HandleCanceledDownload(video, streamForSequentialDownload);

                    if (cancelCurrentDownload)
                    {
                        cancelCurrentDownload = false;
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
                    await DisposeAndCloseStream(streamForSequentialDownload);
                    downloadedVideos++;
                    DownloadProgress.Value = downloadedVideos * 100 / (uint)vidsWithPathsAndLinks.Count;
                    ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";
                }

 
                // Remove current download text from label 
                CurrentDownload.Text = CurrentDownload.Text.Replace($"{video.path.ReplaceLineEndings(string.Empty)}", string.Empty);

                // Remove unnecessary data from memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            CurrentDownload.Visibility = Visibility.Hidden;
            FinalizeDownloads();
        }

        private async Task DownloadYoutubeVideoAsync((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) mediaToBeLoaded, CancellationToken cts)
        {   
            try
            {
                CurrentDownload.Text += $"\nDateiname: {mediaToBeLoaded.path.Split('\\').Last()}";

                await Task.WhenAny(DownloadVideo(mediaToBeLoaded, cts), Task.Run(() =>
                {
                    while (!cancelCurrentDownload && !cancelAllDownloads)
                    {
                        //nop -> this loop is finished when the Cancel Button is pressed
                    }
                }, cts));

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

        private async Task DownloadVideo((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) vid, CancellationToken cts)
        {
            sw.Start();
            var youtube = new YoutubeClient();
            //streamForSequentialDownload = File.OpenWrite(vid.Item3);
            //await WriteFileToDrive(vid, streamForSequentialDownload, true, cts);
            await youtube.Videos.Streams.DownloadAsync(vid.streams, vid.path, null, cts);
        }

        #endregion



        #region Parallel Download
        private async void ParallelDownload_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();
            ParallelDownload.Content = "Download läuft...";

            // Single download cannot be cancelled if download parallel is started
            CancelOperation.IsEnabled = false;

            List<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = await GenerateListOfDownloads();

            if(vidsWithPathsAndLinks.Count == 0)
            {
                FinalizeDownloads();
                return;
            }

            ConcurrentBag<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)> concurrentVids = new ConcurrentBag<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)>(vidsWithPathsAndLinks);

            // used in order to get largest donwload and use it as estimation for whole download time
            largestMedium = concurrentVids.MaxBy(medium => medium.streams.Size);

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



        private async Task DownloadVideosParallel((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) media, CancellationToken cts)
        {
            // There needs to be a single stream for each parallel download otherwise it could cause problems
            Stream output = await Task.Run(() => File.OpenWrite(media.path), cts);

            try
            {
                var youtubeClient = new YoutubeClient();
                //await WriteFileToDrive(media, output, false, cts);
                await youtubeClient.Videos.Streams.DownloadAsync(media.streams, media.path, null, cts);
            }
            catch (OperationCanceledException)
            {
                // Remove unnecessary data from memory
                GC.Collect();
                GC.WaitForPendingFinalizers();

                taskbar.SetProgressState(TaskbarProgressBarState.Paused);
                taskbar.SetProgressValue(100, 100);

                await DisposeAndCloseStream(output);

                if (File.Exists(media.path) && (cancelAllDownloads || cancelCurrentDownload) && output != null)
                {
                    File.Delete(media.path);
                }
            }
        }

        #endregion


        //private async Task WriteFileToDrive((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) media, Stream stream, bool refreshGui, CancellationToken cts)
        //{
        //    var client = new HttpClient();

        //    long? totalByte;
        //    using (var request = new HttpRequestMessage(HttpMethod.Head, media.Item1.Uri))
        //    {
        //        totalByte = await Task.Run(() => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts).Result.Content.Headers.ContentLength);
        //    }

        //    /*System.Net.Http.HttpRequestException
        //          HResult=0x80131500
        //          Nachricht = Response status code does not indicate success: 403 (Forbidden).
        //          Quelle = System.Net.Http
        //          Stapelüberwachung:
        //           bei System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()
        //           bei System.Net.Http.HttpClient.<GetStreamAsyncCore>d__51.MoveNext()
        //           bei YoutubeDownloader.MainWindow.<WriteFileToDrive>d__18.MoveNext() in C:\Users\Dennis\Documents\GitHub\YoutubeDownloader\YoutubeDownloader\MainWindow.xaml.cs: Zeile300
        //           bei YoutubeDownloader.MainWindow.<DownloadVideosParallel>d__17.MoveNext() in C:\Users\Dennis\Documents\GitHub\YoutubeDownloader\YoutubeDownloader\MainWindow.xaml.cs: Zeile281
        //           bei YoutubeDownloader.MainWindow.<<ParallelDownload_Click>b__16_0>d.MoveNext() in C:\Users\Dennis\Documents\GitHub\YoutubeDownloader\YoutubeDownloader\MainWindow.xaml.cs: Zeile255
        //           bei System.Threading.Tasks.Parallel.<>c__50`1.<<ForEachAsync>b__50_0>d.MoveNext()
        //           bei YoutubeDownloader.MainWindow.<ParallelDownload_Click>d__16.MoveNext() in C:\Users\Dennis\Documents\GitHub\YoutubeDownloader\YoutubeDownloader\MainWindow.xaml.cs: Zeile253
        //     */
        //    Stream stream1 = null;
        //    AwaitStreamAgain:
        //    try
        //    {
        //        stream1 = await client.GetStreamAsync(media.Item1.Uri, cts);
        //    }
        //    catch (HttpRequestException)
        //    {
        //        await Task.Delay(3000, cts);
        //        goto AwaitStreamAgain;
        //    }

        //    using Stream input = stream1;
        //    byte[] buffer = new byte[16 * 1024];
        //    int read;
        //    int totalRead = 0;
        //    int lastRead = 0;


        //    while ((read = await input.ReadAsync(buffer, cts)) > 0)
        //    {
        //        try
        //        {
        //            cts.ThrowIfCancellationRequested();

        //            await stream.WriteAsync(buffer.AsMemory(0, read), cts);
        //            lastRead = totalRead;
        //            totalRead += read;


        //            if(refreshGui)
        //            {
        //                RefreshGuiCurrentDownload(totalByte, totalRead);
        //            }
        //            else
        //            {
        //                await Dispatcher.BeginInvoke(() =>
        //                {
        //                    // Refresh estimated remaining time and duration
        //                    TimeSpan duration = sw.Elapsed;
        //                    Duration.Text = $"Vergangene Zeit: {duration:h\\:mm\\:ss}";

        //                    if (media == largestMedium)
        //                    {
        //                        // Refresh progress percenatge label
        //                        double currentProgress = totalRead / (double)totalByte * 100;
        //                        DownloadingIndicatorBar.Value = currentProgress;
        //                        CurrentDownloadProgressLabel.Text = $"{currentProgress:0.##}%";
        //                        taskbar.SetProgressValue(totalRead, (int)totalByte);

        //                        // Refresh estimated remaining time and duration
        //                        double bytesLeft = ((double)totalByte - totalRead);
        //                        TimeSpan calced = duration.Multiply(bytesLeft) / totalRead;
        //                        EstimatedTime.Text = $"Verbleibende Zeit: {calced:h\\:mm\\:ss}";
        //                    }
        //                });
        //            }

        //        }

        //        catch (OperationCanceledException)
        //        {
        //            await DisposeAndCloseStream(stream);
        //            throw;
        //        }
        //    }
        //    await DisposeAndCloseStream(stream);
        //}


        #region HelperMethods

        private async Task<(IStreamInfo, YoutubeExplode.Videos.Video)> GetMediaInformation(string mediaToBeLoaded, CancellationToken cts)
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

            IStreamInfo mediaManifest;
            // Decide whether audio or video file is loaded.
            if ((bool)Audio.IsChecked)
            {
                // LINQ necessary because the default audio format is opus which is incompatible with mp3
                mediaManifest = allStreamInfos.GetAudioOnlyStreams().Where(format => format.Container == Container.Mp4).GetWithHighestBitrate();
            }

            else
            {
                mediaManifest = allStreamInfos.GetMuxedStreams().GetWithHighestVideoQuality();
            }

            return (mediaManifest, videoData);
        }

        private async Task<List<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)>> GenerateListOfDownloads()
        {
            List<string> videosToBeDownloaded = FilterForYoutubeLinks(VideoList.Text);


            List<(IStreamInfo, YoutubeExplode.Videos.Video)> allMediaData = await YouTubeVideosToBeLoaded(videosToBeDownloaded);
            List<string> paths = GenerateFullFileNameList(allMediaData);


            videosToBeDownloaded= videosToBeDownloaded.Where(link => !invalidYouTubeLinks.Contains(link)).ToList();


            List<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)> vidsWithPathsAndLinks = new List<(IStreamInfo, YoutubeExplode.Videos.Video, string)>();
            
            for(int i = 0; i < allMediaData.Count; i++) { 
            
                (IStreamInfo, YoutubeExplode.Videos.Video, string) tripel = (allMediaData[i].Item1, allMediaData[i].Item2, paths[i]);
                vidsWithPathsAndLinks.Add(tripel);
            }

            List<int> remainingElements = CheckForAlreadyLoadedFile(vidsWithPathsAndLinks);
            return vidsWithPathsAndLinks = remainingElements.Select(index => vidsWithPathsAndLinks[index]).ToList();
        }


        private async Task HandleCanceledDownload((IStreamInfo streams, YoutubeExplode.Videos.Video video, string path) video, Stream output)
        {
            await DisposeAndCloseStream(output);

            // Remove unnecessary data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            cancellationToken = new CancellationTokenSource();

            taskbar.SetProgressState(TaskbarProgressBarState.Paused);
            taskbar.SetProgressValue(100, 100);
            System.Windows.MessageBox.Show($"Der Download der Datei {video.path.Split('\\').Last()} wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation,MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            DownloadProgress.Foreground = Brushes.Yellow;
            CurrentDownload.Text = string.Empty;
            // Download was started and file did not exist before current download session
            if (File.Exists(video.path) && (cancelAllDownloads || cancelCurrentDownload) && output != null)
            {
                File.Delete(video.path);
            }
        }

        // Needs to be modified
        private string GenerateFullFileName(YoutubeExplode.Videos.Video video)
        {
            // Remove invalid characters from Youtube video title -> filename
            string videoTitle = string.Join('_', video.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            return (bool)Audio.IsChecked ? videoTitle  + ".mp3" : videoTitle + ".mp4";
        }


        private List<string> GenerateFullFileNameList(List<(IStreamInfo, YoutubeExplode.Videos.Video)> youTubeVideos)
        {
            List<string> fullFilePaths = new List<string>();
            foreach (var video in youTubeVideos)
            {
                fullFilePaths.Add(DownloadDirectory.Text + GenerateFullFileName(video.Item2));
            }
            return fullFilePaths;
        }


        private async static Task DisposeAndCloseStream(Stream output)
        {
           await Task.Run(()=> output?.Dispose());
           await Task.Run(() => output?.Close());
        }


        private List<int> CheckForAlreadyLoadedFile(List<(IStreamInfo streams, YoutubeExplode.Videos.Video video, string path)> videosWithPaths)
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

        private async Task<List<(IStreamInfo, YoutubeExplode.Videos.Video)>> YouTubeVideosToBeLoaded(List<string> youTubeLinks)
        {
            List<(IStreamInfo, YoutubeExplode.Videos.Video)> youTubeVideosToBeLoaded = new List<(IStreamInfo, YoutubeExplode.Videos.Video)>();
            foreach (string video in youTubeLinks)
            {
                (IStreamInfo, YoutubeExplode.Videos.Video) youTubeVideo = await GetMediaInformation(video, cancellationToken.Token);

                // youTubeVideo can become null if an age restricted video is in the download list or an broken link
                if(youTubeVideo.Item1 != null && youTubeVideo.Item2 != null)
                {
                    youTubeVideosToBeLoaded.Add(youTubeVideo);
                }
                else
                {
                    invalidYouTubeLinks.Add(video);
                }
            }

            return youTubeVideosToBeLoaded;
        }


        private void RefreshGuiCurrentDownload(long? totalByte, int totalRead)
        {

            // Refresh progress percenatge label
            double currentProgress = totalRead / (double)totalByte * 100;
            DownloadingIndicatorBar.Value = currentProgress;
            CurrentDownloadProgressLabel.Text = $"{currentProgress:0.##}%";
            taskbar.SetProgressValue(totalRead, (int)totalByte);

            // Refresh estimated remaining time and duration
            TimeSpan duration = sw.Elapsed;
            Duration.Text = $"Vergangene Zeit: {duration:h\\:mm\\:ss}";
            double bytesLeft = ((double)totalByte - totalRead);
            TimeSpan calced = duration.Multiply(bytesLeft) / totalRead;
            EstimatedTime.Text = $"Verbleibende Zeit: {calced:h\\:mm\\:ss}";
        }

        private static List<string> FilterForYoutubeLinks(string textToBeFiltered)
        {
            Regex youtubePattern = new Regex(@"https?://www\.youtube\.com/(watch|shorts)\S*");
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

            // Cancel running tasks (loop for cancel downloads) and create a new cancellationToken for new download sessions
            CurrentDownload.Visibility = Visibility.Hidden;
            CurrentDownload.Text = "";
            ParallelDownload.Content = "Paralleler Download";
            DownloadList.Content = "Dateien herunterladen";
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
            taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
            System.Windows.MessageBox.Show("Alle Vorgänge abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            taskbar.SetProgressState(TaskbarProgressBarState.NoProgress);
        }
        #endregion
    }
}
