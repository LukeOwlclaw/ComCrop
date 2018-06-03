using System;
using System.IO;
using System.Linq;

namespace ComCrop
{
    class Program
    {
        /// <summary>
        /// Create executable via command line:
        /// dotnet publish -c Release -r win10-x64
        /// dotnet publish -c Release -r linux-x64
        /// dotnet publish -c Release -r osx.10.11-x64
        /// <para/>
        /// Requires on Linux:
        /// sudo apt-get install libunwind8
        /// </summary>
        /// <param name="videoFiles"></param>
        static void Main(string[] videoFiles)
        {
            bool waitForKey = true;
            bool quiet = false;
            FileStream lockObject2 = null;
            StreamWriter lockObject = null;
            string lockFileLocation = null;
            try
            {
                if (videoFiles.Length > 0 && videoFiles[0] == "quiet")
                {
                    quiet = true;
                    waitForKey = false;
                    videoFiles = videoFiles.Skip(1).ToArray();
                }

                string settingsFile = "comcrop.settings";
                Settings settings = new Settings
                {
                    Debug = Console.Out,
                    Console = Console.Out
                };
                settings.Read(settingsFile, quiet);
                TextWriter c = Console.Out;

                bool settingsValid = true;
                foreach (var path in settings.StringSettings)
                {
                    if (path.Key.StartsWith("Path", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (!File.Exists(path.Value))
                        {
                            c.WriteLine(string.Format("Setting {0} invalid. Path does not exist", path.Key));
                            settingsValid = false;
                        }
                    }
                }
                foreach (var extension in settings.StringSettings)
                {
                    if (extension.Key.StartsWith("Extension", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(extension.Value))
                        {
                            c.WriteLine(string.Format("Setting {0} invalid. Extension must not be empty", extension.Key));
                            settingsValid = false;
                        }
                    }
                }
                if (!settingsValid)
                {
                    c.WriteLine(string.Format("Fix settings file {0}. Abort.", settingsFile));
                    return;
                }

                ComCrop main = new ComCrop(settings);

                if (videoFiles.Length > 0 && videoFiles[0] == "nowait")
                {
                    waitForKey = false;
                    videoFiles = videoFiles.Skip(1).ToArray();
                }
                bool notifyUser = true;
                if (videoFiles.Length > 0 && videoFiles[0] == "nonotify")
                {
                    notifyUser = false;
                    videoFiles = videoFiles.Skip(1).ToArray();
                }

                if (!string.IsNullOrWhiteSpace(settings.LockFile))
                {
                    string programLocation = new FileInfo(System.Reflection.Assembly.GetEntryAssembly().Location).Directory.FullName;
                    lockFileLocation = settings.LockFile;
                    if (!Path.IsPathRooted(lockFileLocation) == true)
                    {
                        lockFileLocation = Path.Combine(programLocation, lockFileLocation);
                    }
                    if (!quiet)
                        c.WriteLine(string.Format("Opening file lock {0}...", lockFileLocation));
                    try
                    {
                        if (File.Exists(lockFileLocation))
                            throw new IOException("already exists", -111);

                        //File.Delete(lockFileLocation);
                        //lockObject = File.CreateText(lockFileLocation);
                        //lockObject.WriteLine("test");
                        //lockObject.Flush();
                        lockObject2 = new FileStream(lockFileLocation, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
                        lockObject2.WriteByte(66);
                    }
                    catch (IOException ex)
                    {
                        if (!quiet)
                            c.WriteLine(string.Format("ComCrop locked {0}. Exit.", ex.HResult));
                        return;
                    }
                }

                if (videoFiles.Length == 1 && videoFiles[0].StartsWith("*."))
                {
                    string extension = videoFiles[0].Substring(2);
                    string filter = videoFiles[0];
                    videoFiles = Directory.GetFiles(".", filter);
                    if (videoFiles.Length == 0)
                    {
                        if (!quiet)
                            c.WriteLine(string.Format("No file matching {0} found. Exit.", filter));
                        return;
                    }
                }
                else if (videoFiles.Length == 0)
                {
                    c.WriteLine(string.Format("Usage:"));
                    c.WriteLine(string.Format("ComCrop [quiet] [nowait] [nonotify] infile1 infile2 infile3 ..."));
                    c.WriteLine(string.Format("(removes commercials from all given files)"));
                    c.WriteLine(string.Format("or"));
                    c.WriteLine(string.Format("ComCrop [quiet] [nowait] [nonotify] *.ext"));
                    c.WriteLine(string.Format("(removes commercials from all files matching given file extension)"));
                    c.WriteLine(string.Format(""));
                    c.WriteLine(string.Format("Note: Output file type (and thus name) is configured in settings file."));
                    c.WriteLine(string.Format("nonotify: Only create chapter files. No waiting for user to check chapter files."));
                    c.WriteLine(string.Format("nowait: No key press on exit necessary."));
                    c.WriteLine(string.Format("quiet: No output if ComCrop is locked or no file to handle."));

                    return;
                }
                foreach (var videoFile in videoFiles)
                {
                    if (!File.Exists(videoFile))
                    {
                        if (!quiet)
                            c.WriteLine(string.Format("File {0} does not exist. Skip.", videoFile));
                        continue;
                    }
                    ComCropOptions options = new ComCropOptions
                    {
                        InFile = videoFile,
                        ExtensionDestination = settings.ExtensionDestination,
                        NoNotify = !notifyUser,
                        CreateChaptersForCommercials = settings.CreateChaptersForCommercials
                    };
                    main.CropCommercial(options);
                    if (options.AbortRequested)
                        break;
                }


            }
            finally
            {
                if (waitForKey)
                {
                    Console.WriteLine("Press enter to exit.");
                    // Also allow CTRL+C to exit application gracefully.
                    Console.TreatControlCAsInput = true;
                    Console.ReadKey();
                }
                if (lockObject != null)
                {
                    lockObject.WriteLine("test2");
                    lockObject.Flush();
                    lockObject.Dispose();
                    if (File.Exists(lockFileLocation))
                        File.Delete(lockFileLocation);
                }
                if (lockObject2 != null)
                {
                    lockObject2.WriteByte(65);
                    lockObject2.Dispose();
                    if (File.Exists(lockFileLocation))
                        File.Delete(lockFileLocation);
                }
            }
        }
    }
}
