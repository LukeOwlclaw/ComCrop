using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ComCrop
{
    class ComCrop
    {
        private Settings mSettings;
        private ComCropOptions mOptions;
        private TextWriter Debug;
        private TextWriter C;
        // show progress when running ffmpeg (if true, results in a lot of output of crontab log)
        private bool showStats = false;

        public ComCrop(Settings settings)
        {
            this.mSettings = settings;
            this.Debug = settings.Debug;
            this.C = settings.Console;
        }

        private void Print(string msg)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        private int RunCommand(string exe, string param)
        {
            C.WriteLine("====================================================================================");
            C.WriteLine(string.Format("Calling {0} {1}", exe, param));
            C.WriteLine("====================================================================================");
            Process process = new Process();
            var h = new ConsoleCancelEventHandler(myHandler);
            try
            {
                Console.CancelKeyPress += h;
                process.StartInfo.Arguments = param;
                process.StartInfo.FileName = exe;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardInput = true;
                process.Start();
                process.PriorityClass = ProcessPriorityClass.Idle;
                process.WaitForExit();
                return process.ExitCode;
            }
            finally
            {
                Console.CancelKeyPress -= h;
                C.WriteLine("====================================================================================");
                C.WriteLine(string.Format("End of {0} with exit code {1}", exe, process.ExitCode));
                C.WriteLine("====================================================================================");
            }
        }

        protected void myHandler(object sender, ConsoleCancelEventArgs args)
        {
            if (args.SpecialKey == ConsoleSpecialKey.ControlC)
            {
                Console.WriteLine("\nCancelling child process. Note: Press CTRL+C again, if child process continues executing.");
                args.Cancel = true;
                mOptions.AbortRequested = true;
            }
        }

        /// <summary>
        /// Current variables
        /// </summary>
        private string mFileEdl;
        private string mBaseName;
        private string mHoldFile;
        private string mOutFile;
        private string mCreatingInProgressOutFile;
        private string mInFile;
        private string mChapterExt;

        internal void CropCommercial(ComCropOptions options)
        {
            CreateChapterSuccessValue success = CreateChapterSuccessValue.Failed;
            this.mInFile = options.InFile;
            mOptions = options;
            try
            {
                var inExt = Path.GetExtension(mInFile);
                if (inExt == ".ts" && mInFile.Contains("part-"))
                {
                    Debug.WriteLine(string.Format("Detected part file {0} as input file. Skip.", mInFile));
                    success = CreateChapterSuccessValue.NotRelevantSkip;
                    return;
                }
                else if (inExt == ".mp4")
                {
                    mChapterExt = inExt;
                }
                else
                {
                    mChapterExt = ".ts";
                }
                try
                {
                    using (var fs = File.Open(mInFile, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                    }
                }
                catch (Exception ex)
                {
                    if (ex.HResult == -2147024864)
                    {
                        Debug.WriteLine(string.Format("Detected file {0} being in use. Skip.", mInFile));
                        success = CreateChapterSuccessValue.NotRelevantSkip;
                        return;
                    }
                }


                mBaseName = Path.GetFileNameWithoutExtension(mInFile);
                mHoldFile = mBaseName + ".part-select.delete-to-continue";
                // Change current directory since we are working with file name (without path)
                var inPath = Path.GetDirectoryName(mInFile);
                if (!string.IsNullOrWhiteSpace(inPath) && inPath != "." && Directory.GetCurrentDirectory() != inPath)
                {
                    Debug.WriteLine(string.Format("Changing current directory to: {0}", inPath));
                    Directory.SetCurrentDirectory(inPath);
                }
                mFileEdl = mBaseName + ".edl";
                var outNameSupplement = "";
                if (inExt == "." + options.ExtensionDestination)
                {
                    outNameSupplement = "_ComCrop";
                    Debug.WriteLine(string.Format($"Detected same input extension as output extension. Append \"{outNameSupplement}\" to output file"));
                }
                mOutFile = string.Format("{0}{1}.{2}", mBaseName, outNameSupplement, mOptions.ExtensionDestination);
                mCreatingInProgressOutFile = string.Format("{0}.creating-in-progress", mOutFile);
                Print(string.Format("Cropping commercials from {0}", mInFile));

                if (File.Exists(mOutFile) && !File.Exists(mCreatingInProgressOutFile))
                {
                    C.WriteLine(string.Format("Output file {0} already exists. Skip.", mOutFile));
                    success = CreateChapterSuccessValue.AlreadyExisted;
                    return;
                }

                if (CreateEdlFile() == false)
                {
                    return;
                }
                var ret = CreateChapterFiles();
                if (mOptions.NoNotify)
                {
                    CreateFile(mHoldFile);
                    C.WriteLine("NoNotify set. Skip waiting for user to check chapter files.");
                    return;
                }
                if (ret == CreateChapterSuccessValue.Failed)
                {
                    return;
                }
                else if (ret == CreateChapterSuccessValue.Created ||
                    ret == CreateChapterSuccessValue.AlreadyExisted && File.Exists(mHoldFile))
                {
                    NotifyUser();
                }
                else
                {
                    C.WriteLine("No new chapter file created and mHoldFile does not exist. No need to notify. Concat.");
                }

                success = FindAndConcatChapterFiles();
                if (success != CreateChapterSuccessValue.Failed)
                {
                    if (!File.Exists(mOutFile) || new FileInfo(mOutFile).Length == 0)
                    {
                        Print(string.Format("Outfile {0} was not created", mOutFile));
                        success = CreateChapterSuccessValue.Failed;
                    }
                    else
                    {
                        CleanUpFiles();
                    }
                }

            }
            finally
            {
                if (success == CreateChapterSuccessValue.Failed)
                {
                    Print(string.Format("Creating {0} failed", mOutFile));
                }
                else if (success == CreateChapterSuccessValue.Created)
                {
                    Print(string.Format("Successfully created {0}", mOutFile));
                    ThreadPool.QueueUserWorkItem(new WaitCallback(Beep), 1);
                }
                else if (success == CreateChapterSuccessValue.AlreadyExisted)
                    Print(string.Format("Already existed {0}", mOutFile));
            }
        }

        /// <summary>
        /// Cleanup all part files and related files possibly left over from last (unsuccessful) run of ComCrop.
        /// </summary>
        private void CleanUpPartFiles()
        {
            string fileChapters = string.Format("{0}.part-{1:00}{2}", mBaseName, "*", mChapterExt);
            string fileCheckChapters = string.Format("{0}.created-{1}-lock", mBaseName, "*");
            var chapters = Directory.GetFiles(".", fileChapters);
            foreach (var chapter in chapters)
            {
                // Note: If file does not exist, no error is raised by File.Delete()
                File.Delete(chapter);
            }
            var checkFiles = Directory.GetFiles(".", fileCheckChapters);
            foreach (var checkFile in checkFiles)
            {
                File.Delete(checkFile);
            }
            File.Delete(mHoldFile);
        }
        private void CleanUpFiles()
        {
            CleanUpPartFiles();
            File.Delete(mFileEdl);
            var file = string.Format("{0}.logo.txt", mBaseName);
            File.Delete(file);
            file = string.Format("{0}.txt", mBaseName);
            File.Delete(file);
            file = string.Format("{0}.log", mBaseName);
            File.Delete(file);
            File.Delete(mCreatingInProgressOutFile);
        }

        private CreateChapterSuccessValue FindAndConcatChapterFiles()
        {
            Print("Concatenating and compressing chapters to video file without commercials...");
            CreateFile(mCreatingInProgressOutFile);
            string fileChapters = string.Format("{0}.part-{1:00}{2}", mBaseName, "*", mChapterExt);
            var chapterFiles = Directory.GetFiles(".", fileChapters).OrderBy(f => f);
            var chaptersString = String.Join("|", chapterFiles);
            //var tempFile = mBaseName + ".temp" + Path.GetExtension(mBaseName);
            //var param = string.Format(
            //               "-hide_banner -loglevel error -nostdin -i \"concat:{0}\" -c copy -map_metadata 0 -y \"{1}\"",
            //               chaptersString, tempFile);
            string stats = showStats ? "-stats " : "";
            // max_muxing_queue_size necessary as workaround for https://trac.ffmpeg.org/ticket/6375

            int ret;
            if (mChapterExt == ".mp4")
            {

                // concatenating mp4 is not possible with concat command from above
                // https://stackoverflow.com/questions/7333232/how-to-concatenate-two-mp4-files-using-ffmpeg/11175851#11175851

                // use instead: ffmpeg -safe 0 -f concat -i list.txt -c copy output.mp4
                // with list.txt: (echo file 'first file.mp4' & echo file 'second file.mp4' )>list.txt

                var listFile = mInFile + ".list";
                File.WriteAllLines(listFile, chapterFiles.Select(f => $"file '{f}'"));
                var videoCodec = "copy";
                var audioCodec = "copy";
                var param = $"-hide_banner -loglevel error {stats}-safe 0 -f concat -i \"{listFile}\" "
                    + $"-c:v {videoCodec} -c:a {audioCodec} -map_metadata 0 -max_muxing_queue_size 1000 -y \"{mOutFile}\"";
                ret = RunCommand(mSettings.PathFfmpegExe, param);
                File.Delete(listFile);
            }
            else
            {
                var videoCodec = "libx264";
                var audioCodec = "libmp3lame -b:a 128k";
                var param = $"-hide_banner -loglevel error {stats}-nostdin -i \"concat:{chaptersString}\" "
                    + $"-map 0:v -map 0:a -c:v {videoCodec} -c:a {audioCodec} -map_metadata 0 -max_muxing_queue_size 1000 -y \"{mOutFile}\"";
                ret = RunCommand(mSettings.PathFfmpegExe, param);
            }
            //-map 0:v -map 0:a -c:v libx264 -c:a libmp3lame -b:a 128k  "$outfile"
            if (ret != 0)
            {
                C.WriteLine(string.Format("Creating output file {0} failed Ret={1}. Abort.", mOutFile, ret));
                return CreateChapterSuccessValue.Failed;
            }
            return CreateChapterSuccessValue.Created;
        }

        private void CreateFile(string emptyNewFile)
        {
            using (var fs = File.CreateText(emptyNewFile)) { }
        }

        private void Beep(object repeatObj)
        {
            try
            {
                int? repeat = repeatObj as int?;
                for (int i = 0; i < repeat; i++)
                {
                    Console.Beep(600, 333);
                    Console.Beep(800, 333);
                    Console.Beep(1000, 333);
                }
            }
            catch (System.PlatformNotSupportedException)
            {
                // cannot do anything here. ignore exception.
            }
        }

        private void NotifyUser()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(Beep), 3);
            CreateFile(mHoldFile);


            Print(string.Format("Check if all part files are wanted." + Environment.NewLine +
                "Delete unnecessary \"{0}.part-X" + mChapterExt + "\" files, " + Environment.NewLine +
                "then delete file {1}." + Environment.NewLine +
                $"Working dir: {Directory.GetCurrentDirectory()}", mBaseName, mHoldFile));

            while (File.Exists(mHoldFile))
            {
                Thread.Sleep(5000);
            }

            //Print(string.Format("Check if all part files are wanted." +
            //    " Delete unnecessary \"{0}.part-X.ts\" files, then press enter.", mBaseName));
            //int key = Console.Read();
        }

        private CreateChapterSuccessValue CreateChapterFiles()
        {
            Print("Creating video file for each non-commercial block...");
            int lineCount = 0;
            int chapterCount = 0;
            float timeStart = 0;
            bool anyCreated = false;

            // Check if all chapter files have been created previously:
            bool allCreated = true;
            int numberOfExpectedChapters = File.ReadLines(mFileEdl).Count();
            if (mOptions.CreateChaptersForCommercials)
                // if creating chapters also for commercials, number is doubled
                numberOfExpectedChapters *= 2;
            // last chapter (last row till end of file)
            numberOfExpectedChapters++;

            for (int i = 1; i <= numberOfExpectedChapters; i++)
            {
                string fileCheckChapter = string.Format("{0}.created-{1}-lock", mBaseName, i);
                if (!File.Exists(fileCheckChapter))
                {
                    allCreated = false;
                    break;
                }
            }
            if (allCreated)
            {
                Print("All blocks already exist. Skip.");
                return CreateChapterSuccessValue.AlreadyExisted;
            }
            foreach (var line in File.ReadLines(mFileEdl))
            {
                lineCount++;
                var parts = line.Split();
                if (parts.Length != 3)
                {
                    C.WriteLine(string.Format("Error on line {0} in EDL file {1}. edl_skip_field=3 in comskip.ini must not be set! Abort.", lineCount, mFileEdl));
                    return CreateChapterSuccessValue.Failed;
                }
                float timeEnd = float.Parse(parts[0], CultureInfo.InvariantCulture);
                float timeNextStart = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float duration = timeEnd - timeStart;
                if (duration > 0)
                {
                    chapterCount++;
                    var ret = CreateChapterFile(mBaseName, chapterCount, timeStart, duration);
                    if (ret == CreateChapterSuccessValue.Failed)
                        return CreateChapterSuccessValue.Failed;
                    if (ret == CreateChapterSuccessValue.Created)
                        anyCreated = true;
                }
                if (mOptions.CreateChaptersForCommercials)
                {
                    // create chapter for following commerical
                    timeStart = timeEnd;
                    duration = timeNextStart - timeEnd;
                    if (duration > 0)
                    {
                        chapterCount++;
                        var ret = CreateChapterFile(mBaseName, chapterCount, timeStart, duration);
                        if (ret == CreateChapterSuccessValue.Failed)
                            return CreateChapterSuccessValue.Failed;
                        if (ret == CreateChapterSuccessValue.Created)
                            anyCreated = true;
                    }
                }
                timeStart = timeNextStart;
            }

            // Create last chapter (end of last commercial to end of file)
            chapterCount++;
            var ret2 = CreateChapterFile(mBaseName, chapterCount, timeStart, -1);
            if (ret2 == CreateChapterSuccessValue.Failed)
                return CreateChapterSuccessValue.Failed;
            if (ret2 == CreateChapterSuccessValue.Created)
                anyCreated = true;

            if (anyCreated)
                return CreateChapterSuccessValue.Created;
            else
                return CreateChapterSuccessValue.AlreadyExisted;
        }
        enum CreateChapterSuccessValue { Created, AlreadyExisted, Failed, NotRelevantSkip };
        private CreateChapterSuccessValue CreateChapterFile(string mBaseName, int chapterCount, float timeStart, float duration)
        {
            string fileChapter = string.Format("{0}.part-{1:00}{2}", mBaseName, chapterCount, mChapterExt);
            // fileCheckChapter will be created after fileChapter has been created successfully.
            string fileCheckChapter = string.Format("{0}.created-{1}-lock", mBaseName, chapterCount);
            if (File.Exists(fileChapter) && new FileInfo(fileChapter).Length > 0 && File.Exists(fileCheckChapter))
            {
                C.WriteLine(string.Format("Skip creating chapter file {0} because it already exists. Continue.", fileChapter));
                return CreateChapterSuccessValue.AlreadyExisted;
            }
            else if ((File.Exists(mHoldFile) || File.Exists(mCreatingInProgressOutFile)) && File.Exists(fileCheckChapter))
            {
                // If (mHoldFile or mCreatingInProgressOutFile) and fileCheckChapter exist, 
                // chapter file has been successfully created on last run of ComCrop.
                C.WriteLine(string.Format("Skip creating chapter file {0} because it had been created before. Continue.", fileChapter));
                return CreateChapterSuccessValue.AlreadyExisted;
            }
            else
            {
                string durationString = "";
                if (duration > -1)
                {
                    durationString = string.Format("-t \"{0}\" ", duration.ToString(CultureInfo.InvariantCulture));
                }
                string stats = showStats ? "-stats " : "";
                int ret = RunCommand(mSettings.PathFfmpegExe, string.Format(
                    "-hide_banner -loglevel error " + stats + "-nostdin -i \"{0}\" -ss \"{1}\" {2}-c copy -y \"{3}\"",
                    mInFile, timeStart.ToString(CultureInfo.InvariantCulture), durationString, fileChapter));
                if (ret != 0)
                {
                    C.WriteLine(string.Format("Creating chapter file {0} failed Ret={1}. Abort.", fileChapter, ret));
                    return CreateChapterSuccessValue.Failed;
                }
                CreateFile(fileCheckChapter);
            }
            return CreateChapterSuccessValue.Created;
        }

        private bool CreateEdlFile()
        {
            if (File.Exists(mFileEdl) && new FileInfo(mFileEdl).Length > 0)
            {
                Debug.WriteLine(string.Format("EDL file {0} exists.", mFileEdl));
            }
            else
            {
                Debug.WriteLine(string.Format("EDL file missing or empty {0}. Create.", mFileEdl));
                Print("Scanning video, find commercials...");
                int ret = RunCommand(mSettings.PathComskipExe, string.Format("-q --ini=\"{0}\" \"{1}\"", mSettings.PathComskipIni, mInFile));
                // return value of comskip is not reliable (we have seen so far: Windows: 1==success, Linux: 0==success)
                if (!File.Exists(mFileEdl))
                {
                    C.WriteLine(string.Format("Creating EDL file failed. output_edl=1 set in comskip.ini? Ret={0}. Abort.", ret));
                    return false;
                }
                // Remove existing part files.
                CleanUpPartFiles();
            }
            return true;
        }
    }
}
