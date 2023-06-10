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
        string videoFullName = string.Empty;
        string FullFilePath = string.Empty;

        bool cancelCurrentDownload = false;
        bool cancelAllDownloads = false;
        readonly TaskbarManager taskbar = TaskbarManager.Instance;
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
        #endregion

        #region Downloading

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            InitializeAppForDownloading();

            List<string> videosToBeDownloaded = FilterForYoutubeLinks(VideoList.Text);

            #region In testing
            // NEEDS SOME IMPROVEMENT: BREAKS IF A COMBINATION OF PASTED TEXT VIA CTRL+V AND DOUBLE CLICK IS USED - FOR NOW DEACTIVATED
            //foreach (string video in videosToBeDownloaded)
            //{
            //    VideoList.Text += $"{video}";
            //}
            #endregion

            // Initialize variables for progress bar
            uint downloadedVideos = 0;
            ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {videosToBeDownloaded.Count} Dateien";


            foreach (string video in videosToBeDownloaded)
            {
                CurrentDownload.Text += $" {video.ReplaceLineEndings(string.Empty)}";

                try
                {
                    await DownloadYoutubeVideoAsync(video, cancellationToken.Token);
                } // After the exception in DownloadYoutubeVideoAsync the exception is not caught on this level. Instead this methods does not go to the next element in the list

                catch (OperationCanceledException)
                {
                    HandleCanceledDownload(videoFullName);

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
                    downloadedVideos++;
                    DownloadProgress.Value = downloadedVideos * 100 / (uint)videosToBeDownloaded.Count;
                    ProgressIndicator.Text = $"Gesamtfortschritt: {downloadedVideos} / {videosToBeDownloaded.Count} Dateien";
                }

                // Remove current download text from label 
                CurrentDownload.Text = CurrentDownload.Text.Replace($" {video.ReplaceLineEndings(string.Empty)}", string.Empty);

                if (VideoList.Text.Contains(video))
                {
                    // ToDo Either delete the link or highlight it in some way
                    //VideoList.Text = VideoList.Text.Replace(video, string.Empty);
                }

                // Remove unnecessary data from memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            FinalizeDownloads();
        }

        /// <summary>
        /// ToDo in order to customize quality / resolution / bitrate and removing the necessity for MediaToolkit NuGet package
        /// 1. Use GetAllVideosAsync() in order to check for available audio / video data
        /// 2. Pick a suiting format, resolution, fps/bitrate by using the select/where function in order to get one single item of the list
        /// 3. Maybe replace radiobuttons by checkboxes if you want to keep both audio and video files
        /// </summary>
        /// <param name="mediaToBeLoaded"></param>
        /// <param name="downloadDir"></param>
        /// <param name="cts"></param>
        /// <returns></returns>
        private async Task DownloadYoutubeVideoAsync(string mediaToBeLoaded, CancellationToken cts)
        {   
            try
            {
                YouTubeVideo vid = await GetMediaInformation(mediaToBeLoaded, cts);

                videoFullName = GenerateFullFileName(vid);
                FullFilePath = DownloadDirectory.Text + videoFullName;

                CheckForAlreadyLoadedFile(cts);

                CurrentDownload.Text += $" \nDateiname: {videoFullName}";

                await Task.WhenAny(DownloadVideo(vid, cts), Task.Run(() =>
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
                CurrentDownload.Text = CurrentDownload.Text.Replace($" \nDateiname: {videoFullName}", string.Empty);
            }
        }

        private async Task DownloadVideo(YouTubeVideo vid, CancellationToken cts)
        {
            var client = new HttpClient();
            using Stream output = File.OpenWrite(FullFilePath);
            long? totalByte = 0;
            using (var request = new HttpRequestMessage(HttpMethod.Head, vid.Uri))
            {
                totalByte = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts).Result.Content.Headers.ContentLength;
            }

            using var input = await client.GetStreamAsync(vid.Uri, cts);
            byte[] buffer = new byte[16 * 1024];
            int read;
            int totalRead = 0;

            while ((read = await input.ReadAsync(buffer, cts)) > 0)
            {
                try
                {
                    cts.ThrowIfCancellationRequested();
                    await output.WriteAsync(buffer.AsMemory(0, read), cts);
                    totalRead += read;

                    RefreshGuiCurrentDownload(totalByte, totalRead);
                }

                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }



        #endregion

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
        private void HandleCanceledDownload(string videoName)
        {
            // Remove unnecessary data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            cancellationToken = new CancellationTokenSource();

            // If this messagebox is not integrated the file stream cannot be deleted if the download is canceled
            System.Windows.MessageBox.Show($"Der Download der Datei {videoName} wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            CurrentDownload.Text = "Aktueller Download: ";
            DownloadProgress.Foreground = Brushes.Yellow;

            // Download was started and file did not exist before current download session
            if (File.Exists(FullFilePath) && cancelAllDownloads || cancelCurrentDownload)
            {
                File.Delete(FullFilePath);
            }
        }

        private string GenerateFullFileName(YouTubeVideo video)
        {
            // Remove invalid characters from Youtube video title -> filename
            string videoTitle = string.Join('_', video.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            return (bool)Audio.IsChecked ? videoTitle + ".mp3" : videoTitle + ".mp4";
        }

        private void CheckForAlreadyLoadedFile(CancellationToken cts)
        {
            // Check if file name already exists in directory before downloading
            if (File.Exists(FullFilePath))
            {
                DialogResult overwriteAlreadyDownloadedFile = System.Windows.Forms.MessageBox.Show($"Die Datei {videoFullName} existiert bereits. Soll der Download übersprungen werden?", "Datei existiert bereits!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (overwriteAlreadyDownloadedFile == System.Windows.Forms.DialogResult.Yes)
                {
                    cancellationToken.Cancel();
                }
            }

            cts.ThrowIfCancellationRequested();
        }

        private void RefreshGuiCurrentDownload(long? totalByte, int totalRead)
        {

            // Refresh progress
            double currentProgress = totalRead / (double)totalByte * 100;
            DownloadingIndicatorBar.Value = currentProgress;
            CurrentDownloadProgressLabel.Text = $"{currentProgress:0.##} %";
            taskbar.SetProgressValue(totalRead, (int)totalByte);
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

        #region ControllingGUIElementsBeforeAfterDownload
        private void InitializeAppForDownloading()
        {
            DownloadList.IsEnabled = false;
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
            Audio.IsEnabled = true;
            Video.IsEnabled = true;
            BrowseSaveDirectory.IsEnabled = true;
            VideoList.IsEnabled = true;
            CancelOperation.IsEnabled = false;
            CancelAll.IsEnabled = false;


            // Cancel running tasks (loop for cancel downloads) and create a new cancellationToken for new download sessions
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
            taskbar.SetProgressState(TaskbarProgressBarState.Indeterminate);
            System.Windows.MessageBox.Show("Alle Vorgänge abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
            taskbar.SetProgressState(TaskbarProgressBarState.NoProgress);
        }
        #endregion
    }
}
