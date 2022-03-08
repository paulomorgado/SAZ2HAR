using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;

namespace PauloMorgado.Tools.SazToHar;

internal static class MemoryExtensions
{
    public static Memory<byte> AsMemory(this MemoryStream memoryStream, int start)
        => memoryStream.AsMemory(start, (int)(memoryStream.Length) - start);

    public static Memory<byte> AsMemory(this MemoryStream memoryStream, int start, int length)
        => new (memoryStream.GetBuffer(), start, length);

    public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream memoryStream)
        => memoryStream.AsReadOnlyMemory(0);

    public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream memoryStream, int start)
        => memoryStream.AsReadOnlyMemory(start, (int)(memoryStream.Length) - start);

    public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream memoryStream, int start, int length)
        => new (memoryStream.GetBuffer(), start, length);

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

            if (b is >= 65 and <= 90)
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

    public static Stream AsStream(this ReadOnlyMemory<byte> memory) => new ReadOnlyMemoryOfByteAsStream(memory);

    private sealed class ReadOnlyMemoryOfByteAsStream : Stream
    {
        private readonly ReadOnlyMemory<byte> memory;
        private int position;

        public ReadOnlyMemoryOfByteAsStream(ReadOnlyMemory<byte> memory)
        {
            this.memory = memory;
        }

        public override bool CanTimeout => false;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => this.memory.Length;

        public override long Position
        {
            get => this.position;
            set => this.EnsureValidPosition(this.position);
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            var originalPosition = this.position;
            destination.Write(this.memory.Span.Slice(this.position));
            this.position = this.memory.Length;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            this.CopyTo(destination, bufferSize);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var byteCount = Math.Min(count, this.memory.Length - this.position);

            this.memory.Slice(this.position, byteCount).CopyTo(new Memory<byte>(buffer, offset, count));

            this.position += byteCount;

            return byteCount;
        }

        public override int Read(Span<byte> buffer)
        {
            var byteCount = Math.Min(buffer.Length, this.memory.Length - this.position);

            this.memory.Span.Slice(this.position, byteCount).CopyTo(buffer);

            this.position += byteCount;

            return byteCount;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            var result = this.Read(buffer, offset, count);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            return Task.FromResult(result);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            var result = new ValueTask<int>(this.Read(buffer.Span));

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            return result;
        }

        public override int ReadByte() => this.position >= this.memory.Length ? -1 : this.memory.Span[this.position++];

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    this.position = (int)this.EnsureValidPosition(offset);
                    break;
                case SeekOrigin.Current:
                    this.position = (int)this.EnsureValidPosition(this.position + offset);
                    break;
                case SeekOrigin.End:
                    ThrowNotSupportedException();
                    break;
                default:
                    ThrowNotSupportedException();
                    break;
            }

            return this.position;
        }

        public override void SetLength(long value) => ThrowNotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => ThrowNotSupportedException();

        public override void Write(ReadOnlySpan<byte> buffer) => ThrowNotSupportedException();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromException(NotSupportedException());

        public override void WriteByte(byte value) => ThrowNotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException(NotSupportedException());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long EnsureValidPosition(long position, [CallerArgumentExpression("position")] string argumentName = default!)
        {
            if (position < 0 || position > this.Length)
            {
                ThrowArgumentOutOfRangeException(argumentName);
            }

            return position;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedException() => throw NotSupportedException();

        private static Exception NotSupportedException() => new NotSupportedException("Unwritable stream.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentOutOfRangeException(string argumentName) => throw new ArgumentOutOfRangeException(argumentName);
    }
}
