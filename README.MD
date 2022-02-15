# SAZ to HAR converter

Utitlity to convert [Fiddler](https://www.telerik.com/fiddler "Fiddler | Web Debugging Proxy and Troubleshooting Solutions (telerik.com)")'s SAZ file format to [HTTP Archive (HAR) format](https://w3c.github.io/web-performance/specs/HAR/Overview.html "HTTP Archive (HAR) format (w3c.github.io)").

## Command-line

```
Description:
  SAZ to HAR Converter

Usage:
  Saz2Har <SourceFilePath> [options]

Arguments:
  <SourceFilePath>  Input SAZ file to be converted to HAR.

Options:
  -p, --password <password>  The optional password for password-protected SAZ files.
  -o, --output <output>      The optional output file. Default is appending ".har" to the source file path.
  --n, --no-logo             Does not print logo information. Default is ON. OFF when output is not specified.
  --i, --indented            Generates indented output.
  --version                  Show version information
  -?, -h, --help             Show help and usage information
```
