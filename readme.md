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
- [] improve controls -> scaling windows
- [] improve behavior of canceled or 0 downloads e.g. deleting files, Messageboxes displaying video titles etc.
- [] add more settings for output formats (video resolution, audio bitrate etc.)
- [] handle exceptions which occur randomly if download is started
- [] instead of canceling one download cancel all downloads