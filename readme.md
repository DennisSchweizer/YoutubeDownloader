This executable loads either videos or music from a provided link to a Youtube video. It uses the repo [libvideo by omansak](https://github.com/omansak/libvideo) and wraps its functionality in a GUI. 

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
- [] improve controls -> scaling windows
- [x] improve validation of textbox with youtube links
- [] improve behavior of canceled or 0 downloads e.g. deleting files, Messageboxes displaying video titles etc.
- [] add more settings for output formats
- [] handle exceptions which occur randomly if download is started