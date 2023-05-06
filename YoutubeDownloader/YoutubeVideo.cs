using System;
using System.IO;

using VideoLibrary;

namespace YoutubeDownloader
{
    public class YoutubeVideo
    {
        public string VideoUrl { get; set; }
        public string DownloadDirectory { get; set; } = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");

        public YouTube YoutubeMediaContainer { get; set; }

        public string MediaExtension { get; set; } = ".mp3";

        /// <summary>
        /// Set the directory where the media should be saved
        /// How do you get the System Dialog for retrieving a path
        /// </summary>
        /// <returns></returns>
        public string SetDirectory()
        {
            return "";
        }
    }
}
