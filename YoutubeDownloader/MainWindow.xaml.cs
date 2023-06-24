using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using VideoLibrary;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Taskbar;


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
        Stopwatch sw = new Stopwatch();
        bool pausePressed = false;
        Stream streamForSequentialDownload;
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
        private void OnPauseClicked(object sender, RoutedEventArgs e)
        {
            if (!pausePressed)
            {
                PauseDownload.Content = "Fortsetzen";
            }
            else
            {
                PauseDownload.Content = "Pause";
            }
            pausePressed = !pausePressed;

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
        #endregion

        #region Sequential Download

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();

            List<(YouTubeVideo vids, string path, string link)> vidsWithPathsAndLinks = await GenerateListOfDownloads();


            // Initialize variables for progress bar
            uint downloadedVideos = 0;
            ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";


            foreach ((YouTubeVideo, string, string) video in vidsWithPathsAndLinks)
            {
                sw.Reset();

                CurrentDownload.Text += $" {video.Item3.ReplaceLineEndings(string.Empty)}";

                try
                {
                    await DownloadYoutubeVideoAsync(video, cancellationToken.Token);
                } // After the exception in DownloadYoutubeVideoAsync the exception is not caught on this level. Instead this methods does not go to the next element in the list

                catch (OperationCanceledException)
                {

                    HandleCanceledDownload(video, streamForSequentialDownload);

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
                    DisposeAndCloseStream(streamForSequentialDownload);
                    downloadedVideos++;
                    DownloadProgress.Value = downloadedVideos * 100 / (uint)vidsWithPathsAndLinks.Count;
                    ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {vidsWithPathsAndLinks.Count} Dateien";
                }

 
                // Remove current download text from label 
                CurrentDownload.Text = CurrentDownload.Text.Replace($" {video.Item3.ReplaceLineEndings(string.Empty)}", string.Empty);

                // Remove unnecessary data from memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            FinalizeDownloads();
        }

        private async Task DownloadYoutubeVideoAsync((YouTubeVideo, string, string) mediaToBeLoaded, CancellationToken cts)
        {   
            try
            {
                CurrentDownload.Text += $" \nDateiname: {mediaToBeLoaded.Item2.Split('\\').Last()}";

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

            catch (VideoLibrary.Exceptions.UnavailableStreamException ex)
            {
                if (ex.Message.Contains("Alter"))
                {
                    System.Windows.MessageBox.Show($"Die Altersbeschränkung kann mit dem Youtube Downloader NOCH nicht umgangen werden. {Environment.NewLine} {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    System.Windows.MessageBox.Show("Der Link zu dem Youtube Video ist fehlerhaft oder das Video existiert nicht!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
                CurrentDownload.Text = CurrentDownload.Text.Replace($" \nDateiname: {mediaToBeLoaded.Item2.Split('\\').Last()}", string.Empty);
            }
        }

        private async Task DownloadVideo((YouTubeVideo, string, string) vid, CancellationToken cts)
        {
            sw.Start();
            streamForSequentialDownload = File.OpenWrite(vid.Item2);
            await WriteFileToDrive(vid, streamForSequentialDownload, cts);
        }

        #endregion



        #region Parallel Download
        private async void ParallelDownload_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();

            List<(YouTubeVideo vids, string path, string link)> vidsWithPathsAndLinks = await GenerateListOfDownloads();

            List<Task> tasks = new List<Task>();

            foreach ((YouTubeVideo, string,string) media in vidsWithPathsAndLinks)
            {
                tasks.Add(DownloadVideosParallel(media, CancellationToken.None));
            }

            sw.Start();
            await Task.WhenAll(tasks);
            sw.Stop();

            FinalizeDownloads();
        }


        private async Task DownloadVideosParallel((YouTubeVideo, string, string) media, CancellationToken cts)
        {
            // There needs to be a single stream for each parallel download otherwise it could cause problems
            Stream output = File.OpenWrite(media.Item2);

            await WriteFileToDrive(media, output, cts);

            DisposeAndCloseStream(output);
        }

        #endregion


        private async Task WriteFileToDrive((YouTubeVideo, string, string) media,Stream stream, CancellationToken cts)
        {

            //Equality starts here
            var client = new HttpClient();

            long? totalByte;
            using (var request = new HttpRequestMessage(HttpMethod.Head, media.Item1.Uri))
            {
                totalByte = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts).Result.Content.Headers.ContentLength;
            }

            using var input = await client.GetStreamAsync(media.Item1.Uri, cts);
            byte[] buffer = new byte[16 * 1024];
            int read;
            int totalRead = 0;
            int lastRead = 0;


            while ((read = await input.ReadAsync(buffer, cts)) > 0)
            {
                // Task for pausing download
                await Task.Run(() =>
                {
                    while (pausePressed)
                    {
                        // paused
                    }
                }, cts);

                try
                {
                    cts.ThrowIfCancellationRequested();
                    await stream.WriteAsync(buffer.AsMemory(0, read), cts);
                    lastRead = totalRead;
                    totalRead += read;


                    RefreshGuiCurrentDownload(totalByte, totalRead);
                }

                catch (OperationCanceledException)
                {
                    DisposeAndCloseStream(stream);
                    throw;
                }
            }
            //Equality ends here
        }


        #region HelperMethods

        private async Task<YouTubeVideo> GetMediaInformation(string mediaToBeLoaded, CancellationToken cts)
        {
            YouTube youtube = YouTube.Default;
            IEnumerable<YouTubeVideo> allVids = await Task.Run(() => youtube.GetAllVideosAsync(mediaToBeLoaded), cts);
            cts.ThrowIfCancellationRequested();

            // Decide whether audio or video file is loaded.
            if ((bool)Audio.IsChecked)
            {
                allVids = allVids.Where((singleVideo) => singleVideo.AudioFormat == AudioFormat.Aac && singleVideo.AdaptiveKind == AdaptiveKind.Audio);
                return allVids.MaxBy((singleVid) => singleVid.AudioBitrate);
            }

            else
            {
                allVids = allVids.Where((singleVideo) => singleVideo.Format == VideoFormat.Mp4 && singleVideo.AdaptiveKind == AdaptiveKind.Video && singleVideo.AudioFormat != AudioFormat.Unknown);
                return allVids.MaxBy((singleVid) => singleVid.Resolution);
            }
        }

        private async Task<List<(YouTubeVideo vids, string path, string link)>> GenerateListOfDownloads()
        {
            List<string> videosToBeDownloaded = FilterForYoutubeLinks(VideoList.Text);

            List<YouTubeVideo> vids = await YouTubeVideosToBeLoaded(videosToBeDownloaded);
            List<string> paths = GenerateFullFileNameList(vids);
            List<(YouTubeVideo vids, string path, string link)> vidsWithPathsAndLinks = vids.Zip(paths, videosToBeDownloaded).ToList();

            List<int> remainingElements = CheckForAlreadyLoadedFile(vidsWithPathsAndLinks);
            return vidsWithPathsAndLinks = remainingElements.Select(index => vidsWithPathsAndLinks[index]).ToList();
        }


        private void HandleCanceledDownload((YouTubeVideo, string, string) video, Stream output)
        {
            DisposeAndCloseStream(output);
            // Remove unnecessary data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            cancellationToken = new CancellationTokenSource();

            // If this messagebox is not integrated the file stream cannot be deleted if the download is canceled
            taskbar.SetProgressState(TaskbarProgressBarState.Paused);
            taskbar.SetProgressValue(100, 100);
            System.Windows.MessageBox.Show($"Der Download der Datei {video.Item2.Split('\\').Last()} wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation,MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            CurrentDownload.Text = "Aktueller Download: ";
            DownloadProgress.Foreground = Brushes.Yellow;

            // Download was started and file did not exist before current download session
            if (File.Exists(video.Item2) && (cancelAllDownloads || cancelCurrentDownload) && output != null)
            {
                File.Delete(video.Item2);
            }
            PauseDownload.Content = "Pause";
            pausePressed = false;
        }

        private string GenerateFullFileName(YouTubeVideo video)
        {
            // Remove invalid characters from Youtube video title -> filename
            string videoTitle = string.Join('_', video.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            return (bool)Audio.IsChecked ? videoTitle + ".mp3" : videoTitle + ".mp4";
        }


        private List<string> GenerateFullFileNameList(List<YouTubeVideo> youTubeVideos)
        {
            List<string> fullFilePaths = new List<string>();
            foreach (var video in youTubeVideos)
            {
                fullFilePaths.Add(DownloadDirectory.Text + GenerateFullFileName(video));
            }
            return fullFilePaths;
        }


        private static void DisposeAndCloseStream(Stream output)
        {
            output?.Dispose();
            output?.Close();
        }


        private List<int> CheckForAlreadyLoadedFile(List<(YouTubeVideo, string, string)> videosWithPaths)
        {
            List<int> indicesToBeDownloaded = new List<int>();
            for (int i = 0; i < videosWithPaths.Count; i++)
            {
                // Check if file name already exists in directory before downloading
                if (File.Exists(videosWithPaths[i].Item2))
                {
                    taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
                    DialogResult overwriteAlreadyDownloadedFile = System.Windows.Forms.MessageBox.Show($"Die Datei \n{videosWithPaths[i].Item2} \nexistiert bereits. Soll der Download übersprungen werden?", "Datei existiert bereits!", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly);

                    if (overwriteAlreadyDownloadedFile == System.Windows.Forms.DialogResult.No)
                    {
                        indicesToBeDownloaded.Add(i);
                    }

                }
                else
                {
                    indicesToBeDownloaded.Add(i);
                }
            }
            return indicesToBeDownloaded;
        }


        private async Task<List<YouTubeVideo>> YouTubeVideosToBeLoaded(List<string> youTubeLinks)
        {
            List<YouTubeVideo> youTubeVideosToBeLoaded = new List<YouTubeVideo>();
            foreach (string video in youTubeLinks)
            {
                YouTubeVideo youTubeVideo = await GetMediaInformation(video, cancellationToken.Token);
                youTubeVideosToBeLoaded.Add(youTubeVideo);
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
            Regex youtubePattern = new Regex(@"https?://www\.youtube\.com/(watch|shorts).*");
            MatchCollection matches = youtubePattern.Matches(textToBeFiltered);

            //Convert MatchCollection to Hashset (for filtering duplicates) then convert it back to a list
            List<string> videosToBeDownloaded = matches.Cast<Match>().Select(item => item.Value).ToHashSet<string>().ToList();
            return videosToBeDownloaded = videosToBeDownloaded.Select(element => element = element.Trim('\r').Trim('\n')).ToList();
        }
        #endregion

        #region Controlling GUI elements before and after download
        private void InitializeAppForDownloading()
        {
            DownloadList.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;
            VideoList.IsEnabled = false;
            CancelOperation.IsEnabled = true;
            PauseDownload.IsEnabled = true;
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
            Audio.IsEnabled = true;
            Video.IsEnabled = true;
            BrowseSaveDirectory.IsEnabled = true;
            PauseDownload.IsEnabled = false;
            VideoList.IsEnabled = true;
            CancelOperation.IsEnabled = false;
            CancelAll.IsEnabled = false;
            DownloadProgress.Value = 100;

            // Cancel running tasks (loop for cancel downloads) and create a new cancellationToken for new download sessions
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
            taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
            System.Windows.MessageBox.Show("Alle Vorgänge abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            taskbar.SetProgressState(TaskbarProgressBarState.NoProgress);
        }
        #endregion
    }
}
