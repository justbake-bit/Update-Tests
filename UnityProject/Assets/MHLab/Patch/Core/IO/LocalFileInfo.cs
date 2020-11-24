using System;
using System.IO;

namespace MHLab.Patch.Core.IO
{
    public sealed class LocalFileInfo
    {
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public DateTime LastWriting { get; set; }
        public FileAttributes Attributes { get; set; }
    }
}
