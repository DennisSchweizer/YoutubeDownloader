using MediaToolkit;
using MediaToolkit.Model;
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

namespace YoutubeDownloader
{

    public partial class MainWindow : Window
    {
        CancellationTokenSource cancellationToken = new CancellationTokenSource();
        string videoTitle = string.Empty;
        bool cancelCurrentDownload = false;

        public MainWindow()
        {
            InitializeComponent();
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
        }

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


        #region Click events
        private void BrowseSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();

                if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    DownloadDirectory.Text = $"{fbd.SelectedPath}\\";
                }
            }
        }

        private void ResetApplication_Click(object sender, RoutedEventArgs e)
        {
            //Set all GUI-Elements to default values
            VideoList.Text = "Links zu den Videos hier in je eine Zeile einfügen";
            VideoList.Foreground = Brushes.Gray;
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
            Audio.IsChecked = true;
            Video.IsChecked = false;
            cancellationToken = new CancellationTokenSource();
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            cancelCurrentDownload = true;
        }
        #endregion

        #region Async methods

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
        private async Task DownloadYoutubeVideoAsync(string mediaToBeLoaded, string downloadDir, CancellationToken cts)
        {
            YouTube youtube = YouTube.Default;
            // Initialize helper variables
            string videoFullName = null;
            byte[] videoAsBytes = Array.Empty<byte>();

            try
            {
                cts.ThrowIfCancellationRequested();
                var vid = await Task.Run(() => youtube.GetVideo(mediaToBeLoaded), cts);
                videoTitle = vid.FullName;
                CurrentDownload.Text += $" \nDateiname: {videoTitle}";
                videoFullName = downloadDir + videoTitle;
                cts.ThrowIfCancellationRequested();
                try
                {
                    await Task.WhenAny(Task.Run(() => videoAsBytes = vid.GetBytes(), cts), Task.Run(() =>
                    {
                        while (!cancelCurrentDownload)
                        {
                            //nop -> this loop is finished when the Cancel Button is pressed
                        }
                    }, cts));

                    cts.ThrowIfCancellationRequested();
                    await File.WriteAllBytesAsync(videoFullName, videoAsBytes, cts);
                    cts.ThrowIfCancellationRequested();
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    if (ex.Message.Contains("403") || ex.Message.Contains("303"))
                    {
                        System.Windows.MessageBox.Show("Youtube hat die Verbindung verweigert. Warten Sie kurz, bis die Verbindung wieder möglich ist!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
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

            catch (System.Net.Http.HttpRequestException ex)
            {
                if (ex.Message.Contains("403") || ex.Message.Contains("303")){
                    System.Windows.MessageBox.Show("Youtube hat die Verbindung verweigert. Warten Sie kurz, bis die Verbindung wieder möglich ist!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    System.Windows.MessageBox.Show("Die Verbindung zu Youtube konnte nicht hergestellt werden. Überprüfen Sie die Internetverbindung!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            finally
            {
                // Clear videoAsBytes since it is not necessary anymore
                Array.Clear(videoAsBytes);
                videoAsBytes = null;
                CurrentDownload.Text = CurrentDownload.Text.Replace($" \nDateiname: {videoTitle}", string.Empty);
            }
        }

        private async Task ConvertToAudioAsync(string filename, CancellationToken cts)
        {
            CurrentDownload.Text += $" \nUmwandlen in Audiodatei: {videoTitle}";
            cts.ThrowIfCancellationRequested();
            MediaFile inputFile = new MediaFile { Filename = filename };
            //  -4 since length is 1 more than maximum index and additional 3 in order to cut mp3
            MediaFile outputFile = new MediaFile { Filename = $"{filename.Substring(0, filename.Length - 4)}.mp3" };
            string downloadDir = DownloadDirectory.Text;
            using (Engine engine = new Engine())
            {
                await Task.Run(() => engine.GetMetadata(inputFile));
                cts.ThrowIfCancellationRequested();
                await Task.Run(() => engine.Convert(inputFile, outputFile));
                cts.ThrowIfCancellationRequested();
            }
            await Task.Run(() => File.Delete(filename));
            CurrentDownload.Text =CurrentDownload.Text.Replace($" \nUmwandlen in Audiodatei: {videoTitle}", string.Empty);
        }

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            DisableControlsWhileDownloading();

            // Initialize variables for progress bar
            uint downloadedVideos = 0;
            uint percentageDownloadedVideos = 0;


            List<string> videosToBeDownloaded = FilterForYoutubeLinks(VideoList.Text);
            //VideoList.Text = string.Empty;

            // In testing
            // NEEDS SOME IMPROVEMENT: BREAKS IF A COMBINATION OF PASTED TEXT VIA CTRL+V AND DOUBLE CLICK IS USED - FOR NOW DEACTIVATED
            //foreach (string video in videosToBeDownloaded)
            //{
            //    VideoList.Text += $"{video}";
            //}

            videosToBeDownloaded = videosToBeDownloaded.Select(element => element = element.Trim('\r').Trim('\n')).ToList();

            foreach (string video in videosToBeDownloaded)
            {
     
                CurrentDownload.Text += $" {video.ReplaceLineEndings(string.Empty)}";
                System.Diagnostics.Debug.WriteLine("Download of video just started");

                try 
                {
                    await DownloadYoutubeVideoAsync(video, DownloadDirectory.Text, cancellationToken.Token);
                }
                catch (OperationCanceledException)
                {
                    HandleCanceledDownload(DownloadDirectory.Text, videoTitle);
                    continue;
                }
                catch
                {
                    continue;
                }

                System.Diagnostics.Debug.WriteLine("Finished download!");

                // Convert the file to audio and delete the original file  if mp3 is desired
                if ((bool)Audio.IsChecked)
                {
                    System.Diagnostics.Debug.WriteLine("Converting downloaded video to audio!");
                    try 
                    {
                        await ConvertToAudioAsync(DownloadDirectory.Text + videoTitle, cancellationToken.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        HandleCanceledDownload(DownloadDirectory.Text, videoTitle);
                        continue;
                    }
                }

                // Refresh progress bar values
                downloadedVideos++;
                percentageDownloadedVideos = downloadedVideos * 100 / (uint) videosToBeDownloaded.Count;
                DownloadProgress.Value = percentageDownloadedVideos;


                // NEEDS SOME IMPROVEMENT UNTIL END OF METHOD
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

            EnableControlsAfterDownloading();
            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Cancel running tasks (loop for cancel downloads) and create a new cancellationToken for new download sessions
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
        }
        #endregion

        private void HandleCanceledDownload(string source, string videoName)
        {
            cancelCurrentDownload = false;


            // Deactivated for now. It is unclear if this function makes sense

            //string videoFile = $"{source}{videoName}";
            //string audioFile = $"{source}{videoName.Remove(videoName.Length - 1) + "3"}";


            //if (File.Exists(videoFile))
            //{
            //    File.Delete(videoFile);
            //}
            //if (File.Exists(audioFile))
            //{
            //    File.Delete(audioFile);
            //}


            // Remove unnecessary data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            cancellationToken = new CancellationTokenSource();
            System.Windows.MessageBox.Show("Der Download wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            CurrentDownload.Text = "Aktueller Download: ";
        }

        private List<string> FilterForYoutubeLinks(string textToBeFiltered)
        {
            Regex youtubePattern = new Regex(@"https?://www\.youtube\.com/(watch|shorts).*");
            MatchCollection matches = youtubePattern.Matches(textToBeFiltered);

            //Convert MatchCollection to Hashset (for filtering duplicates) then convert it back to a list
            return matches.Cast<Match>().Select(item => item.Value).ToHashSet<string>().ToList();
        }

        private void DisableControlsWhileDownloading()
        {
            DownloadList.IsEnabled = false;
            ResetApplication.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;
            VideoList.IsEnabled = false;
            CancelOperation.IsEnabled = true;
            DownloadingIndicatorBar.Visibility = Visibility.Visible;
            CurrentDownload.Visibility = Visibility.Visible;
        }

        private void EnableControlsAfterDownloading()
        {
            // Enable all controls after download
            DownloadList.IsEnabled = true;
            ResetApplication.IsEnabled = true;
            Audio.IsEnabled = true;
            Video.IsEnabled = true;
            BrowseSaveDirectory.IsEnabled = true;
            VideoList.IsEnabled = true;
            CancelOperation.IsEnabled = false;
            DownloadingIndicatorBar.Visibility = Visibility.Hidden;
            CurrentDownload.Visibility = Visibility.Hidden;
            DownloadProgress.Value = 0;
        }
    }
}
