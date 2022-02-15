using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PauloMorgado.Tools.SazToHar
{
    internal class CaseInsensitiveAsciiByteEqualityComparer : IEqualityComparer<byte>
    {
        public static readonly CaseInsensitiveAsciiByteEqualityComparer Instance = new();

        public bool Equals(byte x, byte y) => ToLower(x).Equals(ToLower(y));

        public int GetHashCode([DisallowNull] byte obj) => ToLower(obj).GetHashCode();

        private static byte ToLower(byte b) => (byte)((b is >= (byte)'A' and <= (byte)'Z') ? (b | 32) : b);
    }
}
