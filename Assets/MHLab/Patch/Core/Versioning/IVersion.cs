using System;
using System.Runtime.CompilerServices;

namespace MHLab.Patch.Core.Versioning
{
    public interface IVersion : IComparable<IVersion>, IEquatable<IVersion>
    {
        int Major { get; set; }
        int Minor { get; set; }
        int Patch { get; set; }

        string ToString();

        void UpdatePatch();
        void UpdateMinor();
        void UpdateMajor();

        bool IsLower(IVersion compare);
        bool IsHigher(IVersion version);
    }
}
