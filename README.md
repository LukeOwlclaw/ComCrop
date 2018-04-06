# ComCrop
A cross-platform command-line wrapper for commercial detector [comskip](https://github.com/erikkaashoek/Comskip) ([Official homepage](http://www.comskip.org/), [Forum](http://www.kaashoek.com/comskip/)).

ComCrop takes TV recording as input, passes it to comskip for commercial detecting. Then it creates chapter files and allows the user to review the chapter files. Those chapters which were not deleted by the user are finally concatenated and compressed to a single compressed MP4 output file.

## Usage

ComCrop [quiet] [nowait] [nonotify] infile1 infile2 infile3 ...
or
ComCrop [quiet] [nowait] [nonotify] *.ext

nonotify: Only create chapter files. No waiting for user to check chapter files.
nowait: No key press on exit necessary.
quiet: No output if ComCrop is locked or no file to handle. (Useful to avoid cronjob mail when there was nothing to do for ComCrop.)

Note: Output file type (and thus name) is configured in settings file comcrop.settings which is created on first run of ComCrop.

## Notes

The source code has room for enhancements and refactoring. First it was just a re-write of [comchap](https://github.com/BrettSheleski/comchap) because comchap did not play well with Cygwin. Now there are some options (as comments in the source code) which should be configurable via parameters. For that a [command line parser](https://www.codeproject.com/Articles/63374/C-NET-Command-Line-Argument-Parser-Reloaded) would be nice.
