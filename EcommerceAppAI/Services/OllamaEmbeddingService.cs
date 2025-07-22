using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using EcommerceAppAI.Models;

namespace EcommerceAppAI.Services;

public class OllamaEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmSettings _settings;
    private readonly ILogger<OllamaEmbeddingService> _logger;

    public OllamaEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmSettings> settings,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be null or empty", nameof(text));
            }

            var client = _httpClientFactory.CreateClient("LlmClient");

            var request = new
            {
                model = _settings.EmbeddingModel,
                prompt = text.Trim()
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Generating embedding for text: {TextPreview}...", 
                text.Length > 50 ? text[..50] + "..." : text);

            var response = await client.PostAsync("/api/embeddings", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Embedding generation failed: {StatusCode} - {Error}", 
                    response.StatusCode, error);
                throw new HttpRequestException($"Failed to generate embedding: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("embedding", out var embeddingElement))
            {
                throw new InvalidOperationException("No embedding found in response");
            }

            var embedding = embeddingElement.EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            _logger.LogDebug("Generated embedding with {Dimensions} dimensions", embedding.Length);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {Text}", text);
            throw;
        }
    }

    public async Task<float[][]> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        var tasks = texts.Select(GenerateEmbeddingAsync);
        return await Task.WhenAll(tasks);
    }
}