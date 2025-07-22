using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using EcommerceAppAI.Models;
using EcommerceAppAI.Services;

namespace EcommerceAppAI.Hubs;

public class ChatHub : Hub
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatHub> _logger;
    private readonly LlmSettings _llmSettings;
    private readonly RagService _ragService;

    public ChatHub(
        IHttpClientFactory httpClientFactory, 
        ILogger<ChatHub> logger, 
        IOptions<LlmSettings> llmSettings,
        RagService ragService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _llmSettings = llmSettings.Value;
        _ragService = ragService;
    }

    public async Task SendMessage(string message)
    {
        try
        {
            // Echo user message back to client immediately
            await Clients.Caller.SendAsync("ReceiveMessage", message, "user", DateTime.Now);

            // Indicate AI is typing
            await Clients.Caller.SendAsync("TypingIndicator", true);

            // Check if RAG is ready
            var isRagReady = await _ragService.IsRagReadyAsync();
            
            string systemPrompt;
            string userContent = message;

            if (isRagReady)
            {
                // Get relevant context using RAG
                _logger.LogInformation("Using RAG pipeline for query: {Query}", message);
                var ragContext = await _ragService.GetRelevantContextAsync(message);
                
                // Build enhanced system prompt with context
                systemPrompt = BuildRagSystemPrompt();
                userContent = BuildRagUserPrompt(message, ragContext);
                
                _logger.LogDebug("RAG context built with {ProductCount} products", 
                    ragContext.RelevantProducts.Count);
            }
            else
            {
                // Fallback to basic chat without RAG
                _logger.LogWarning("RAG not ready, using basic chat mode");
                systemPrompt = "You are a helpful AI assistant for an ecommerce platform. " +
                              "The product database is currently not available, but you can still help with general inquiries. " +
                              "Let users know they can initialize the vector database from the Vector DB page to enable product search.";
            }

            var httpClient = _httpClientFactory.CreateClient("LlmClient");

            // Prepare Ollama-compatible request with RAG context
            var ollamaRequest = new
            {
                model = _llmSettings.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                stream = true,
                options = new
                {
                    temperature = _llmSettings.Temperature,
                    num_ctx = _llmSettings.MaxTokens
                }
            };

            var json = JsonSerializer.Serialize(ollamaRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation($"Sending RAG-enhanced request to Ollama for user: {Context.ConnectionId}");

            // Make streaming request to Ollama
            var response = await httpClient.PostAsync("/api/chat", content);

            if (response.IsSuccessStatusCode)
            {
                await ProcessOllamaStreamingResponse(response);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Ollama API error: {response.StatusCode} - {errorContent}");
                await Clients.Caller.SendAsync("ReceiveError", $"Ollama API error: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout occurred while communicating with Ollama");
            await Clients.Caller.SendAsync("ReceiveError", "Request timeout. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while communicating with Ollama");
            await Clients.Caller.SendAsync("ReceiveError", "Unable to connect to Ollama. Please check if Ollama is running on port 11434.");
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

    private string BuildRagSystemPrompt()
    {
        return """
            You are an intelligent ecommerce AI assistant with access to our product catalog. 
            
            Your capabilities:
            - Answer questions about specific products in our inventory
            - Provide product recommendations based on user needs
            - Compare products and highlight features
            - Help with pricing and availability information
            - Assist with general shopping inquiries
            
            Guidelines:
            - Use the provided product context to give accurate, specific answers
            - When recommending products, explain why they match the user's needs
            - If no relevant products are found, acknowledge this and offer general guidance
            - Be friendly, helpful, and professional
            - Always provide accurate pricing information when available
            - Mention product categories when relevant
            """;
    }

    private string BuildRagUserPrompt(string originalQuery, RagContext ragContext)
    {
        var promptBuilder = new StringBuilder();
        
        if (ragContext.HasResults)
        {
            promptBuilder.AppendLine("PRODUCT CONTEXT:");
            promptBuilder.AppendLine(ragContext.ContextText);
            promptBuilder.AppendLine("---");
            promptBuilder.AppendLine();
        }
        
        promptBuilder.AppendLine($"USER QUESTION: {originalQuery}");
        
        return promptBuilder.ToString();
    }

    private async Task ProcessOllamaStreamingResponse(HttpResponseMessage response)
    {
        var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream);

        var isFirstChunk = true;
        var fullResponse = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                
                // Check if the response is done
                if (doc.RootElement.TryGetProperty("done", out var doneElement) && 
                    doneElement.GetBoolean())
                {
                    await Clients.Caller.SendAsync("FinalizeMessage");
                    _logger.LogInformation($"Ollama streaming completed. Full response length: {fullResponse.Length}");
                    break;
                }

                // Extract message content
                if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                    messageElement.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString();
                    
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (isFirstChunk)
                        {
                            await Clients.Caller.SendAsync("StartMessage", DateTime.Now);
                            isFirstChunk = false;
                        }
                        
                        fullResponse.Append(content);
                        await Clients.Caller.SendAsync("ReceiveChunk", content);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, $"Failed to parse Ollama streaming response: {line}");
            }
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        
        var isRagReady = await _ragService.IsRagReadyAsync();
        var welcomeMessage = isRagReady 
            ? "Hello! I'm your AI assistant with access to our product catalog. I can help you find products, compare features, and answer questions about our inventory. How can I help you today?"
            : "Hello! I'm your AI assistant. The product database is currently not available, but I can still help with general inquiries. Visit the Vector DB page to initialize the product catalog for enhanced shopping assistance.";
            
        await Clients.Caller.SendAsync("ReceiveMessage", welcomeMessage, "assistant", DateTime.Now);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }
}