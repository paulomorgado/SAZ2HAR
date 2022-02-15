using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PauloMorgado.Tools.SazToHar;

internal static class MemoryExtensions
{
    public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream memoryStream)
        => memoryStream.AsReadOnlyMemory(0, (int)(memoryStream.Length));

    public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream memoryStream, int start, int length)
        => new ReadOnlyMemory<byte>(memoryStream.GetBuffer(), start, length);

    public static bool TryReadTo(ref this ReadOnlyMemory<byte> memory, out ReadOnlySpan<byte> span, byte delimiter, bool advancePastDelimiter = true, bool treatEndAsDelimiter = true)
    {
        span = memory.Span;
        var index = span.IndexOf(delimiter);

        if (index < 0)
        {
            if (treatEndAsDelimiter)
            {
                return true;
            }

            span = default;
            return false;
        }
        else
        {
            span = index == 0 ? default : span.Slice(0, index);
            memory = memory[(index + (advancePastDelimiter ? 1 : 0))..];
            return true;
        }
    }

    public static bool TryReadTo(ref this ReadOnlyMemory<byte> memory, out ReadOnlySpan<byte> span, ReadOnlySpan<byte> delimiter, bool advancePastDelimiter = true, bool treatEndAsDelimiter = true)
    {
        span = memory.Span;
        var index = span.IndexOf(delimiter);

        if (index < 0)
        {
            if (treatEndAsDelimiter)
            {
                return true;
            }

            span = default;
            return false;
        }
        else
        {
            span = index == 0 ? default : span.Slice(0, index);
            memory = memory[(index + (advancePastDelimiter ? delimiter.Length : 0))..];
            return true;
        }
    }

    public static bool TryReadTo(ref this ReadOnlySpan<byte> memory, out ReadOnlySpan<byte> span, byte delimiter, bool advancePastDelimiter = true, bool treatEndAsDelimiter = true)
    {
        span = memory;
        var index = span.IndexOf(delimiter);

        if (index < 0)
        {
            if (treatEndAsDelimiter)
            {
                return true;
            }

            span = default;
            return false;
        }
        else
        {
            span = index == 0 ? default : span.Slice(0, index);
            memory = memory[(index + (advancePastDelimiter ? 1 : 0))..];
            return true;
        }
    }

    public static bool AsciiStartsWith(this ReadOnlySpan<byte> source, ReadOnlySpan<byte> value, IEqualityComparer<byte>? comparer = null)
        => (source.Length >= value.Length) && source[..(value.Length)].SequenceEqual(value, comparer);

    public static int AsciiIndexOf(this ReadOnlySpan<byte> source, ReadOnlySpan<byte> value, IEqualityComparer<byte>? comparer = null)
    {
        if (comparer is null)
        {
            return source.IndexOf(value);
        }

        if (source.Length < value.Length)
        {
            return -1;
        }

        var index = 0;
        var end = source.Length - value.Length;
        while ((index < end) && !source[index..(index + value.Length)].SequenceEqual(value, comparer))
        {
            index++;
        }

        return (index < end) ? index : -1;
    }

    public static ReadOnlySpan<byte> ToLowerInvariant(this ReadOnlySpan<byte> bytes)
    {
        byte[]? buffer = null;

        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];

            if (b >= 65 && b <= 90)
            {
                if (buffer is null)
                {
                    buffer = bytes.ToArray();
                }

                buffer[i] = (byte)(b | 32);
            }
        }

        return buffer is null ? bytes : buffer.AsSpan();
    }

    public static ReadOnlySpan<char> ToLowerInvariant(this ReadOnlySpan<char> source)
    {
        char[]? buffer = null;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != char.ToLowerInvariant(source[i]))
            {
                if (buffer is null)
                {
                    buffer = source.ToArray();
                }

                buffer[i] = char.ToLowerInvariant(source[i]);
            }
        }

        return buffer is null ? source : buffer.AsSpan();
    }
}
