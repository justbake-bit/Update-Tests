using System;
using System.Collections.Generic;
using System.IO;
using MHLab.Patch.Core.Versioning;

namespace MHLab.Patch.Core
{
    public sealed class PatchIndex
    {
        public List<PatchIndexEntry> Patches { get; set; }
    }

    public sealed class PatchIndexEntry
    {
        public IVersion From { get; set; }
        public IVersion To { get; set; }
    }

    public sealed class PatchDefinitionEntry
    {
        public PatchOperation Operation { get; set; }
        public string RelativePath { get; set; }
        public FileAttributes Attributes { get; set; }
        public DateTime LastWriting { get; set; }
        public long Size { get; set; }
    }

    public sealed class PatchDefinition
    {
        public IVersion From { get; set; }
        public IVersion To { get; set; }
        public string Hash { get; set; }
        public long TotalSize { get; set; }
        public List<PatchDefinitionEntry> Entries { get; set; }
    }
}
