using System;
using System.Runtime.CompilerServices;

namespace MHLab.Patch.Core.Versioning
{
    [Serializable]
    public sealed class Version : IVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }

        public Version(int major, int minor, int patch)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));

            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public Version(string version)
        {
            var v = Parse(version);
            Major = v.Major;
            Minor = v.Minor;
            Patch = v.Patch;
        }

        public Version()
        {
            Major = 0;
            Minor = 1;
            Patch = 0;
        }

        public Version(IVersion v)
        {
            if (v != null)
            {
                Major = v.Major;
                Minor = v.Minor;
                Patch = v.Patch;
            }
            else
            {
                Major = 0;
                Minor = 1;
                Patch = 0;
            }
        }
        
        public int CompareTo(IVersion value)
        {
            return
                object.ReferenceEquals(value, this) ? 0 :
                value is null ? 1 :
                Major != value.Major ? (Major > value.Major ? 1 : -1) :
                Minor != value.Minor ? (Minor > value.Minor ? 1 : -1) :
                Patch != value.Patch ? (Patch > value.Patch ? 1 : -1) :
                0;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Version);
        }

        public bool Equals(IVersion obj)
        {
            return object.ReferenceEquals(obj, this) ||
                (!(obj is null) &&
                Major == obj.Major &&
                Minor == obj.Minor &&
                Patch == obj.Patch);
        }

        public override int GetHashCode()
        {
            // Let's assume that most version numbers will be pretty small and just
            // OR some lower order bits together.

            int accumulator = 0;

            accumulator |= (Major & 0x0000000F) << 20;
            accumulator |= (Minor & 0x000000FF) << 12;
            accumulator |= (Patch & 0x00000FFF);

            return accumulator;
        }

        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public void UpdatePatch()
        {
            Patch += 1;
        }

        public void UpdateMinor()
        {
            Minor += 1;
            Patch = 0;
        }

        public void UpdateMajor()
        {
            Major += 1;
            Minor = 0;
            Patch = 0;
        }

        public static Version Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentNullException(nameof(input));

            var tokens = input.Split('.');

            return new Version(int.Parse(tokens[0]), int.Parse(tokens[1]), int.Parse(tokens[2]));
        }

        public bool IsLower(IVersion version)
        {
            return CompareTo(version) < 0;
        }

        public bool IsHigher(IVersion version)
        {
            return CompareTo(version) > 0;
        }

        // Force inline as the true/false ternary takes it above ALWAYS_INLINE size even though the asm ends up smaller
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Version v1, Version v2)
        {
            // Test "right" first to allow branch elimination when inlined for null checks (== null)
            // so it can become a simple test
            if (v2 is null)
            {
                // return true/false not the test result https://github.com/dotnet/runtime/issues/4207
                return (v1 is null) ? true : false;
            }

            // Quick reference equality test prior to calling the virtual Equality
            return ReferenceEquals(v2, v1) ? true : v2.Equals(v1);
        }

        public static bool operator !=(Version v1, Version v2) => !(v1 == v2);

        public static bool operator <(Version v1, Version v2)
        {
            if (v1 is null)
            {
                return !(v2 is null);
            }

            return v1.CompareTo(v2) < 0;
        }

        public static bool operator <=(Version v1, Version v2)
        {
            if (v1 is null)
            {
                return true;
            }

            return v1.CompareTo(v2) <= 0;
        }

        public static bool operator >(Version v1, Version v2) => v2 < v1;

        public static bool operator >=(Version v1, Version v2) => v2 <= v1;
    }
}
