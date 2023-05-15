using MediaToolkit;
using MediaToolkit.Model;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using VideoLibrary;

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
            Url.Text = "Link zum Youtube Video hier per Linksklick einfügen";
            Audio.IsChecked = true;
            Video.IsChecked = false;
        }

        private void LinkGotFocus(object sender, RoutedEventArgs e)
        {
            Url.Foreground = Brushes.Black;
            if (Url.Text.Equals("Link zum Youtube Video hier per Linksklick einfügen"))
            {
                Url.Text = string.Empty;
            }

            // ToDo: Add validation of content
            Url.Text = System.Windows.Clipboard.GetText();
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
            Url.Text = "Link zum Youtube Video hier per Linksklick einfügen";
            Url.Foreground = Brushes.Gray;
            DownloadDirectory.Text = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
            Audio.IsChecked = true;
            Video.IsChecked = false;
            cancellationToken = new CancellationTokenSource();
        }
        #endregion

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            CancelOperation.Background = Brushes.Red;
            pressed = true;
        }


        // ToDo for Download Async
        // 1. Handle occuring exception that appears shortly after a successful / canceled download (403 forbidden?)
        // 2. Clean up code (especially memory problems -> Dispose videoAsBytes after download)
        // 3. Cancel helper task that is making cancel responsive -> for memoty

        #region Async methods
        private async Task DownloadYoutubeVideoAsync(string mediaToBeLoaded, string downloadDir, CancellationToken cts)
        {
            //Disable all Buttons before download active
            DownloadBtn.IsEnabled = false;
            ResetApplication.IsEnabled = false;
            Audio.IsEnabled = false;
            Video.IsEnabled = false;
            BrowseSaveDirectory.IsEnabled = false;

            var youtube = YouTube.Default;
            string videoFullName = null;
            try
            {
                cts.ThrowIfCancellationRequested();
                var vid = await Task.Run(() => youtube.GetVideo(mediaToBeLoaded), cts);
                videoTitle = vid.FullName;
                videoFullName = downloadDir + videoTitle;
                cts.ThrowIfCancellationRequested();
                byte[] videoAsBytes = Array.Empty<byte>();

                await Task.WhenAny(Task.Run(() => videoAsBytes = vid.GetBytes(), cts), Task.Run(() =>
                {
                    while (!pressed)
                    {
                        //nop
                    }
                }, cts));

                //byte[] videoAsBytes = await Task.Run(() => vid.GetBytes(), cts); // the cirtical part of the application -> very slow and cts does not work with it
                cts.ThrowIfCancellationRequested();
                await File.WriteAllBytesAsync(videoFullName, videoAsBytes, cts);
                cts.ThrowIfCancellationRequested();
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
                DownloadBtn.IsEnabled = true;
                ResetApplication.IsEnabled = true;
                Audio.IsEnabled = true;
                Video.IsEnabled = true;
                BrowseSaveDirectory.IsEnabled = true;
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

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Download of video just started");
            string mediaToBeLoaded = Url.Text;
            var source = DownloadDirectory.Text;
            try
            {
                await DownloadYoutubeVideoAsync(mediaToBeLoaded, source, cancellationToken.Token);
            }
            catch (OperationCanceledException)
            {
                HandleCanceledDownload(source,videoTitle);
                return;
            }
            
            if (videoTitle == string.Empty)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine("Finished download!");
            // Convert the file to audio and delete the original file 
            if ((bool)Audio.IsChecked)
            {
                System.Diagnostics.Debug.WriteLine("Converting downloaded video to audio!");
                try
                {
                    await ConvertToAudioAsync(source + videoTitle, cancellationToken.Token);
                }
                catch(OperationCanceledException)
                {
                    HandleCanceledDownload(source, videoTitle);
                    return;
                }
            }

            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
            cancellationToken.Cancel();
            cancellationToken = new CancellationTokenSource();
        }
        #endregion

        private void HandleCanceledDownload(string source, string videoName)
        {
            pressed = false;
            CancelOperation.Background = new SolidColorBrush(Color.FromRgb(0xDD,0xDD,0xDD));
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
            System.Windows.MessageBox.Show("Der Download wurde abgebrochen!");
        }
    }
}
