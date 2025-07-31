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

            // ALWAYS use RAG - no routing, no if statements
            var systemPrompt = BuildStrictSystemPrompt();
            var userPrompt = await BuildContextualUserPrompt(message);

            // Generate response using Ollama
            await ProcessOllamaRequest(systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendMessage");
            await Clients.Caller.SendAsync("ReceiveError", "I'm having trouble processing your request right now.");
        }
        finally
        {
            await Clients.Caller.SendAsync("TypingIndicator", false);
        }
    }

    private string BuildStrictSystemPrompt()
    {
        return """
            You are a professional ecommerce store assistant. Follow these rules STRICTLY:

            CRITICAL RULES:
            1. Use ONLY the information provided in the STORE CATALOG INFORMATION section
            2. If information is not in the provided context, say "I don't have that information in our current catalog"
            3. NEVER make up product names, prices, or categories that aren't listed
            4. NEVER add products from your general knowledge
            5. For categories, list ONLY the categories shown in the context
            6. For product counts, use ONLY the numbers provided in the context

            RESPONSE STYLE:
            - Be helpful and conversational
            - Use the exact product names and prices from the context
            - If asked about categories, list only the categories from "CATEGORY BREAKDOWN"
            - If asked about products, use only products from "RELEVANT PRODUCTS"
            - When you don't have specific information, be honest about it

            EXAMPLES:
            ✅ Good: "We have 3 categories: Electronics, Footwear, and Clothing"
            ❌ Bad: "We have Electronics, Footwear, Clothing, and other common categories like Books, Home & Garden"

            ✅ Good: "I don't have information about shipping times in our current catalog"
            ❌ Bad: "Typically shipping takes 3-5 business days" (making up info)
            """;
    }

    private async Task<string> BuildContextualUserPrompt(string userMessage)
    {
        try
        {
            // ALWAYS get context from RAG - no exceptions
            var ragContext = await _ragService.GetRelevantContextAsync(userMessage);
            
            var promptBuilder = new StringBuilder();
            
            if (ragContext.HasResults)
            {
                promptBuilder.AppendLine(ragContext.ContextText);
                promptBuilder.AppendLine("=== END OF STORE INFORMATION ===");
                promptBuilder.AppendLine();
            }
            else
            {
                promptBuilder.AppendLine("=== STORE CATALOG INFORMATION ===");
                promptBuilder.AppendLine("No catalog information is currently available.");
                promptBuilder.AppendLine("=== END OF STORE INFORMATION ===");
                promptBuilder.AppendLine();
            }
            
            promptBuilder.AppendLine($"CUSTOMER QUESTION: {userMessage}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Respond based ONLY on the store information provided above. If the information needed to answer the question is not available above, clearly state that you don't have that information.");
            
            return promptBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building contextual prompt");
            
            // Fallback prompt when RAG fails
            return $"""
                === STORE CATALOG INFORMATION ===
                Sorry, I cannot access the product catalog right now.
                === END OF STORE INFORMATION ===
                
                CUSTOMER QUESTION: {userMessage}
                
                Please let the customer know that you cannot access the product catalog at the moment and suggest they try again later.
                """;
        }
    }

    private async Task ProcessOllamaRequest(string systemPrompt, string userPrompt)
    {
        var httpClient = _httpClientFactory.CreateClient("LlmClient");

        var ollamaRequest = new
        {
            model = _llmSettings.ModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = true,
            options = new
            {
                temperature = 0.1, // Lower temperature for more factual responses
                num_ctx = _llmSettings.MaxTokens,
                top_p = 0.9, // More focused responses
                repeat_penalty = 1.1 // Avoid repetition
            }
        };

        var json = JsonSerializer.Serialize(ollamaRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation($"Sending RAG-enhanced request to Ollama for user: {Context.ConnectionId}");

        var response = await httpClient.PostAsync("/api/chat", content);

        if (response.IsSuccessStatusCode)
        {
            await ProcessOllamaStreamingResponse(response);
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Ollama API error: {response.StatusCode} - {errorContent}");
            await Clients.Caller.SendAsync("ReceiveError", $"Unable to connect to the AI service. Please try again.");
        }
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
                
                if (doc.RootElement.TryGetProperty("done", out var doneElement) && 
                    doneElement.GetBoolean())
                {
                    await Clients.Caller.SendAsync("FinalizeMessage");
                    _logger.LogInformation($"Ollama streaming completed. Full response length: {fullResponse.Length}");
                    break;
                }

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
        
        string welcomeMessage;
        if (isRagReady)
        {
            welcomeMessage = """
                Hello! I'm your store assistant. I can help you with:
                
                🛍️ **Product Information**: Ask about our products, categories, and pricing
                🔍 **Product Search**: Find specific items you're looking for
                📊 **Catalog Questions**: Learn about our inventory and categories
                
                I use only information from our actual product catalog, so you'll get accurate, up-to-date answers!
                
                What can I help you find today?
                """;
        }
        else
        {
            welcomeMessage = """
                Hello! I'm your store assistant, but I currently cannot access our product catalog.
                
                Please visit the Vector DB page to initialize the product database, then I'll be able to help you with:
                - Product searches and recommendations
                - Category information
                - Pricing and availability
                
                How else can I assist you today?
                """;
        }
            
        await Clients.Caller.SendAsync("ReceiveMessage", welcomeMessage, "assistant", DateTime.Now);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }
}