using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Text;

namespace PauloMorgado.Tools.SazToHar;

internal static  class HttpUtilities
{
    // byte types don't have a data type annotation so we pre-cast them; to avoid in-place casts
    public const byte ByteCR = (byte)'\r';
    public const byte ByteLF = (byte)'\n';
    public const byte ByteColon = (byte)':';
    public const byte ByteEquals = (byte)'=';
    public const byte ByteSemicolon = (byte)';';
    public const byte ByteSpace = (byte)' ';
    public const byte ByteTab = (byte)'\t';
    public const byte ByteQuestionMark = (byte)'?';
    public const byte BytePercentage = (byte)'%';

    public static readonly byte[] whiteSpaceBytes = new byte[] { ByteSpace, ByteTab, };
    public static readonly byte[] singleLineBreakBytes = new byte[] { ByteCR, ByteLF, };
    public static readonly byte[] doubleLineBreakBytes = new byte[] { ByteCR, ByteLF, ByteCR, ByteLF, };
    public static readonly byte[] connectAsciiBytes = Encoding.ASCII.GetBytes("connect");
    public static readonly byte[] cookieAsciiBytes = Encoding.ASCII.GetBytes("cookie");
    public static readonly byte[] setCookieAsciiBytes = Encoding.ASCII.GetBytes("set-cookie");
    public static readonly byte[] contentTypeAsciiBytes = Encoding.ASCII.GetBytes("content-type");
    public static readonly byte[] transferEncodignAsciiBytes = Encoding.ASCII.GetBytes("transfer-encoding");
    public static readonly byte[] contentEncodignAsciiBytes = Encoding.ASCII.GetBytes("content-encoding");
    public static readonly byte[] chunkedAsciiBytes = Encoding.ASCII.GetBytes("chunked");
    public static readonly byte[] gzipAsciiBytes = Encoding.ASCII.GetBytes("gzip");
    public static readonly byte[] deflateAsciiBytes = Encoding.ASCII.GetBytes("deflate");
    public static readonly byte[] brotliAsciiBytes = Encoding.ASCII.GetBytes("brotli");
    public static readonly byte[] textContentTypePreffixAsciiBytes = Encoding.ASCII.GetBytes("text/");
    public static readonly byte[] applicationJsonContentTypePreffixAsciiBytes = Encoding.ASCII.GetBytes("application/json");
    public static readonly byte[] applicationJsonStreamContentTypePreffixAsciiBytes = Encoding.ASCII.GetBytes("application/x-json-stream");
    public static readonly byte[] applicationXmlContentTypePreffixAsciiBytes = Encoding.ASCII.GetBytes("application/xml");
    public static readonly byte[] applicationFormEncodedContentTypePreffixAsciiBytes = Encoding.ASCII.GetBytes("application/x-www-form-urlencoded");
    public static readonly byte[] charSetContentTypeEncodingPreffixAsciiBytes = Encoding.ASCII.GetBytes(";charset=");
    public static readonly byte[] utf8EncodingNameAsciiBytes = Encoding.ASCII.GetBytes(Encoding.UTF8.WebName);

    private static readonly char[] HexDigits = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', };

    public static unsafe string GetAsciiStringEscaped(this ReadOnlySpan<byte> span, int maxChars = 128)
    {
        var sourceLength = span.Length;
        var appendEllipsis = sourceLength > maxChars;
        var destinationLength = sourceLength;

        if (appendEllipsis)
        {
            sourceLength = maxChars;
            destinationLength = sourceLength + 3;
        }
        else
        {
            destinationLength = sourceLength;
        }

        for (var i = 0; i < sourceLength; i++)
        {
            var b = span[i];
            if (b < 0x20 || b >= 0x7F)
            {
                destinationLength += 3;
            }
        }

        fixed (byte* source = &MemoryMarshal.GetReference(span))
        {
            return string.Create(
                destinationLength,
                ((IntPtr)source, appendEllipsis),
                static (Span<char> chars, (IntPtr source, bool appendEllipsis) state) =>
                {
                    var bytes = (byte*)state.source;
                    var length = chars.Length - (state.appendEllipsis ? 3 : 0);
                    var d = 0;
                    while (d < length)
                    {
                        var b = *bytes++;

                        if (b < 0x20 || b >= 0x7F)
                        {
                            chars[d++] = '\\';
                            chars[d++] = 'x';
                            chars[d++] = HexDigits[b >> 4];
                            chars[d++] = HexDigits[b & 0b1111];
                        }
                        else
                        {
                            chars[d++] = (char)b;
                        }
                    }

                    if (state.appendEllipsis)
                    {
                        chars[d++] = '.';
                        chars[d++] = '.';
                        chars[d++] = '.';
                    }
                });
        }
    }

    public static unsafe string GetAsciiString(this ReadOnlySpan<byte> span, bool toLower = false)
    {
        fixed (byte* source = &MemoryMarshal.GetReference(span))
        {
            return string.Create(span.Length, (IntPtr)source, toLower ? GetLower : Get);
        }

        static void Get(Span<char> chars, IntPtr source)
        {
            var bytes = (byte*)source;
            var length = chars.Length;
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)*bytes;
                bytes++;
            }
        }

        static void GetLower(Span<char> chars, IntPtr source)
        {
            var bytes = (byte*)source;
            var length = chars.Length;
            for (var i = 0; i < length; i++)
            {
                var b = *bytes;
                chars[i] = (b >= 65 && b <= 90) ? (char)(b | 32) : (char)b;
                bytes++;
            }
        }

    }

    public static int ParseStatusCode(this ReadOnlySpan<byte> value)
    {
        var statusCode = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var b = value[i] - 48;

            if (b < 0 || b > 9)
            {
                throw new InvalidDataException($"Invalid status code value: {GetAsciiStringEscaped(value)}");
            }

            var newValue = statusCode * 10 + (int)b;

            if (newValue < statusCode)
            {
                throw new InvalidDataException($"Invalid status code value: {GetAsciiStringEscaped(value)}");
            }

            statusCode = newValue;
        }

        return statusCode;
    }

    public static ReadOnlySpan<char> UnescapeDataString(this ReadOnlySpan<char> chars)
    {
        if (chars.Length < 16 && chars.IndexOfAny('%', '+') < 0)
        {
            return chars;
        }

        var result = new char[chars.Length];
        var length = 0;

        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];

            if (ch == '%' && i < chars.Length - 2)
            {
                var h1 = HexDigit(chars[i + 1]);
                var h2 = HexDigit(chars[i + 2]);

                if (h1 <= 0xF || h2 <= 0xF)
                {
                    result[length++] = (char)((h1 << 4) + h2);
                    i += 2;
                    break;
                }
            }
            else
            {
                result[length++] = ch;
            }
        }

        return result.AsSpan(0, length);

        static int HexDigit(char b)
            => b switch
            {
                >= '0' and <= '9' => (b - '0'),
                >= 'A' and <= 'F' => (b - 'A' + 10),
                >= 'a' and <= 'f' => (b - 'a' + 10),
                _ => 0xff
            };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> ReplacePlusWithSpace(this ReadOnlySpan<char> input)
        => input.IndexOf('+') < 0 ? input : ReplacePlusWithSpaceCore(input);

    private static unsafe ReadOnlySpan<char> ReplacePlusWithSpaceCore(ReadOnlySpan<char> input_)
    {
        var output_ = new char[input_.Length].AsSpan();
        fixed (char* inputPtr = &MemoryMarshal.GetReference(input_))
        {
            fixed (char* outputPtr = &MemoryMarshal.GetReference(output_))
            {
                var input = (ushort*)inputPtr;
                var output = (ushort*)outputPtr;

                var i = (nint)0;
                var n = (nint)(uint)output_.Length;

                if (Sse41.IsSupported && n >= Vector128<ushort>.Count)
                {
                    var vecPlus = Vector128.Create((ushort)'+');
                    var vecSpace = Vector128.Create((ushort)' ');

                    do
                    {
                        var vec = Sse2.LoadVector128(input + i);
                        var mask = Sse2.CompareEqual(vec, vecPlus);
                        var res = Sse41.BlendVariable(vec, vecSpace, mask);
                        Sse2.Store(output + i, res);
                        i += Vector128<ushort>.Count;
                    } while (i <= n - Vector128<ushort>.Count);
                }

                for (; i < n; ++i)
                {
                    if (input[i] != '+')
                    {
                        output[i] = input[i];
                    }
                    else
                    {
                        output[i] = ' ';
                    }
                }
            }
        }

        return output_;
    }
}
