using MediaToolkit;
using MediaToolkit.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
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
            DownloadDirectory.Text = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");
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

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            var source = DownloadDirectory.Text;
            var youtube = YouTube.Default;
            string videoFullName;
            try
            {
                var vid = youtube.GetVideo(Url.Text);
                videoFullName = source + vid.FullName;
                File.WriteAllBytes(videoFullName, vid.GetBytes());
            }

            catch(ArgumentException)
            {
                System.Windows.MessageBox.Show("Ungültiger Link! Gebe einen Link zu einem Youtube Video ein!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (VideoLibrary.Exceptions.UnavailableStreamException)
            {
                System.Windows.MessageBox.Show("Der Link zu dem Youtube Video ist fehlerhaft oder das Video existiert nicht!", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if ((bool)Audio.IsChecked)
            {
                var inputFile = new MediaFile { Filename = videoFullName };
                //  -4 since length is 1 more than maximum index and additional 3 in order to cut mp3
                var outputFile = new MediaFile { Filename = $"{videoFullName.Substring(0, videoFullName.Length - 4)}.mp3" };

                using (var engine = new Engine())
                {
                    engine.GetMetadata(inputFile);
                    engine.Convert(inputFile, outputFile);
                }
                File.Delete(videoFullName);
            }

            System.Windows.MessageBox.Show("Download abgeschlossen!", "Download erfolgreich!", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
