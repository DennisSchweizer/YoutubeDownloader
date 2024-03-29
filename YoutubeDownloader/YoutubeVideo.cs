﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace YoutubeDownloader
{
    public class YoutubeVideo
    {
        public string VideoUrl { get; set; }
        public string DownloadDirectory { get; set; } = Path.Combine(Environment.ExpandEnvironmentVariables("%USERPROFILE%"), "Downloads\\");

        public string VideoTitle { get; set; }


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
