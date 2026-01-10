using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace GameScreenTranslator.Core.Configuration;

public static class ConfigBootstrapper
{
    public static void EnsureConfigFilesExist(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));

        EnsureFromEmbeddedTemplate(
            templateResourceName: "appsettings.template.json",
            targetPath: Path.Combine(baseDirectory, "appsettings.json"));

        EnsureFromEmbeddedTemplate(
            templateResourceName: "speech_language_config.template.json",
            targetPath: Path.Combine(baseDirectory, "speech_language_config.json"));
    }

    private static void EnsureFromEmbeddedTemplate(string templateResourceName, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var content = ReadEmbeddedResourceAsString(templateResourceName);
        File.WriteAllText(targetPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ReadEmbeddedResourceAsString(string templateResourceName)
    {
        var asm = Assembly.GetExecutingAssembly();

        using var stream = asm.GetManifestResourceStream(templateResourceName);
        if (stream is null)
        {
            throw new FileNotFoundException(
                $"Embedded config template '{templateResourceName}' was not found in the assembly. " +
                $"Ensure it is marked as EmbeddedResource with LogicalName='{templateResourceName}'.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}