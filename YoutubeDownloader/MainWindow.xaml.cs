using MediaToolkit;
using MediaToolkit.Model;
using System;
using System.Collections.Generic;
using System.IO;
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
        public MainWindow()
        {
            InitializeComponent();
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
            VideoList.Text = "Links zu den Videos hier in je eine Zeile einfügen";
            Audio.IsChecked = true;
            Video.IsChecked = false;
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
                    VideoList.Text = VideoList.Text.Insert(0, $"{videoLink}");
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
        }
        #endregion


        #region Async methods
        private async Task<string> DownloadYoutubeVideoAsync(string mediaToBeLoaded, string downloadDir)
        {
            //Disable all Buttons before download active
            DownloadList.IsEnabled = false;
            ResetApplication.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;

            var youtube = YouTube.Default;
            string videoFullName = null;
            try
            {
                var vid = await Task.Run(() => youtube.GetVideo(mediaToBeLoaded));
                videoFullName = downloadDir + vid.FullName;
                await Task.Run(() => File.WriteAllBytes(videoFullName, vid.GetBytes()));
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

            catch (System.Net.Http.HttpRequestException)
            {
                System.Windows.MessageBox.Show("Die Verbindung zu Youtube konnte nicht hergestellt werden. Überprüfen Sie die Internetverbindung!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            finally
            {
                //Enable all controls after download
                DownloadList.IsEnabled = true;
                ResetApplication.IsEnabled = true;
                Audio.IsEnabled = true;
                Video.IsEnabled = true;
                BrowseSaveDirectory.IsEnabled = true;
            }
            return videoFullName ?? null;
        }

        private async Task ConvertToAudioAsync(string filename)
        {
            var inputFile = new MediaFile { Filename = filename };
            //  -4 since length is 1 more than maximum index and additional 3 in order to cut mp3
            var outputFile = new MediaFile { Filename = $"{filename.Substring(0, filename.Length - 4)}.mp3" };

            using (var engine = new Engine())
            {
                await Task.Run(() => engine.GetMetadata(inputFile));
                await Task.Run(() => engine.Convert(inputFile, outputFile));
            }
            await Task.Run(() => File.Delete(filename));
        }

        #endregion

        private async void DownloadList_Click(object sender, RoutedEventArgs e)
        {
            List<string> videosToBeDownloaded = VideoList.Text.Split('\r', (char)StringSplitOptions.RemoveEmptyEntries).Where(element => !string.IsNullOrEmpty(element)).ToHashSet<string>().ToList();

            foreach (string video in videosToBeDownloaded)
            {
                System.Diagnostics.Debug.WriteLine("Download of video just started");
                string videoName = await DownloadYoutubeVideoAsync(video, DownloadDirectory.Text);
                if (videoName == null)
                {
                    return;
                }
                System.Diagnostics.Debug.WriteLine("Finished download!");
                // Convert the file to audio and delete the original file 
                if ((bool)Audio.IsChecked)
                {
                    System.Diagnostics.Debug.WriteLine("Converting downloaded video to audio!");
                    await ConvertToAudioAsync(videoName);
                }
            }

            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
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
