namespace PauloMorgado.Tools.SazToHar;

internal enum RequestRejectionReason
{
    TlsOverHttpError,
    UnrecognizedHTTPVersion,
    InvalidRequestLine,
    InvalidRequestHeader,
    InvalidRequestHeadersNoCRLF,
    MalformedRequestInvalidHeaders,
    InvalidContentLength,
    MultipleContentLengths,
    UnexpectedEndOfRequestContent,
    BadChunkSuffix,
    BadChunkSizeData,
    ChunkedRequestIncomplete,
    InvalidRequestTarget,
    InvalidCharactersInHeaderName,
    RequestLineTooLong,
    HeadersExceedMaxTotalSize,
    TooManyHeaders,
    RequestBodyTooLarge,
    RequestHeadersTimeout,
    RequestBodyTimeout,
    FinalTransferCodingNotChunked,
    LengthRequiredHttp10,
    OptionsMethodRequired,
    ConnectMethodRequired,
    MissingHostHeader,
    MultipleHostHeaders,
    InvalidHostHeader,
    RequestBodyExceedsContentLength
}