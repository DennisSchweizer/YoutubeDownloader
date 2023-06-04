This executable loads either videos or music from a provided link to a Youtube video. It uses the repos [libvideo by omansak](https://github.com/omansak/libvideo) and [mediatoolkit](https://github.com/AydinAdn/MediaToolkit) and wraps their functionalities in a GUI. 

ToDos:
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
- [] try to merge best audio file with best video file
- [] handle exceptions which occur randomly if download is started
- [] integrate faster custom youtube download client
- [x] improve behavior of download progress bar if downloads are canceled 
- [x] add percentage label for single download 
- [] improve controls -> scaling windows
- [] add age verification fix
- [] show estimated duration of downloads
- [] refactor and split whole project