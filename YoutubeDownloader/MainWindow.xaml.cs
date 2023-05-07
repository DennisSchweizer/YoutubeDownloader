using MediaToolkit;
using MediaToolkit.Model;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using VideoLibrary;

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
            Url.Text = "Link zum Youtube Video hier einfügen";
            Audio.IsChecked = true;
            Video.IsChecked = false;
        }

        private void BrowseSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            //ToDo: Implement system dialog where to set the path -> Code from Stackoverflow
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
            //ToDo: Set all GUI-Elements to default values
            Url.Text = string.Empty;
            DownloadDirectory.Text = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
            Audio.IsChecked = true;
            Video.IsChecked = false;
        }

        private void ClearTextBox_Click(object sender, RoutedEventArgs e)
        {
            Url.Text = string.Empty;
        }

        private async Task<string> DownloadYoutubeVideo(string mediaToBeLoaded, string downloadDir)
        {
            //Disable all Buttons before download active
            DownloadBtn.IsEnabled = false;
            ResetApplication.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;

            var youtube = YouTube.Default;
            string videoFullName;
            try
            {
                var vid = await Task.Run(() => youtube.GetVideo(mediaToBeLoaded));
                videoFullName = downloadDir + vid.FullName;
                await Task.Run(() => File.WriteAllBytes(videoFullName, vid.GetBytes()));
            }

            catch (ArgumentException)
            {
                System.Windows.MessageBox.Show("Ungültiger Link! Gebe einen Link zu einem Youtube Video ein!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return "";
            }
            catch (VideoLibrary.Exceptions.UnavailableStreamException)
            {
                System.Windows.MessageBox.Show("Der Link zu dem Youtube Video ist fehlerhaft oder das Video existiert nicht!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return "";
            }

            finally
            {
                DownloadBtn.IsEnabled = true;
                ResetApplication.IsEnabled = true;
                Audio.IsEnabled = true;
                Video.IsEnabled = true;
                BrowseSaveDirectory.IsEnabled = true;
            }
            return videoFullName;
        }

        private async Task ConvertToAudio(string filename)
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
        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Download of video just started");
            string mediaToBeLoaded = Url.Text;
            var source = DownloadDirectory.Text;
            string videoName = await DownloadYoutubeVideo(mediaToBeLoaded, source);
            System.Diagnostics.Debug.WriteLine("Finished download!");
            // Convert the file to audio and delete the original file 
            if ((bool)Audio.IsChecked)
            {
                System.Diagnostics.Debug.WriteLine("Converting downloaded video to audio!");
                await ConvertToAudio(videoName);
            }

            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
