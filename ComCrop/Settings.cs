using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ComCrop
{
    class Settings
    {
        [SettingAttribute(Comment = "Absolute path to ffmpeg executable")]
        public string PathFfmpegExe { get; set; } = @"d:\Bin\video\ffmpeg\ffmpeg.exe";
        [SettingAttribute(Comment = "Absolute path to comskip executable")]
        public string PathComskipExe { get; set; } = @"d:\Bin\video\comskip\comskip.exe";
        [SettingAttribute(Comment = "Absolute path to settings file for comskip. Must produce 3-column EDL file. output_edl=1 but no edl_skip_field=3")]
        public string PathComskipIni { get; set; } = @"d:\Bin\video\comskip\comskip.ini";
        [SettingAttribute]
        public string ExtensionDestination { get; set; } = @"mp4";
        [SettingAttribute(Comment = "Leave empty to allow multiple instances of ComCrop to run in parallel. If relative, lock file is placed next to executable; use absolute path to allow only one instance system-wide.")]
        public string LockFile { get; set; } = @"comcrop.lock";
        [SettingAttribute(Comment = "Create chapter files for commercials (need to be deleted manually)")]
        public bool CreateChaptersForCommercials { get; set; } = true;

        public Dictionary<string, string> StringSettings { get; private set; }
        public Dictionary<string, int> IntSettings { get; private set; }

        /// <summary>
        /// Set if debug output is needed
        /// </summary>
        public TextWriter Debug { get; internal set; } = null;
        public TextWriter Console { get; internal set; }


        /// <summary>
        /// Reads file <paramref name="settingsFile"/> with format: NAME=VALUE
        /// and stores all values in the string property with matching name (if there is any).
        /// If settings file does not exist, it is created using the default (hard-coded) configuration.
        /// <para/>
        /// E.g. PathFfmpeg=c:\ffmpeg.exe store string "c:\\ffmpeg.exe" into <see cref="PathFfmpeg"/>
        /// <para/>
        /// Note: Name matching is case invariant.
        /// <para/>
        /// Note2: Only types string and int are supported.
        /// </summary>
        /// <param name="settingsFile">Name of settings file (without path!)</param>
        internal void Read(string settingsFile, bool quiet)
        {
            string programLocation = new FileInfo(System.Reflection.Assembly.GetEntryAssembly().Location).Directory.FullName;
            ReadFromPath(Path.Combine(programLocation, settingsFile), quiet);
        }
        private void ReadFromPath(string absPathSettingsFile, bool quiet)
        {
            StringSettings = new Dictionary<string, string>();
            IntSettings = new Dictionary<string, int>();

            Dictionary<string, PropertyInfo> props = GetPropertyDictionary();
            if (!File.Exists(absPathSettingsFile))
            {
                using (StreamWriter file = new StreamWriter(absPathSettingsFile))
                {
                    Debug?.WriteLine(string.Format("File {0} does not exist. Create with default values.", absPathSettingsFile));
                    foreach (var prop in props)
                    {
                        SettingAttribute attr = prop.Value.GetCustomAttribute(typeof(SettingAttribute)) as SettingAttribute;
                        if (attr == null)
                            throw new ArgumentNullException();
                        if(!string.IsNullOrWhiteSpace(attr.Comment))
                            file.WriteLine(string.Format("# {0}", attr.Comment));
                        string name = prop.Key;
                        object valueObject = GetValueFromProp(prop.Value);
                        string value = valueObject.ToString();
                        file.WriteLine(string.Format("{0}={1}", name, value));
                    }
                }
            }
            if(!quiet)
                Debug?.WriteLine(string.Format("Reading settings from file {0}...", absPathSettingsFile));
            using (StreamReader reader = File.OpenText(absPathSettingsFile))
            {
                string line;
                int lineCount = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineCount++;
                    string trim = line.TrimStart();
                    if (trim.StartsWith(';') || trim.StartsWith('#') || trim.StartsWith("//"))
                        continue;
                    int posEq = line.IndexOf('=');
                    if (posEq > 0 && line.Length > posEq)
                    {
                        string name = line.Substring(0, posEq);
                        string value = line.Substring(posEq + 1);
                        if (props.TryGetValue(name, out PropertyInfo prop))
                        {
                            object propValue = GetValueFromProp(prop);
                            if (propValue is string)
                            {
                                prop.SetValue(this, value, null);
                                propValue = value;
                                StringSettings.Add(name, value);
                            }
                            else if (propValue is int)
                            {
                                if (int.TryParse(value, out int intValue))
                                {
                                    propValue = intValue;
                                    prop.SetValue(this, intValue, null);
                                    IntSettings.Add(name, intValue);
                                }
                            }
                            else
                            {
                                Debug?.WriteLine(string.Format("Unsupprted type of setting \"{0}\" on line {1}", name, lineCount));
                            }
                        }
                        else
                        {
                            Debug?.WriteLine(string.Format("Unknown setting \"{0}\" on line {1}", name, lineCount));
                        }
                    }
                    else
                    {
                        Debug?.WriteLine(string.Format("Bad syntax on line {0}", lineCount));
                    }
                }
            }
        }

        /// <summary>
        /// Reads all properties using reflection and returns them as dictionary where the dictionary key is the lower-cased property name.
        /// </summary>
        /// <returns></returns>
        private  Dictionary<string, PropertyInfo> GetPropertyDictionary()
        {
            Type t = this.GetType();
            PropertyInfo[] props = t.GetProperties();
            Dictionary<string, PropertyInfo> dict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo prp in props)
            {
                /// Only considers properties marked as <see cref="SettingAttribute"/>
                if (prp.GetCustomAttribute(typeof(SettingAttribute)) != null){
                    //object value = prp.GetValue(this, new object[] { });
                    //var value = GetValueFromProp(prp);
                    dict.Add(prp.Name, prp);                    
                }
            }
            return dict;
        }

        private object GetValueFromProp(PropertyInfo prp)
        {
            return prp.GetValue(this, new object[] { }); 
        }

        /// <summary>
        /// Properties with this attribute are considered settings. They are read from the settings file and written to the settings template file.
        /// </summary>
        private class SettingAttribute : Attribute
        {
            public string Comment { get; set; }
        }
    }
}
