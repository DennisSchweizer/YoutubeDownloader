This executable loads either videos or music from a provided link to a Youtube video. It uses the repos [YoutubeExplode by Tyrrrz](https://github.com/Tyrrrz/YoutubeExplode)) and wraps its functionalities in a GUI. 

ToDos:
- [] refactor and try to merge functionalitites of parallel and sequential download
- [] improve controls -> scaling windows
- [] split GUI functions from basic functions
- [] improve progress evaluation if age restricted video is downloaded because it is much slower than larger unrestricted downloads
- [] implement update mechanism 
- [] get media tags especially for music

Done:
- [x] make download asynchronuous
- [X] paste link from Clipboard if clicked on textbox
- [x] validate content of textbox
- [x] disable buttons while a download is already running
- [x] implement Progress bar
- [x] implement indicator for currently running download -> another progress bar (indeterminate)
- [x] add support for multiple downloads from a textfile / textfield
- [x] improve behavior of link textbox e.g. display title(s) of video -> now displaying video title while downloading
- [X] cancel downloads by clicking on button
- [x] improve validation of textbox with youtube links
- [x] improve behavior of canceled or 0 downloads e.g. deleting files, Messageboxes displaying video titles etc.
- [x] add more settings for output formats (video resolution, audio bitrate etc.). The best bitrate for audio files will now be downloaded for videos difficult since most video files don't have audio
- [x] instead of canceling one download cancel all downloads
- [x] implemented progress bar behavior in task bar
- [x] integrate faster custom youtube download client
- [x] improve behavior of download progress bar if downloads are canceled 
- [x] add percentage label for single download 
- [x] show estimated duration of downloads
- [x] implement parallel downloads
- [x] create backup in temp if download did already exist and if it will be overwritten
- [x] handle exceptions which occur randomly if download is started
- [x] add age verification fix -> fixed by integrating YoutubeExplode
- [x] try to merge best audio file with best video file