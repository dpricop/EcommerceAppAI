using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using EcommerceAppAI.Models;

namespace EcommerceAppAI.Hubs;

public class ChatHub : Hub
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatHub> _logger;
    private readonly LlmSettings _llmSettings;

    public ChatHub(IHttpClientFactory httpClientFactory, ILogger<ChatHub> logger, IOptions<LlmSettings> llmSettings)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _llmSettings = llmSettings.Value;
    }

    public async Task SendMessage(string message)
    {
        try
        {
            // Echo user message back to client immediately
            await Clients.Caller.SendAsync("ReceiveMessage", message, "user", DateTime.Now);

            // Indicate AI is typing
            await Clients.Caller.SendAsync("TypingIndicator", true);

            var httpClient = _httpClientFactory.CreateClient("LlmClient");

            // Prepare streaming request for LLM
            var llmRequest = new
            {
                model = _llmSettings.ModelName,
                messages = new[]
                {
                    new { role = "user", content = message }
                },
                temperature = _llmSettings.Temperature,
                max_tokens = _llmSettings.MaxTokens,
                stream = true // Enable streaming
            };

            var json = JsonSerializer.Serialize(llmRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation($"Sending streaming request to LLM for user: {Context.ConnectionId}");

            // Make streaming request to LLM
            var response = await httpClient.PostAsync("/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                await ProcessStreamingResponse(response);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"LLM API error: {response.StatusCode} - {errorContent}");
                await Clients.Caller.SendAsync("ReceiveError", $"LLM API error: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout occurred while communicating with LLM");
            await Clients.Caller.SendAsync("ReceiveError", "Request timeout. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while communicating with LLM");
            await Clients.Caller.SendAsync("ReceiveError", "Unable to connect to LLM. Please check if LM Studio is running.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SendMessage");
            await Clients.Caller.SendAsync("ReceiveError", "An unexpected error occurred.");
        }
        finally
        {
            await Clients.Caller.SendAsync("TypingIndicator", false);
        }
    }

    private async Task ProcessStreamingResponse(HttpResponseMessage response)
    {
        var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream);

        var isFirstChunk = true;

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6); // Remove "data: " prefix

            if (data == "[DONE]")
            {
                await Clients.Caller.SendAsync("FinalizeMessage");
                break;
            }

            try
            {
                using var doc = JsonDocument.Parse(data);
                
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString();
                    
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (isFirstChunk)
                        {
                            await Clients.Caller.SendAsync("StartMessage", DateTime.Now);
                            isFirstChunk = false;
                        }
                        await Clients.Caller.SendAsync("ReceiveChunk", content);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, $"Failed to parse streaming response: {data}");
            }
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        await Clients.Caller.SendAsync("ReceiveMessage", "Hello! I'm your AI assistant. How can I help you today?", "assistant", DateTime.Now);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }
}