using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace PauloMorgado.Tools.SazToHar;

internal readonly struct CookiesEnumerable
{
    private readonly ReadOnlyMemory<byte> cookie;

    public CookiesEnumerable(ReadOnlyMemory<byte> cookie)
    {
        this.cookie = cookie;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this.cookie);
    }

    public readonly struct EncodedNameValuePair
    {
        public readonly ReadOnlyMemory<byte> EncodedName { get; }

        public readonly ReadOnlyMemory<byte> EncodedValue { get; }

        internal EncodedNameValuePair(ReadOnlyMemory<byte> encodedName, ReadOnlyMemory<byte> encodedValue)
        {
            this.EncodedName = encodedName;
            this.EncodedValue = encodedValue;
        }

        public ReadOnlySpan<char> DecodeName() => Decode(this.EncodedName);

        public ReadOnlySpan<char> DecodeValue() => Decode(this.EncodedValue);

        private static ReadOnlySpan<char> Decode(ReadOnlyMemory<byte> bytes) => Encoding.ASCII.GetString(bytes.Span).AsSpan().UnescapeDataString();
    }

    public struct Enumerator
    {
        private ReadOnlyMemory<byte> cookies;

        internal Enumerator(ReadOnlyMemory<byte> cookie)
        {
            this.Current = default;
            this.cookies = cookie.IsEmpty || cookie.Span[0] != HttpUtilities.ByteQuestionMark
                ? cookie
                : cookie.Slice(1);
        }

        public EncodedNameValuePair Current { get; private set; }

        public bool MoveNext()
        {
            while (!this.cookies.IsEmpty)
            {
                // Chomp off the next segment
                ReadOnlyMemory<byte> segment;
                var delimiterIndex = this.cookies.Span.IndexOf(HttpUtilities.ByteSemicolon);
                if (delimiterIndex >= 0)
                {
                    segment = this.cookies.Slice(0, delimiterIndex);
                    this.cookies = this.cookies.Slice(delimiterIndex + 1);
                }
                else
                {
                    segment = this.cookies;
                    this.cookies = default;
                }

                // If it's nonempty, emit it
                var equalIndex = segment.Span.IndexOf((byte)'=');
                if (equalIndex >= 0)
                {
                    this.Current = new EncodedNameValuePair(
                        segment.Slice(0, equalIndex),
                        segment.Slice(equalIndex + 1));
                    return true;
                }
                else if (!segment.IsEmpty)
                {
                    this.Current = new EncodedNameValuePair(segment, default);
                    return true;
                }
            }

            this.Current = default;
            return false;
        }
    }
}
