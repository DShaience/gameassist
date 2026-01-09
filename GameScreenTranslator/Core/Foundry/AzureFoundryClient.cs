using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Imaging;
using GameScreenTranslator.Core.Models;
using GameScreenTranslator.Core.State;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Foundry;

public sealed class AzureFoundryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AppSettings.AzureFoundrySettings _settings;

    public AzureFoundryClient(AppSettings.AzureFoundrySettings settings, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // Note: Azure Foundry "model inference" endpoint is OpenAI-compatible. We call:
        //   POST /openai/deployments/{deployment}/chat/completions?api-version=...
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(EnsureTrailingSlash(_settings.Endpoint));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);

        _http.Timeout = TimeSpan.FromSeconds(_settings.LLM.TimeoutSeconds);
    }

    public async Task<TranslateCycleResponse> TranslateCycleAsync(
        Image<Rgba32> screenshot,
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string mode,
        string? userQuestion = null,
        int? downscaleWidthOverride = null,
        Action<string>? onRawModelContent = null,
        CancellationToken cancellationToken = default)
    {
        if (screenshot is null) throw new ArgumentNullException(nameof(screenshot));
        if (string.IsNullOrWhiteSpace(windowTitle)) throw new ArgumentException("Window title cannot be empty.", nameof(windowTitle));
        if (string.IsNullOrWhiteSpace(targetLanguage)) throw new ArgumentException("Target language cannot be empty.", nameof(targetLanguage));
        if (string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("Mode cannot be empty.", nameof(mode));

        var deployment = _settings.LLM.VisionDeployment;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Providers:AzureFoundry:LLM:VisionDeployment is required for vision calls.");

        var downscaleWidth = downscaleWidthOverride ?? 640;
        using var downscaled = ImageDownscaler.DownscaleToWidth(screenshot, downscaleWidth);
        var pngBytes = ImageDownscaler.EncodePng(downscaled);
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);

        var systemText =
            "You are a screen translator for children (ages 5–10). " +
            "You will be given a screenshot of a game UI and must identify the most relevant readable text for the requested UI priority, " +
            "translate it, and optionally give a short explanation. " +
            "Output MUST be valid JSON with the required fields and no extra commentary. " +
            "IMPORTANT: sourceText and translatedText must be CLEAN human-readable strings (no JSON fragments, no trailing commas, no surrounding quotes).";

        var userText = BuildUserPrompt(windowTitle, priority, targetLanguage, mode, userQuestion);

        var request = new ChatCompletionsRequest
        {
            Messages =
            [
                new()
                {
                    Role = "system",
                    Content =
                    [
                        new() { Type = "text", Text = systemText }
                    ]
                },
                new()
                {
                    Role = "user",
                    Content =
                    [
                        new() { Type = "text", Text = userText },
                        new() { Type = "image_url", ImageUrl = new ImageUrl { Url = dataUrl } }
                    ]
                }
            ],
            // Some models (e.g. GPT-5.x) reject non-default temperature values.
            // We'll start with a low temperature, and auto-fallback to "omit" if rejected.
            Temperature = 0.2,

            // Compatibility note:
            // Some newer models (e.g. GPT-5.x) reject "max_tokens" and require "max_completion_tokens".
            MaxTokens = 800,
            MaxCompletionTokens = null,

            // Most OpenAI-compatible endpoints support this. If not, we can remove and rely on prompt-only.
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        var url = $"openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(_settings.ApiVersion)}";

        var attempts = Math.Max(0, _settings.LLM.RetryAttempts) + 1;
        Exception? last = null;

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var requestJson = JsonSerializer.Serialize(request, JsonOptions);
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Auto-fix common compatibility issues and retry.
                    if ((int)response.StatusCode == 400)
                    {
                        if (responseBody.Contains("Unsupported parameter: 'max_tokens'", StringComparison.OrdinalIgnoreCase))
                        {
                            request.MaxCompletionTokens = request.MaxTokens;
                            request.MaxTokens = null;
                        }

                        if (responseBody.Contains("Unsupported parameter: 'response_format'", StringComparison.OrdinalIgnoreCase))
                        {
                            request.ResponseFormat = null;
                        }

                        if (responseBody.Contains("Unsupported value: 'temperature'", StringComparison.OrdinalIgnoreCase)
                            || responseBody.Contains("temperature", StringComparison.OrdinalIgnoreCase) &&
                               responseBody.Contains("Only the default (1) value is supported", StringComparison.OrdinalIgnoreCase))
                        {
                            // Omitting temperature typically uses the model default (1).
                            request.Temperature = null;
                        }
                    }

                    throw new InvalidOperationException($"Foundry call failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
                }

                var parsed = JsonSerializer.Deserialize<ChatCompletionsResponse>(responseBody, JsonOptions)
                             ?? throw new InvalidOperationException("Foundry call returned empty response JSON.");

                var modelContent = parsed.Choices?[0]?.Message?.Content;
                if (string.IsNullOrWhiteSpace(modelContent))
                    throw new InvalidOperationException("Foundry response did not contain message content.");

                onRawModelContent?.Invoke(modelContent);

                var output = JsonSerializer.Deserialize<TranslateCycleResponse>(modelContent, JsonOptions);
                if (output is null)
                    throw new InvalidOperationException("Model content could not be parsed as TranslateCycleResponse JSON.");

                NormalizeOutput(output);
                return output;
            }
            catch (Exception ex) when (i < attempts - 1)
            {
                last = ex;
                await Task.Delay(200 * (i + 1), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException("Foundry call failed after retries.", last);
    }

    private static string BuildUserPrompt(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string mode,
        string? userQuestion)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Window title: {windowTitle}");
        sb.AppendLine($"UI priority to focus on: {priority}");
        sb.AppendLine($"Target language: {targetLanguage}");
        sb.AppendLine($"Mode: {mode}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(userQuestion))
        {
            sb.AppendLine("The user is asking a question about what is visible on screen:");
            sb.AppendLine(userQuestion);
            sb.AppendLine();
            sb.AppendLine("Answer only using what is visible on screen. If you cannot see it, say so.");
        }
        else
        {
            sb.AppendLine("Task:");
            sb.AppendLine("- Identify the single most relevant piece of readable UI text according to the UI priority.");
            sb.AppendLine("- Translate it to the target language.");
            sb.AppendLine("- If Mode is 'explain', add ONE short explanatory sentence for a child.");
            sb.AppendLine("- If Mode is 'simplified', rewrite in child-friendly language.");
        }

        sb.AppendLine();
        sb.AppendLine("Return strict JSON with this schema:");
        sb.AppendLine("{");
        sb.AppendLine(@"  ""selectedLabel"": string,");
        sb.AppendLine(@"  ""boundingBox"": { ""x"": int, ""y"": int, ""width"": int, ""height"": int } | null,");
        sb.AppendLine(@"  ""sourceText"": string,");
        sb.AppendLine(@"  ""translatedText"": string,");
        sb.AppendLine(@"  ""explanation"": string | null");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Formatting rules:");
        sb.AppendLine("- sourceText and translatedText MUST be plain human-readable strings.");
        sb.AppendLine(@"- Do NOT return JSON fragments like """"GameTitle"""": """"Super Adventure"""",""");
        sb.AppendLine(@"- If the UI shows label+value, return it as a normal phrase, e.g. ""Game Title: Super Adventure"".");
        sb.AppendLine("- No markdown. No extra keys.");

        return sb.ToString();
    }

    private static void NormalizeOutput(TranslateCycleResponse output)
    {
        output.SelectedLabel = CleanSimple(output.SelectedLabel);
        output.SourceText = CleanUiText(output.SourceText);
        output.TranslatedText = CleanUiText(output.TranslatedText);
        output.Explanation = output.Explanation is null ? null : CleanSimple(output.Explanation);
    }

    private static string CleanSimple(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return StripWrapping(value).Trim();
    }

    private static string CleanUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var s = StripWrapping(value).Trim().TrimEnd(',');

        // Heuristic: model sometimes returns a JSON-like key/value fragment.
        // Example:  "GameTitle": "Super Adventure",
        // Convert to: GameTitle: Super Adventure
        if (TryExtractKeyValue(s, out var key, out var val))
        {
            key = StripWrapping(key).Trim();
            val = StripWrapping(val).Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                return $"{key}: {val}";
        }

        return s;
    }

    private static bool TryExtractKeyValue(string s, out string key, out string value)
    {
        key = "";
        value = "";

        var idx = s.IndexOf(':');
        if (idx <= 0 || idx >= s.Length - 1)
            return false;

        // Avoid times like "12:34" by requiring at least one quote or letter around the split.
        var left = s[..idx].Trim();
        var right = s[(idx + 1)..].Trim().TrimEnd(',');

        if (left.Length == 0 || right.Length == 0)
            return false;

        // Require that the left side looks like a label (letters/quotes/underscores/spaces).
        // This is intentionally loose.
        key = left;
        value = right;
        return true;
    }

    private static string StripWrapping(string s)
    {
        var t = s.Trim();

        // Remove wrapping backticks (rare but happens)
        if (t.Length >= 2 && t[0] == '`' && t[^1] == '`')
            t = t[1..^1].Trim();

        // Remove wrapping quotes
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
            t = t[1..^1].Trim();

        return t;
    }

    private static string EnsureTrailingSlash(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return endpoint;

        return endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/";
    }

    private sealed class ChatCompletionsRequest
    {
        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; } = 0.2;

        // Some models use max_tokens, others require max_completion_tokens.
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; } = 800;

        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }
    }

    private sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public ChatMessagePart[] Content { get; set; } = Array.Empty<ChatMessagePart>();
    }

    private sealed class ChatMessagePart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        public ImageUrl? ImageUrl { get; set; }
    }

    private sealed class ImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    private sealed class ChatCompletionsResponse
    {
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}