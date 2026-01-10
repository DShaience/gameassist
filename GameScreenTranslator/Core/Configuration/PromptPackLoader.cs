using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameScreenTranslator.Core.Configuration;

public static class PromptPackLoader
{
    public const string DefaultPromptsRelativePath = "configs/prompts.yaml";
    public const string TemplateEmbeddedResourceName = "prompts.template.yaml";

    public static PromptPack Load(string baseDirectory, string relativePath = DefaultPromptsRelativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));

        var path = Path.Combine(baseDirectory, relativePath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Prompts config file not found: {path}. " +
                $"Create it by copying '{Path.Combine(baseDirectory, "configs", "prompts.template.yaml")}' to '{path}'.",
                path);
        }

        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var pack = deserializer.Deserialize<PromptPack>(yaml) ?? new PromptPack();

        Validate(pack, path);
        return pack;
    }

    public static void EnsurePromptsFileExists(string baseDirectory, string relativePath = DefaultPromptsRelativePath)
    {
        var path = Path.Combine(baseDirectory, relativePath);
        if (File.Exists(path))
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var content = ReadEmbeddedTemplate();
        File.WriteAllText(path, content);
    }

    private static string ReadEmbeddedTemplate()
    {
        // Reuse the existing embedded-resource helper (ConfigBootstrapper) pattern.
        // The resource logical name is fixed in the csproj.
        var asm = typeof(PromptPackLoader).Assembly;

        using var stream = asm.GetManifestResourceStream(TemplateEmbeddedResourceName);
        if (stream is null)
        {
            throw new FileNotFoundException(
                $"Embedded prompt template '{TemplateEmbeddedResourceName}' was not found in the assembly. " +
                $"Ensure it is marked as EmbeddedResource with LogicalName='{TemplateEmbeddedResourceName}'.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Validate(PromptPack pack, string path)
    {
        if (pack is null)
            throw new InvalidOperationException($"Prompts config could not be parsed: {path}");

        if (string.IsNullOrWhiteSpace(pack.Foundry.System))
            throw new InvalidOperationException($"Missing prompt: foundry.system ({path})");

        if (string.IsNullOrWhiteSpace(pack.Foundry.TranslateUser))
            throw new InvalidOperationException($"Missing prompt: foundry.translate_user ({path})");

        if (string.IsNullOrWhiteSpace(pack.Foundry.QuestionUser))
            throw new InvalidOperationException($"Missing prompt: foundry.question_user ({path})");
    }
}
