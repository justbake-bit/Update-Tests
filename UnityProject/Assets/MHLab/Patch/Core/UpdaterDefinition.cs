using System;
using System.IO;

namespace MHLab.Patch.Core
{
    public sealed class UpdaterDefinitionEntry
    {
        public PatchOperation Operation { get; set; }
        public string RelativePath { get; set; }
        public FileAttributes Attributes { get; set; }
        public DateTime LastWriting { get; set; }
        public long Size { get; set; }
    }

    public sealed class UpdaterDefinition
    {
        public UpdaterDefinitionEntry[] Entries { get; set; }
    }
}
