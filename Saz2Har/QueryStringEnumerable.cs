using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace PauloMorgado.Tools.SazToHar;

/// <summary>
/// An enumerable that can supply the name/value pairs from a URI query string.
/// </summary>
internal readonly struct QueryStringEnumerable
{
    private readonly ReadOnlyMemory<char> queryString;

    /// <summary>
    /// Constructs an instance of <see cref="QueryStringEnumerable"/>.
    /// </summary>
    /// <param name="queryString">The query string.</param>
    public QueryStringEnumerable(ReadOnlyMemory<char> queryString)
    {
        this.queryString = queryString;
    }

    /// <summary>
    /// Retrieves an object that can iterate through the name/value pairs in the query string.
    /// </summary>
    /// <returns>An object that can iterate through the name/value pairs in the query string.</returns>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this.queryString);
    }

    /// <summary>
    /// Represents a single name/value pair extracted from a query string during enumeration.
    /// </summary>
    public readonly struct EncodedNameValuePair
    {
        /// <summary>
        /// Gets the name from this name/value pair in its original encoded form.
        /// To get the decoded string, call <see cref="DecodeName"/>.
        /// </summary>
        public readonly ReadOnlyMemory<char> EncodedName { get; }

        /// <summary>
        /// Gets the value from this name/value pair in its original encoded form.
        /// To get the decoded string, call <see cref="DecodeValue"/>.
        /// </summary>
        public readonly ReadOnlyMemory<char> EncodedValue { get; }

        internal EncodedNameValuePair(ReadOnlyMemory<char> encodedName, ReadOnlyMemory<char> encodedValue)
        {
            this.EncodedName = encodedName;
            this.EncodedValue = encodedValue;
        }

        /// <summary>
        /// Decodes the name from this name/value pair.
        /// </summary>
        /// <returns>Characters representing the decoded name.</returns>
        public ReadOnlySpan<char> DecodeName() => Decode(this.EncodedName);

        /// <summary>
        /// Decodes the value from this name/value pair.
        /// </summary>
        /// <returns>Characters representing the decoded value.</returns>
        public ReadOnlySpan<char> DecodeValue() => Decode(this.EncodedValue);

        private static ReadOnlySpan<char> Decode(ReadOnlyMemory<char> chars) => chars.Span.ReplacePlusWithSpace().UnescapeDataString();
    }

    /// <summary>
    /// An enumerator that supplies the name/value pairs from a URI query string.
    /// </summary>
    public struct Enumerator
    {
        private ReadOnlyMemory<char> query;

        internal Enumerator(ReadOnlyMemory<char> query)
        {
            this.Current = default;
            this.query = query.IsEmpty || query.Span[0] != '?'
                ? query
                : query.Slice(1);
        }

        /// <summary>
        /// Gets the currently referenced key/value pair in the query string being enumerated.
        /// </summary>
        public EncodedNameValuePair Current { get; private set; }

        /// <summary>
        /// Moves to the next key/value pair in the query string being enumerated.
        /// </summary>
        /// <returns>True if there is another key/value pair, otherwise false.</returns>
        public bool MoveNext()
        {
            while (!this.query.IsEmpty)
            {
                // Chomp off the next segment
                ReadOnlyMemory<char> segment;
                var delimiterIndex = this.query.Span.IndexOf('&');
                if (delimiterIndex >= 0)
                {
                    segment = this.query.Slice(0, delimiterIndex);
                    this.query = this.query.Slice(delimiterIndex + 1);
                }
                else
                {
                    segment = this.query;
                    this.query = default;
                }

                // If it's nonempty, emit it
                var equalIndex = segment.Span.IndexOf('=');
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
