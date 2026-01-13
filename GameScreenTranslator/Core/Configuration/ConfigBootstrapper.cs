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

        // Prompts (YAML)
        EnsureFromEmbeddedTemplate(
            templateResourceName: "prompts.template.yaml",
            targetPath: Path.Combine(baseDirectory, "configs", "prompts.yaml"));

        // Extract earcon sound assets (MP3/WAV files)
        EnsureAssetFromEmbeddedResource(
            resourceName: "Sounds\\tap-notification-357575.mp3",
            targetPath: Path.Combine(baseDirectory, "Assets", "Sounds", "tap-notification-357575.mp3"));
    }

    private static void EnsureFromEmbeddedTemplate(string templateResourceName, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var content = ReadEmbeddedResourceAsString(templateResourceName);
        File.WriteAllText(targetPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void EnsureAssetFromEmbeddedResource(string resourceName, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        var asm = Assembly.GetExecutingAssembly();

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // Asset not found in embedded resources; this is non-fatal for earcons
            // (the EarconPlayer will log a warning if the file is missing at runtime)
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using var fileStream = File.Create(targetPath);
        stream.CopyTo(fileStream);
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
