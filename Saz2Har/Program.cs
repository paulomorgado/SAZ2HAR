using PauloMorgado.Tools.SazToHar;
using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

[assembly: CLSCompliant(true)]

#pragma warning disable CA1812 // Program is an internal class that is apparently never instantiated. If so, remove the code from the assembly. If this class is intended to contain only static members, make it static (Shared in Visual Basic).

var sourceFilePathArgument = new Argument<FileInfo>(
        name: "SourceFilePath",
        description: "Input SAZ file to be converted to HAR.")
{
};

var passwordOption = new Option<string?>(
        aliases: new[] { "--password", "-p" },
        description: "The optional password for password-protected SAZ files.")
{
    IsRequired = false,
};

var outpitFilePathOption = new Option<FileInfo?>(
        aliases: new[] { "--output", "-o" },
        description: "The optional output file. Default is appending \".har\" to the source file path.")
{
    IsRequired = false,
};

var noLogoOption = new Option<bool?>(
        aliases: new[] { "--no-logo", "--n" },
        description: "Does not print logo information. Default is ON. OFF when output is not specified.")
{
    IsRequired = false,
};

var indented = new Option<bool?>(
        aliases: new[] { "--indented", "--i" },
        description: "Generates indented output.")
{
    IsRequired = false,
};

var rootCommand = new RootCommand("SAZ to HAR Converter")
{
    sourceFilePathArgument,
    passwordOption,
    outpitFilePathOption,
    noLogoOption,
    indented,
};

rootCommand.SetHandler(
    (IConsole console, FileInfo sourceFilePath, string? password, FileInfo? outputFile, bool? noLogo, bool? indented) =>
    {
        try
        {
            using var converter = new SazToHarConverter(sourceFilePath.FullName, password);

            var jsonWriterOptions = new JsonWriterOptions
            {
                Indented = indented.GetValueOrDefault(),
                Encoder = JavaScriptEncoder.Default,
            };

            var outputFilePath = outputFile?.FullName ?? sourceFilePath.FullName + ".har";
            using var outputFileStream = File.Create(outputFilePath);
            using var outputJsonWriter = new Utf8JsonWriter(outputFileStream, jsonWriterOptions);

            converter.WriteTo(outputJsonWriter);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            console.Error.WriteLine(ex.ToString());
        }
#pragma warning restore CA1031 // Do not catch general exception types
    },
    sourceFilePathArgument,
    passwordOption,
    outpitFilePathOption,
    noLogoOption,
    indented);

rootCommand.Invoke(args);
