using System;
using System.Collections.Generic;
using System.Text;

namespace ComCrop
{
    public class ComCropOptions
    {
        /// <summary>
        /// File to be cropped
        /// </summary>
        public string InFile { get; set; }

        public string ExtensionDestination { get; set; }
        
        /// <summary>
        /// Set to true to disable waiting for user to check chapter files. Usefull for batch processing. Chapter files for all input files will be created.
        /// </summary>
        public bool NoNotify { get; internal set; }
        
        /// <summary>
        /// Set to true when user requests to terminte (e.g. by pressing CTRL+C)
        /// </summary>
        public bool AbortRequested { get; set; }

        /// <summary>
        /// Create chapter files for commercials (need to be deleted manually)
        /// </summary>
        public bool CreateChaptersForCommercials{ get; set; }
    }
}
