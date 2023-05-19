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
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        CancellationTokenSource cancellationToken = new CancellationTokenSource();
        string videoTitle = string.Empty;
        bool pressed = false;

        public MainWindow()
        {
            InitializeComponent();
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
            VideoList.Text = "Links zu den Videos hier in je eine Zeile einfügen";
            Audio.IsChecked = true;
            Video.IsChecked = false;
            CancelOperation.IsEnabled = false;
        }

        private void LinkGotFocus(object sender, RoutedEventArgs e)
        {
            VideoList.Foreground = Brushes.Black;
            if (VideoList.Text.Equals("Links zu den Videos hier in je eine Zeile einfügen"))
            {
                VideoList.Text = string.Empty;
            }


            List<string> videosToBeDownloaded = FilterForYoutubeLinks(System.Windows.Clipboard.GetText());

            foreach (string videoLink in videosToBeDownloaded)
            {
                if(VideoList.Text == string.Empty)
                    VideoList.Text = VideoList.Text.Insert(0, $"{videoLink}");
                else
                    VideoList.Text = VideoList.Text.Insert(0, $"{videoLink}{Environment.NewLine}");
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
        #endregion

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            //CancelOperation.Background = Brushes.Red;
            pressed = true;
        }


        #region Async methods
        private async Task DownloadYoutubeVideoAsync(string mediaToBeLoaded, string downloadDir, CancellationToken cts)
        {
            //Disable all Buttons before download active
            DownloadList.IsEnabled = false;
            ResetApplication.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;
            VideoList.IsEnabled = false;
            CancelOperation.IsEnabled = true;
           

            var youtube = YouTube.Default;
            string videoFullName = null;
            byte[] videoAsBytes = Array.Empty<byte>();
            try
            {
                cts.ThrowIfCancellationRequested();
                var vid = await Task.Run(() => youtube.GetVideo(mediaToBeLoaded), cts);
                videoTitle = vid.FullName;
                videoFullName = downloadDir + videoTitle;
                cts.ThrowIfCancellationRequested();
                try
                {
                    await Task.WhenAny(Task.Run(() => videoAsBytes = vid.GetBytes(), cts), Task.Run(() =>
                    {
                        while (!pressed)
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
                //Enable all controls after download
                DownloadList.IsEnabled = true;
                ResetApplication.IsEnabled = true;
                Audio.IsEnabled = true;
                Video.IsEnabled = true;
                BrowseSaveDirectory.IsEnabled = true;
                VideoList.IsEnabled = true;
                CancelOperation.IsEnabled = false;
                Array.Clear(videoAsBytes);
                videoAsBytes = null;
            }
        }

        private async Task ConvertToAudioAsync(string filename, CancellationToken cts)
        {
            cts.ThrowIfCancellationRequested();
            var inputFile = new MediaFile { Filename = filename };
            //  -4 since length is 1 more than maximum index and additional 3 in order to cut mp3
            var outputFile = new MediaFile { Filename = $"{filename.Substring(0, filename.Length - 4)}.mp3" };
            string downloadDir = DownloadDirectory.Text;
            using (var engine = new Engine())
            {
                await Task.Run(() => engine.GetMetadata(inputFile));
                cts.ThrowIfCancellationRequested();
                await Task.Run(() => engine.Convert(inputFile, outputFile));
                cts.ThrowIfCancellationRequested();
            }
            await Task.Run(() => File.Delete(filename));
        }

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            uint downloadedVideos = 0;
            uint percentageDownloadedVideos = 0;
            List<string> videosToBeDownloaded = VideoList.Text.Split('\r', (char)StringSplitOptions.RemoveEmptyEntries).Where(element => !string.IsNullOrEmpty(element)).ToHashSet<string>().ToList();
            DownloadingIndicatorBar.Visibility = Visibility.Visible;
            CurrentDownload.Visibility = Visibility.Visible;

            foreach (string video in videosToBeDownloaded)
            {
                if (video.StartsWith("\n"))
                {
                    CurrentDownload.Text += $" {video.Replace("\n",string.Empty)}";
                }
                else
                {
                    CurrentDownload.Text += $" {video}";
                }
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
                // Convert the file to audio and delete the original file 
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
                downloadedVideos++;
                percentageDownloadedVideos = downloadedVideos * 100 / (uint) videosToBeDownloaded.Count;
                DownloadProgress.Value = percentageDownloadedVideos;
                CurrentDownload.Text = CurrentDownload.Text.Replace($" {video}", string.Empty);
                if (VideoList.Text.Contains(video))
                {
                    // ToDo Either delete the link or highlight it in some way
                    //VideoList.Text = VideoList.Text.Replace(video, string.Empty);
                    
                }
            }
            DownloadingIndicatorBar.Visibility = Visibility.Hidden;
            CurrentDownload.Visibility = Visibility.Hidden;
            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            DownloadProgress.Value = 0;
        }
        #endregion

        private void HandleCanceledDownload(string source, string videoName)
        {
            pressed = false;
            //CancelOperation.Background = new SolidColorBrush(Color.FromRgb(0xDD,0xDD,0xDD));
            string videoFile = $"{source}{videoName}";
            string audioFile = $"{source}{videoName.Remove(videoName.Length - 1) + "3"}";

            if (File.Exists(videoFile))
            {
                File.Delete(videoFile);
            }
            if (File.Exists(audioFile))
            {
                File.Delete(audioFile);
            }
            cancellationToken = new CancellationTokenSource();
            System.Windows.MessageBox.Show("Der Download wurde abgebrochen!", "Abbruch!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }

        private List<string> FilterForYoutubeLinks(string textToBeFiltered)
        {
            Regex youtubePattern = new Regex(@"https?://www\.youtube\.com/(watch|shorts).*");
            MatchCollection matches = youtubePattern.Matches(textToBeFiltered);

            //Convert MatchCollection to Hashset (for filtering duplicates) then convert it back to a list
            return matches.Cast<Match>().Select(item => item.Value).ToHashSet<string>().ToList();
        }
    }
}
