using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MHLab.Patch.Core.Versioning;

namespace MHLab.Patch.Core
{
    public sealed class BuildsIndex
    {
        public List<IVersion> AvailableBuilds { get; set; }

        public IVersion GetLast()
        {
            if (AvailableBuilds == null || AvailableBuilds.Count == 0)
                return null;

            return AvailableBuilds.Last();
        }

        public IVersion GetFirst()
        {
            if (AvailableBuilds == null || AvailableBuilds.Count == 0)
                return null;

            return AvailableBuilds.First();
        }

        public bool Contains(IVersion version)
        {
            for (int i = 0; i < AvailableBuilds.Count; i++)
            {
                var current = AvailableBuilds[i];
                if (current.Equals(version)) return true;
            }

            return false;
        }
    }

    public sealed class BuildDefinitionEntry
    {
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public DateTime LastWriting { get; set; }
        public string Hash { get; set; }
        public FileAttributes Attributes { get; set; }
    }

    public sealed class BuildDefinition
    {
        public BuildDefinitionEntry[] Entries { get; set; }
    }
}
