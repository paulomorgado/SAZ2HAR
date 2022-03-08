using Ionic.Zip;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static PauloMorgado.Tools.SazToHar.HttpUtilities;

namespace PauloMorgado.Tools.SazToHar;

/// <summary>
/// SAZ to HAR converter.
/// </summary>
/// <seealso cref="https://source.dot.net/#Microsoft.AspNetCore.Server.Kestrel.Core/Internal/Http/HttpParser.cs"/>
/// <seealso cref="https://github.com/dotnet/aspnetcore/blob/main/src/Servers/Kestrel/Core/src/Internal/Http/HttpParser.cs"/>
internal sealed class SazToHarConverter : IDisposable
{
#pragma warning restore CA1823 // Avoid unused private fields
    private Ionic.Zip.ZipFile zipFile;
    private ExceptionDispatchInfo error;
    private Dictionary<int, (ZipEntry client, ZipEntry server, ZipEntry metadata)> frames;
    private int[] frameIds;
    private MemoryStream? memory;
    private char[]? charBuffer;
    private List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>? httpHeadersList;
    private bool isDisposed;
    public readonly string inputFilePath;
    public readonly string? password;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public SazToHarConverter(string inputFilePath, string? password)
    {
        this.inputFilePath = inputFilePath;
        this.password = password;
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    ~SazToHarConverter()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: false);
    }

    public void WriteTo(Utf8JsonWriter outputJsonWriter)
    {
        this.OneTimeInitialize();

        this.WriteLog(outputJsonWriter);
    }

    private void OneTimeInitialize()
    {
        if (this.error is not null)
        {
            this.error.Throw();
        }

        if (this.zipFile is not null)
        {
            return;
        }

        try
        {
            this.zipFile = new Ionic.Zip.ZipFile(this.inputFilePath)
            {
                Password = this.password,
            };
            var frameIds = new HashSet<int>();
            var frames = new Dictionary<int, (ZipEntry client, ZipEntry server, ZipEntry metadata)>();

            foreach (var entry in this.zipFile)
            {
                if (entry.IsDirectory || !entry.FileName.StartsWith("raw/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.FileName.EndsWith("_c.txt", StringComparison.OrdinalIgnoreCase))
                {
                    var frameId = ParseFrameId(entry.FileName);
                    frameIds.Add(frameId);
                    frames.TryGetValue(frameId, out var frame);
                    frame.client = entry;
                    frames[frameId] = frame;
                }
                else if (entry.FileName.EndsWith("_s.txt", StringComparison.OrdinalIgnoreCase))
                {
                    var frameId = ParseFrameId(entry.FileName);
                    frameIds.Add(frameId);
                    frames.TryGetValue(frameId, out var frame);
                    frame.server = entry;
                    frames[frameId] = frame;
                }
                else if (entry.FileName.EndsWith("_m.xml", StringComparison.OrdinalIgnoreCase))
                {
                    var frameId = ParseFrameId(entry.FileName);
                    frameIds.Add(frameId);
                    frames.TryGetValue(frameId, out var frame);
                    frame.metadata = entry;
                    frames[frameId] = frame;
                }

                static int ParseFrameId(ReadOnlySpan<char> fileName)
                {
                    return int.Parse(fileName[4..^6]);
                }
            }

            frames.TrimExcess();
            this.frames = frames;

            this.frameIds = new int[frameIds.Count];
            frameIds.CopyTo(this.frameIds);
            Array.Sort(this.frameIds);
        }
        catch (Exception ex)
        {
            this.error = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }

    private void WriteLog(Utf8JsonWriter outputJsonWriter)
    {
        outputJsonWriter.WriteStartObject();

        outputJsonWriter.WriteStartObject("log");

        WriteLogVersion(outputJsonWriter);

        WriteLogCreator(outputJsonWriter);

        WritePages(outputJsonWriter);

        this.WrtieEntries(outputJsonWriter);

        outputJsonWriter.WriteEndObject();

        outputJsonWriter.WriteEndObject();
    }

    private static void WriteLogCreator(Utf8JsonWriter outputJsonWriter)
    {
        outputJsonWriter.WriteStartObject("creator");

        outputJsonWriter.WriteString("name", "SAZ2HAR");

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        if (!string.IsNullOrEmpty(version))
        {
            outputJsonWriter.WriteString("version", version);
        }

        outputJsonWriter.WriteString("comment", "https://www.paulomorgado.net/");

        outputJsonWriter.WriteEndObject();
    }

    private static void WriteLogVersion(Utf8JsonWriter outputJsonWriter)
    {
        outputJsonWriter.WriteString("version", "1.2");
    }

    private static void WritePages(Utf8JsonWriter outputJsonWriter)
    {
        outputJsonWriter.WriteStartArray("pages");
        outputJsonWriter.WriteEndArray();
    }

    private void WrtieEntries(Utf8JsonWriter outputJsonWriter)
    {
        outputJsonWriter.WriteStartArray("entries");

        foreach (var frameId in this.frameIds)
        {
            this.WriteEntry(outputJsonWriter, frameId);
        }

        outputJsonWriter.WriteEndArray();
    }

    private void WriteEntry(Utf8JsonWriter outputJsonWriter, int frameId)
    {
        outputJsonWriter.WriteStartObject();

        outputJsonWriter.WriteString("comment", $"[#{frameId}]");

        if (this.frames.TryGetValue(frameId, out var frame))
        {
            this.InitMemory();

            this.WriteRequest(outputJsonWriter, frame.client);

            this.InitMemory();

            this.WriteResponse(outputJsonWriter, frame.server);
        }

        outputJsonWriter.WriteEndObject();
    }

    private void WriteRequest(Utf8JsonWriter outputJsonWriter, ZipEntry client)
    {
        outputJsonWriter.WriteStartObject("request");

        var httpMessageBytes = this.GetHttpMessageBytes(client);

        if (httpMessageBytes.Length > 0)
        {
            ParseRequestLine(ref httpMessageBytes, out var method, out var url, out var httpVersion);
            WriteRequestLine(outputJsonWriter, method, url, httpVersion);

            var httpHeaders = this.ParseHttpHeaders(ref httpMessageBytes);

            WriteHttpHeaders(outputJsonWriter, httpHeaders);

            WriteQueryString(outputJsonWriter, url);

            WriteRequestHttpCookies(
                outputJsonWriter,
                httpHeaders
                    .Where(h => h.Key.Span.SequenceEqual(CookieAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
                    .Select(h => h.Value));

            this.WritePostData(
                outputJsonWriter,
                httpMessageBytes,
                httpHeaders.LastOrDefault(h => h.Key.Span.SequenceEqual(ContentTypeAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)).Value.Span);
        }

        outputJsonWriter.WriteEndObject();
    }

    private static void ParseRequestLine(ref ReadOnlyMemory<byte> httpMessageBytes, out ReadOnlySpan<byte> method, out ReadOnlySpan<byte> url, out ReadOnlySpan<byte> httpVersion)
    {
        if (httpMessageBytes.IsEmpty
            || !httpMessageBytes.TryReadTo(out var requestLine, SingleLineBreakBytes, true, false)
            || requestLine.IsEmpty)
        {
            throw new InvalidDataException($"Empty request line.");
        }

        var remaining = requestLine;

        if (!remaining.TryReadTo(out method, ByteSpace, true, false)
            || remaining.IsEmpty)
        {
            throw new InvalidDataException($"Invalid request line: {requestLine.GetAsciiStringEscaped()}");
        }

        if (!remaining.TryReadTo(out url, ByteSpace, true, false)
            || remaining.IsEmpty)
        {
            throw new InvalidDataException($"Invalid request line: {requestLine.GetAsciiStringEscaped()}");
        }

        httpVersion = remaining;
    }

    private static void WriteQueryString(Utf8JsonWriter outputJsonWriter, ReadOnlySpan<byte> url)
    {
        var queryStart = url.IndexOf(ByteQuestionMark) + 1;
        var queryString = (queryStart >= 0 ? url[queryStart..] : default).GetAsciiString().AsMemory();

        WriteParams(outputJsonWriter, queryString, "queryString");
    }

    private static void WriteParams(Utf8JsonWriter outputJsonWriter, ReadOnlyMemory<char> data, string paramsName)
    {
        outputJsonWriter.WriteStartArray(paramsName);

        if (!data.IsEmpty)
        {

            foreach (var queryStringParameter in new QueryStringEnumerable(data))
            {
                outputJsonWriter.WriteStartObject();

                outputJsonWriter.WriteString("name", queryStringParameter.DecodeName());
                outputJsonWriter.WriteString("value", queryStringParameter.DecodeValue());

                outputJsonWriter.WriteEndObject();
            }
        }

        outputJsonWriter.WriteEndArray();
    }

    private void WritePostData(Utf8JsonWriter outputJsonWriter, ReadOnlyMemory<byte> httpMessageBytes, ReadOnlySpan<byte> contentType)
    {
        outputJsonWriter.WriteNumber("bodySize", httpMessageBytes.Length);

        if (httpMessageBytes.IsEmpty)
        {
            return;
        }

        outputJsonWriter.WriteStartObject("postData");

        this.WriteContent(outputJsonWriter, httpMessageBytes, contentType);

        outputJsonWriter.WriteEndObject();
    }

    private static void WriteRequestLine(Utf8JsonWriter outputJsonWriter, ReadOnlySpan<byte> method, ReadOnlySpan<byte> url, ReadOnlySpan<byte> httpVersion)
    {
        outputJsonWriter.WriteString("method", method);
        outputJsonWriter.WriteString("url", url);
        outputJsonWriter.WriteString("httpVersion", httpVersion.GetAsciiString(toLower: true));
    }

    private void WriteResponse(Utf8JsonWriter outputJsonWriter, ZipEntry server)
    {
        outputJsonWriter.WriteStartObject("response");

        var httpMessageBytes = this.GetHttpMessageBytes(server);

        if (httpMessageBytes.Length > 0)
        {
            ParseResponseLine(ref httpMessageBytes, out var httpVersion, out var statusCode, out var statusText);
            WriteResponseLine(outputJsonWriter, httpVersion, statusCode, statusText);

            var httpHeaders = this.ParseHttpHeaders(ref httpMessageBytes);

            WriteHttpHeaders(outputJsonWriter, httpHeaders);

            WriteResponseHttpCookies(
                outputJsonWriter,
                httpHeaders
                    .Where(h => h.Key.Span.SequenceEqual(SetCookieAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
                    .Select(h => h.Value));

            this.WriteResponseBody(
                outputJsonWriter,
                httpMessageBytes,
                httpHeaders);
        }

        outputJsonWriter.WriteEndObject();
    }

    private static void ParseResponseLine(ref ReadOnlyMemory<byte> httpMessageBytes, out ReadOnlySpan<byte> httpVersion, out ReadOnlySpan<byte> statusCode, out ReadOnlySpan<byte> statusText)
    {
        if (httpMessageBytes.IsEmpty
            || !httpMessageBytes.TryReadTo(out var responseLine, SingleLineBreakBytes, true, false)
            || responseLine.IsEmpty)
        {
            throw new InvalidDataException($"Empty response line.");
        }

        var remaining = responseLine;

        if (!remaining.TryReadTo(out httpVersion, ByteSpace, true, false)
            || remaining.IsEmpty)
        {
            throw new InvalidDataException($"Invalid response line: {responseLine.GetAsciiStringEscaped()}");
        }

        if (!remaining.TryReadTo(out statusCode, ByteSpace, true, true))
        {
            throw new InvalidDataException($"Invalid response line: {responseLine.GetAsciiStringEscaped()}");
        }

        statusText = remaining;
    }

    private static void WriteResponseLine(Utf8JsonWriter outputJsonWriter, ReadOnlySpan<byte> httpVersion, ReadOnlySpan<byte> statusCode, ReadOnlySpan<byte> statusText)
    {
        outputJsonWriter.WriteNumber("status", statusCode.ParseStatusCode());
        outputJsonWriter.WriteString("statusText", statusText);
        outputJsonWriter.WriteString("httpVersion", httpVersion.GetAsciiString(toLower: true));
    }

    private void WriteResponseBody(Utf8JsonWriter outputJsonWriter, ReadOnlyMemory<byte> httpMessageBytes, List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> httpHeaders)
    {
        outputJsonWriter.WriteStartObject("content");

        outputJsonWriter.WriteNumber("size", httpMessageBytes.Length);

        try
        {
            this.UnchunkContent(
                ref httpMessageBytes,
                httpHeaders.LastOrDefault(h => h.Key.Span.SequenceEqual(TransferEncodignAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)).Value.Span);

            this.DecompressContent(
                ref httpMessageBytes,
                httpHeaders.LastOrDefault(h => h.Key.Span.SequenceEqual(ContentEncodignAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)).Value.Span);

            this.WriteContent(
                outputJsonWriter,
                httpMessageBytes,
                httpHeaders.LastOrDefault(h => h.Key.Span.SequenceEqual(ContentTypeAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)).Value.Span);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            outputJsonWriter.WriteString(":error", $"{ex.GetType().FullName}: {ex.Message}");
            this.WriteContent(outputJsonWriter, httpMessageBytes, ReadOnlySpan<byte>.Empty);
        }
#pragma warning restore CA1031 // Do not catch general exception types

        outputJsonWriter.WriteEndObject();
    }

    private List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ParseHttpHeaders(ref ReadOnlyMemory<byte> httpMessageBytes)
    {
        var httpHeadersList = this.httpHeadersList ??= new();
        httpHeadersList.Clear();

        while (true)
        {
            var idx = httpMessageBytes.Span.IndexOf(SingleLineBreakBytes);

            if (idx < 0)
            {
                throw new InvalidDataException("The message does not contain a valid HTTP header.");
            }

            if (idx == 0)
            {
                httpMessageBytes = httpMessageBytes[2..];
                return httpHeadersList;
            }

            var header = httpMessageBytes[..idx];
            httpMessageBytes = httpMessageBytes[(idx + 2)..];

            idx = header.Span.IndexOfAny(ByteColon, ByteSpace, ByteTab);

            if (idx < 1 || idx >= header.Span.Length)
            {
                throw new InvalidDataException("The message does not contain a valid HTTP header.");
            }

            var name = header[..idx];

            var value = header[(idx + 1)..].Trim<byte>(WhiteSpaceBytes);

            httpHeadersList.Add(new(name, value));
        }
    }

    private void UnchunkContent(ref ReadOnlyMemory<byte> httpMessageBytes, ReadOnlySpan<byte> transferEncodingBytes)
    {
        if (httpMessageBytes.IsEmpty || transferEncodingBytes.IsEmpty
            || !transferEncodingBytes.SequenceEqual(ChunkedAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
        {
            return;
        }

        var messageStream = this.GetMemoryStream();
        var start = messageStream.Length;
        var bytes = httpMessageBytes.Span;

        for (var chunkStart = 0; chunkStart < bytes.Length - 3;)
        {
            var i = chunkStart;
            while (i < bytes.Length - 3 && bytes[i] != ByteCR && bytes[i] != ByteLF)
            {
                i++;
            }

            if (i > bytes.Length - 2)
            {
                throw new InvalidDataException($"HTTP Error: The chunked content is corrupt. Cannot find Chunk-Length in expected location. Offset: {chunkStart}");
            }

            var chars = bytes[chunkStart..i].GetAsciiString();
            chunkStart = i + 2;

            if (!int.TryParse(TrimAfter(chars, ';'), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out var chunkLength))
            {
                throw new InvalidDataException($"HTTP Error: The chunked content is corrupt. Chunk Length was malformed. Offset: {chunkStart}");
            }

            if (chunkLength == 0)
            {
                httpMessageBytes = messageStream.AsReadOnlyMemory((int)start);
                return;
            }

            if (bytes.Length < chunkStart + chunkLength)
            {
                throw new InvalidDataException("HTTP Error: The chunked entity body is corrupt. The final chunk length is greater than the number of bytes remaining.");
            }

            messageStream.Write(bytes.Slice(chunkStart, chunkLength));
            chunkStart += chunkLength + 2;
        }

        throw new InvalidDataException("Chunked body did not terminate properly with 0-sized chunk.");
    }

    private static ReadOnlySpan<char> TrimAfter(ReadOnlySpan<char> chars, char delimiter)
    {
        var idx = chars.IndexOf(delimiter);
        return (idx < 0) ? chars : chars[..(idx - 1)];
    }

    private void DecompressContent(ref ReadOnlyMemory<byte> httpMessageBytes, ReadOnlySpan<byte> contentEncodingBytes)
    {
        if (contentEncodingBytes.IsEmpty
            || contentEncodingBytes.SequenceEqual(IdentityContentEncodingAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
        {
            return;
        }

        if (contentEncodingBytes.SequenceEqual(GZzipContentEncodingAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
        {
            var sourceStream = httpMessageBytes.AsStream();

            try
            {
                httpMessageBytes = this.DecompressGZipContent(sourceStream);
            }
            catch (InvalidDataException)
            {
                sourceStream.Position = 0;
                httpMessageBytes = this.DeflateDeflateContent(sourceStream);
            }
        }
        else if (contentEncodingBytes.SequenceEqual(DeflateContentEncodingAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
        {
            var sourceStream = httpMessageBytes.AsStream();

            httpMessageBytes = this.DeflateDeflateContent(sourceStream);
        }
        else if (contentEncodingBytes.SequenceEqual(BrotliContentEncodingAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance))
        {
            var sourceStream = httpMessageBytes.AsStream();

            httpMessageBytes = this.DecompressBrotliContent(sourceStream);
        }
        else
        {
            throw new InvalidDataException($"Unsupported compression method: {contentEncodingBytes.GetAsciiString()}");
        }
    }

    private ReadOnlyMemory<byte> DecompressGZipContent(Stream sourceStream)
    {
        using var compressed = new GZipStream(sourceStream, CompressionMode.Decompress, true);
        return this.CopyStreamToMemory(compressed);
    }

    private ReadOnlyMemory<byte> DeflateDeflateContent(Stream sourceStream)
    {
        using var compressed = new DeflateStream(sourceStream, CompressionMode.Decompress, true);
        return this.CopyStreamToMemory(compressed);
    }

    private ReadOnlyMemory<byte> DecompressBrotliContent(Stream sourceStream)
    {
        using var compressed = new BrotliStream(sourceStream, CompressionMode.Decompress, true);
        return this.CopyStreamToMemory(compressed);
    }

    private ReadOnlyMemory<byte> CopyStreamToMemory(Stream source)
    {
        var start = this.memory!.Position = this.memory!.Length;
        source.CopyTo(this.memory);
        return new(this.memory.GetBuffer(), (int)start, (int)(this.memory.Length - start));
    }

    private static void WriteHttpHeaders(Utf8JsonWriter outputJsonWriter, List<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> httpHeaders)
    {
        outputJsonWriter.WriteStartArray("headers");

        foreach (var httpHeader in httpHeaders)
        {
            outputJsonWriter.WriteStartObject();

            outputJsonWriter.WriteString("name", httpHeader.Key.Span);
            outputJsonWriter.WriteString("value", httpHeader.Value.Span.Trim<byte>(32));

            outputJsonWriter.WriteEndObject();
        }

        outputJsonWriter.WriteEndArray();
    }

    private static void WriteRequestHttpCookies(Utf8JsonWriter outputJsonWriter, IEnumerable<ReadOnlyMemory<byte>> cookies)
    {
        outputJsonWriter.WriteStartArray("cookies");

        foreach (var cookie in cookies)
        {
            foreach (var cookiePart in new CookiesEnumerable(cookie))
            {
                outputJsonWriter.WriteStartObject();

                outputJsonWriter.WriteString("name", cookiePart.DecodeName());
                outputJsonWriter.WriteString("value", cookiePart.DecodeValue());

                outputJsonWriter.WriteEndObject();
            }
        }

        outputJsonWriter.WriteEndArray();
    }

    private static void WriteResponseHttpCookies(Utf8JsonWriter outputJsonWriter, IEnumerable<ReadOnlyMemory<byte>> cookies)
    {
        outputJsonWriter.WriteStartArray("cookies");

        foreach (var cookie in cookies)
        {
            outputJsonWriter.WriteStartObject();

            var first = true;

            foreach (var cookiePart in new CookiesEnumerable(cookie))
            {
                if (first)
                {
                    outputJsonWriter.WriteString("name", cookiePart.DecodeName());
                    outputJsonWriter.WriteString("value", cookiePart.DecodeValue());
                    first = false;
                }
                else
                {
                    outputJsonWriter.WriteString(
                        cookiePart.DecodeName().Trim().ToLowerInvariant(),
                        cookiePart.DecodeValue().Trim());
                }
            }

            outputJsonWriter.WriteEndObject();
        }

        outputJsonWriter.WriteEndArray();
    }

    private void WriteContent(Utf8JsonWriter outputJsonWriter, ReadOnlyMemory<byte> httpMessageBytes, ReadOnlySpan<byte> contentTypeDefinitionBytes)
    {
        outputJsonWriter.WriteString("mimeType", contentTypeDefinitionBytes);

        outputJsonWriter.WritePropertyName("text");

        var httpMessageBytesSpan = httpMessageBytes.Span;

        if (!contentTypeDefinitionBytes.IsEmpty)
        {
            contentTypeDefinitionBytes.TryReadTo(out var contentTypeBytes, ByteSemicolon, true, true);
            contentTypeBytes = contentTypeBytes.Trim(WhiteSpaceBytes);

            var encoding = Encoding.UTF8;

            var isForm = false;

            if (contentTypeBytes.SequenceEqual(TextContentTypePreffixAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)
                || contentTypeBytes.SequenceEqual(ApplicationJsonContentTypePreffixAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)
                || contentTypeBytes.SequenceEqual(ApplicationJsonStreamContentTypePreffixAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)
                || contentTypeBytes.SequenceEqual(ApplicationXmlContentTypePreffixAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)
                || (isForm = contentTypeBytes.SequenceEqual(ApplicationFormEncodedContentTypePreffixAsciiBytes, CaseInsensitiveAsciiByteEqualityComparer.Instance)))
            {
                contentTypeDefinitionBytes = contentTypeDefinitionBytes.Trim(WhiteSpaceBytes);

                if (!contentTypeDefinitionBytes.IsEmpty)
                {
                    foreach (var contentTypeParameter in new CookiesEnumerable(new ReadOnlyMemory<byte>(contentTypeDefinitionBytes.ToArray())))
                    {
                        var contentTypeParameterName = contentTypeParameter.DecodeName();
                        if (contentTypeParameterName.Equals("charset", StringComparison.OrdinalIgnoreCase))
                        {
                            var contentTypeParameterValue = contentTypeParameter.DecodeValue();
                            if (!contentTypeParameterValue.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                            {
                                encoding = Encoding.GetEncoding(contentTypeParameterValue.ToString());
                            }
                        }
                    }
                }

                var contentText = this.GetEncodedChars(encoding, httpMessageBytesSpan);
                outputJsonWriter.WriteStringValue(contentText.Span);

                if (isForm)
                {
                    WriteParams(outputJsonWriter, contentText, "params");
                }

                return;
            }
        }

        if (httpMessageBytesSpan.IndexOf((byte)0) >= 0)
        {
            var inputBlocks = Math.DivRem(httpMessageBytesSpan.Length, 3, out var inputRemainder);
            var outputBytes = (inputBlocks + (inputRemainder != 0 ? 1 : 0)) * 4;

            var output = this.GetMemory(outputBytes);

            var status = Base64.EncodeToUtf8(httpMessageBytesSpan, output.Span, out var bytesConsumed, out var bytesWritten);

            Debug.Assert(bytesWritten == outputBytes);
            Debug.Assert(status == OperationStatus.Done);
            Debug.Assert(bytesConsumed == httpMessageBytesSpan.Length);

            outputJsonWriter.WriteStringValue(output.Span);
            outputJsonWriter.WriteString("encoding", "base64");
        }
        else
        {
            outputJsonWriter.WriteStringValue(httpMessageBytesSpan);
        }
    }

    private ReadOnlyMemory<char> GetEncodedChars(Encoding encoding, ReadOnlySpan<byte> bytes, int @case = 0)
    {
        var count = encoding.GetCharCount(bytes);

        if (this.charBuffer is null || this.charBuffer.Length < count)
        {
            this.charBuffer = new char[count];
        }

        count = encoding.GetChars(bytes, this.charBuffer);

        var chars = this.charBuffer;

        if (@case < 0)
        {
            for (var i = 0; i < count; i++)
            {
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
        }
        else if (@case > 0)
        {
            for (var i = 0; i < count; i++)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
        }

        return new ReadOnlyMemory<char>(chars, 0, count);
    }

    private ReadOnlyMemory<byte> GetHttpMessageBytes(ZipEntry entry)
    {
        var httpMessageStream = this.GetHttpMessageStream(entry);
        var httpMessageBytes = httpMessageStream.AsReadOnlyMemory();
        return httpMessageBytes;
    }

    private void InitMemory()
    {
        var buffer = this.memory ??= new(0x20000);
        buffer.SetLength(0);
    }

    private Memory<byte> GetMemory(int size)
    {
        var memoryStream = this.memory!;
        var finalLength = memoryStream.Length + size;

        if (memoryStream.Capacity < finalLength)
        {
            memoryStream.Capacity = (int)(((finalLength + 0x1FFFFL) / 0x20000L) * 0x20000L);
        }

        var memory = memoryStream.AsMemory((int)memoryStream.Length, size);

        memoryStream.SetLength(finalLength);
        memoryStream.Position = finalLength;

        return memory;
    }

    private MemoryStream GetMemoryStream()
    {
        this.memory!.Position = this.memory!.Length;
        return this.memory!;
    }

    private MemoryStream GetHttpMessageStream(ZipEntry zipEntry)
    {
        var buffer = this.GetMemoryStream();

        using var zipStream = zipEntry.OpenReader();

        zipStream.CopyTo(buffer);

        buffer.Position = 0;

        return buffer;
    }

    private void Dispose(bool disposing)
    {
        if (!this.isDisposed)
        {
            if (disposing)
            {
                // dispose managed state (managed objects)

                this.zipFile?.Dispose();
                this.memory?.Dispose();
            }

            // free unmanaged resources (unmanaged objects) and override finalizer
            // set large fields to null

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            this.zipFile = null;
            this.memory = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

            this.isDisposed = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
