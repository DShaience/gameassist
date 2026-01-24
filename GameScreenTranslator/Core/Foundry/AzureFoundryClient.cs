using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Conversation;
using GameScreenTranslator.Core.Models;
using GameScreenTranslator.Core.State;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Foundry;

/// <summary>
/// Client for Azure Foundry / OpenAI API.
/// Orchestrates HTTP communication, request building, and response parsing.
/// </summary>
public sealed class AzureFoundryClient : IFoundryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AppSettings.AzureFoundrySettings _settings;
    private readonly FoundryRequestBuilder _requestBuilder;

    public AzureFoundryClient(AppSettings.AzureFoundrySettings settings, PromptPack prompts, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (prompts is null) throw new ArgumentNullException(nameof(prompts));

        _requestBuilder = new FoundryRequestBuilder(prompts);

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
        var downscaleWidth = downscaleWidthOverride ?? 640;
        return FoundryRequestBuilder.PrepareScreenshotDataUrl(screenshot, downscaleWidth);
    }

    /// <summary>
    /// Builds the user prompt text from the template.
    /// </summary>
    public string BuildUserPromptText(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string? userQuestion = null)
    {
        return _requestBuilder.BuildUserPromptText(windowTitle, priority, targetLanguage, userQuestion);
    }

    /// <summary>
    /// Gets the system prompt text from the prompt pack.
    /// </summary>
    public string GetSystemPrompt() => _requestBuilder.GetSystemPrompt();

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

        var foundryMessages = FoundryRequestBuilder.BuildRequestMessages(messages);

        var request = new ChatCompletionsRequest
        {
            Messages = ConvertToApiMessages(foundryMessages),
            Temperature = 0.2,
            MaxTokens = 800,
            MaxCompletionTokens = null,
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        var url = BuildUrl(deployment);
        return await ExecuteRequestAsync(request, url, onRawModelContent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a summary of conversation text using the same model (text-only request).
    /// </summary>
    public async Task<string> GenerateSummaryAsync(string textToSummarize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textToSummarize))
            return "";

        var deployment = _settings.LLM.VisionDeployment;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Providers:AzureFoundry:LLM:VisionDeployment is required.");

        var foundryMessages = FoundryRequestBuilder.BuildSummaryMessages(textToSummarize);

        var request = new ChatCompletionsRequest
        {
            Messages = ConvertToApiMessages(foundryMessages),
            Temperature = 0.3,
            MaxTokens = 400,
            MaxCompletionTokens = null,
            ResponseFormat = null // Plain text response
        };

        var url = BuildUrl(deployment);
        return await ExecuteSummaryRequestAsync(request, url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranslateCycleResponse> TranslateCycleAsync(
        Image<Rgba32> screenshot,
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string? userQuestion = null,
        int? downscaleWidthOverride = null,
        Action<string>? onRawModelContent = null,
        CancellationToken cancellationToken = default)
    {
        if (screenshot is null) throw new ArgumentNullException(nameof(screenshot));
        if (string.IsNullOrWhiteSpace(windowTitle)) throw new ArgumentException("Window title cannot be empty.", nameof(windowTitle));
        if (string.IsNullOrWhiteSpace(targetLanguage)) throw new ArgumentException("Target language cannot be empty.", nameof(targetLanguage));

        var deployment = _settings.LLM.VisionDeployment;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Providers:AzureFoundry:LLM:VisionDeployment is required for vision calls.");

        var downscaleWidth = downscaleWidthOverride ?? 640;
        var imageDataUrl = FoundryRequestBuilder.PrepareScreenshotDataUrl(screenshot, downscaleWidth);

        var foundryMessages = _requestBuilder.BuildTranslateMessages(
            windowTitle, priority, targetLanguage, userQuestion, imageDataUrl);

        var request = new ChatCompletionsRequest
        {
            Messages = ConvertToApiMessages(foundryMessages),
            Temperature = 0.2,
            MaxTokens = 800,
            MaxCompletionTokens = null,
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        var url = BuildUrl(deployment);
        return await ExecuteRequestAsync(request, url, onRawModelContent, cancellationToken).ConfigureAwait(false);
    }

    #region HTTP Execution

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
                    ApplyCompatibilityFixes(request, response, responseBody);
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

                // Populate token usage from API response (not from model content)
                output.PromptTokens = parsed.Usage?.PromptTokens;
                output.CompletionTokens = parsed.Usage?.CompletionTokens;

                FoundryResponseParser.NormalizeOutput(output);
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

    private async Task<string> ExecuteSummaryRequestAsync(
        ChatCompletionsRequest request,
        string url,
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
                    ApplyCompatibilityFixes(request, response, responseBody);
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

    /// <summary>
    /// Auto-fix common API compatibility issues and allow retry.
    /// </summary>
    private static void ApplyCompatibilityFixes(ChatCompletionsRequest request, HttpResponseMessage response, string responseBody)
    {
        if ((int)response.StatusCode != 400)
            return;

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
            || (responseBody.Contains("temperature", StringComparison.OrdinalIgnoreCase) &&
                responseBody.Contains("Only the default (1) value is supported", StringComparison.OrdinalIgnoreCase)))
        {
            request.Temperature = null;
        }
    }

    #endregion

    #region Helpers

    private string BuildUrl(string deployment)
    {
        return $"openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(_settings.ApiVersion)}";
    }

    private static ChatMessage[] ConvertToApiMessages(List<FoundryMessage> foundryMessages)
    {
        var result = new ChatMessage[foundryMessages.Count];
        for (var i = 0; i < foundryMessages.Count; i++)
        {
            var fm = foundryMessages[i];
            var parts = new ChatMessagePart[fm.Parts.Count];
            for (var j = 0; j < fm.Parts.Count; j++)
            {
                var p = fm.Parts[j];
                parts[j] = p.Type == "image_url"
                    ? new ChatMessagePart { Type = "image_url", ImageUrl = new ImageUrl { Url = p.ImageUrl! } }
                    : new ChatMessagePart { Type = "text", Text = p.Text };
            }

            result[i] = new ChatMessage { Role = fm.Role, Content = parts };
        }

        return result;
    }

    private static string EnsureTrailingSlash(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return endpoint;

        return endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/";
    }

    #endregion

    #region API DTOs

    private sealed class ChatCompletionsRequest
    {
        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; } = 0.2;

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

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
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

    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }
    }

    #endregion
}
