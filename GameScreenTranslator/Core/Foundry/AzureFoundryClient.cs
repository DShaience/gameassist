using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Conversation;
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
    private readonly PromptPack _prompts;

    public AzureFoundryClient(AppSettings.AzureFoundrySettings settings, PromptPack prompts, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));

        // Note: Azure Foundry "model inference" endpoint is OpenAI-compatible. We call:
        //   POST /openai/deployments/{deployment}/chat/completions?api-version=...
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(EnsureTrailingSlash(_settings.Endpoint));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);

        _http.Timeout = TimeSpan.FromSeconds(_settings.LLM.TimeoutSeconds);
    }

    /// <summary>
    /// Prepares the screenshot as a base64 data URL for use in conversation context.
    /// </summary>
    public string PrepareScreenshotDataUrl(Image<Rgba32> screenshot, int? downscaleWidthOverride = null)
    {
        if (screenshot is null) throw new ArgumentNullException(nameof(screenshot));

        var downscaleWidth = downscaleWidthOverride ?? 640;
        using var downscaled = ImageDownscaler.DownscaleToWidth(screenshot, downscaleWidth);
        var pngBytes = ImageDownscaler.EncodePng(downscaled);
        return "data:image/png;base64," + Convert.ToBase64String(pngBytes);
    }

    /// <summary>
    /// Builds the user prompt text from the template.
    /// </summary>
    public string BuildUserPromptText(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string mode,
        string? userQuestion = null)
    {
        return BuildUserPrompt(windowTitle, priority, targetLanguage, mode, userQuestion, _prompts);
    }

    /// <summary>
    /// Gets the system prompt text from the prompt pack.
    /// </summary>
    public string GetSystemPrompt() => _prompts.Foundry.System;

    /// <summary>
    /// Sends a chat completion request with pre-built messages (for conversation context support).
    /// </summary>
    public async Task<TranslateCycleResponse> SendWithMessagesAsync(
        IReadOnlyList<ChatMessageDto> messages,
        Action<string>? onRawModelContent = null,
        CancellationToken cancellationToken = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be empty.", nameof(messages));

        var deployment = _settings.LLM.VisionDeployment;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Providers:AzureFoundry:LLM:VisionDeployment is required for vision calls.");

        var requestMessages = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            var parts = new List<ChatMessagePart>();
            if (!string.IsNullOrEmpty(msg.TextContent))
            {
                parts.Add(new ChatMessagePart { Type = "text", Text = msg.TextContent });
            }
            if (msg.HasImage)
            {
                parts.Add(new ChatMessagePart { Type = "image_url", ImageUrl = new ImageUrl { Url = msg.ImageDataUrl! } });
            }

            requestMessages.Add(new ChatMessage
            {
                Role = msg.Role,
                Content = parts.ToArray()
            });
        }

        var request = new ChatCompletionsRequest
        {
            Messages = requestMessages.ToArray(),
            Temperature = 0.2,
            MaxTokens = 800,
            MaxCompletionTokens = null,
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        var url = $"openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(_settings.ApiVersion)}";

        return await ExecuteRequestAsync(request, url, onRawModelContent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a summary of conversation text using the same model (text-only request).
    /// </summary>
    public async Task<string> GenerateSummaryAsync(string textToSummarize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textToSummarize))
            return "";

        var deployment = _settings.LLM.VisionDeployment; // Use same deployment for simplicity
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Providers:AzureFoundry:LLM:VisionDeployment is required.");

        var systemPrompt = "You are a helpful assistant that summarizes conversations. Create a brief, factual summary of the key points from the conversation below. Focus on: questions asked, answers given, and any important context about the game or situation. Keep the summary concise (under 300 words).";

        var request = new ChatCompletionsRequest
        {
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = [new ChatMessagePart { Type = "text", Text = systemPrompt }]
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = [new ChatMessagePart { Type = "text", Text = $"Please summarize this conversation:\n\n{textToSummarize}" }]
                }
            ],
            Temperature = 0.3,
            MaxTokens = 400,
            MaxCompletionTokens = null,
            ResponseFormat = null // Plain text response
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
                    // Auto-fix common compatibility issues
                    if ((int)response.StatusCode == 400)
                    {
                        if (responseBody.Contains("Unsupported parameter: 'max_tokens'", StringComparison.OrdinalIgnoreCase))
                        {
                            request.MaxCompletionTokens = request.MaxTokens;
                            request.MaxTokens = null;
                        }
                        if (responseBody.Contains("Unsupported value: 'temperature'", StringComparison.OrdinalIgnoreCase)
                            || responseBody.Contains("temperature", StringComparison.OrdinalIgnoreCase) &&
                               responseBody.Contains("Only the default (1) value is supported", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Temperature = null;
                        }
                    }
                    throw new InvalidOperationException($"Summary generation failed: HTTP {(int)response.StatusCode}. Body: {responseBody}");
                }

                var parsed = JsonSerializer.Deserialize<ChatCompletionsResponse>(responseBody, JsonOptions)
                             ?? throw new InvalidOperationException("Summary call returned empty response.");

                var modelContent = parsed.Choices?[0]?.Message?.Content;
                return modelContent?.Trim() ?? "";
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

        throw new InvalidOperationException("Summary generation failed after retries.", last);
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

        var systemText = _prompts.Foundry.System;

        var userText = BuildUserPrompt(windowTitle, priority, targetLanguage, mode, userQuestion, _prompts);

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

        return await ExecuteRequestAsync(request, url, onRawModelContent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranslateCycleResponse> ExecuteRequestAsync(
        ChatCompletionsRequest request,
        string url,
        Action<string>? onRawModelContent,
        CancellationToken cancellationToken)
    {

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
        string? userQuestion,
        PromptPack prompts)
    {
        var template = string.IsNullOrWhiteSpace(userQuestion)
            ? prompts.Foundry.TranslateUser
            : prompts.Foundry.QuestionUser;

        var values = new Dictionary<string, string?>
        {
            ["windowTitle"] = windowTitle,
            ["priority"] = priority.ToString(),
            ["targetLanguage"] = targetLanguage,
            ["mode"] = mode,
            ["userQuestion"] = userQuestion
        };

        return PromptTemplate.Render(template, values);
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
