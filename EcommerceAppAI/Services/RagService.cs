using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using EcommerceAppAI.Models;

namespace EcommerceAppAI.Services;

public class RagService
{
    private readonly QdrantClient _qdrantClient;
    private readonly OllamaEmbeddingService _embeddingService;
    private readonly ILogger<RagService> _logger;
    
    private const string ProductsCollection = "products";
    private const string DocumentsCollection = "documents";

    public RagService(
        QdrantClient qdrantClient,
        OllamaEmbeddingService embeddingService,
        ILogger<RagService> logger)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<RagContext> GetRelevantContextAsync(string query, int maxResults = 5)
    {
        try
        {
            _logger.LogInformation("Processing RAG query: {Query}", query);

            // Generate embedding for the user query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
            _logger.LogDebug("Generated query embedding with {Dimensions} dimensions", queryEmbedding.Length);

            // Search for relevant products
            var productResults = await SearchProductsAsync(queryEmbedding, maxResults);
            _logger.LogDebug("Found {Count} relevant products", productResults.Count);

            // Build context from search results
            var context = BuildContextFromResults(query, productResults);
            
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG query: {Query}", query);
            return new RagContext
            {
                OriginalQuery = query,
                ContextText = "I apologize, but I couldn't retrieve relevant information at the moment.",
                RelevantProducts = new List<ProductMatch>(),
                HasResults = false
            };
        }
    }

    private async Task<List<ProductMatch>> SearchProductsAsync(float[] queryEmbedding, int limit)
    {
        try
        {
            // Check if products collection exists
            var collections = await _qdrantClient.ListCollectionsAsync();
            if (!collections.Contains(ProductsCollection))
            {
                _logger.LogWarning("Products collection not found. Please initialize collections first.");
                return new List<ProductMatch>();
            }

            // Perform vector search
            var searchResult = await _qdrantClient.SearchAsync(
                collectionName: ProductsCollection,
                vector: queryEmbedding,
                limit: (ulong)limit,
                scoreThreshold: 0.3f // Only return results with reasonable similarity
            );

            var matches = new List<ProductMatch>();

            foreach (var result in searchResult)
            {
                var match = new ProductMatch
                {
                    Score = result.Score,
                    Product = new Product
                    {
                        Id = (int)result.Id.Num,
                        Name = result.Payload["name"].StringValue,
                        Price = (decimal)result.Payload["price"].DoubleValue,
                        Category = result.Payload["category"].StringValue,
                        Description = result.Payload["description"].StringValue
                    }
                };

                matches.Add(match);
            }

            return matches.OrderByDescending(m => m.Score).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products collection");
            return new List<ProductMatch>();
        }
    }

    private RagContext BuildContextFromResults(string query, List<ProductMatch> productResults)
    {
        var contextBuilder = new StringBuilder();
        
        if (productResults.Any())
        {
            contextBuilder.AppendLine("Here are the most relevant products from our catalog:");
            contextBuilder.AppendLine();

            foreach (var match in productResults.Take(5))
            {
                contextBuilder.AppendLine($"• **{match.Product.Name}** - ${match.Product.Price:F2}");
                contextBuilder.AppendLine($"  Category: {match.Product.Category}");
                contextBuilder.AppendLine($"  Description: {match.Product.Description}");
                contextBuilder.AppendLine($"  Relevance: {match.Score:F2}");
                contextBuilder.AppendLine();
            }
        }
        else
        {
            contextBuilder.AppendLine("No specific products were found matching your query, but I can still help with general information.");
        }

        return new RagContext
        {
            OriginalQuery = query,
            ContextText = contextBuilder.ToString(),
            RelevantProducts = productResults,
            HasResults = productResults.Any()
        };
    }

    public async Task<bool> IsRagReadyAsync()
    {
        try
        {
            var collections = await _qdrantClient.ListCollectionsAsync();
            var hasProducts = collections.Contains(ProductsCollection);
            
            if (!hasProducts)
            {
                _logger.LogWarning("RAG not ready - products collection missing");
                return false;
            }

            // Check if collection has data
            var scrollResult = await _qdrantClient.ScrollAsync(ProductsCollection, limit: 1);
            var hasData = scrollResult.Result.Any();

            if (!hasData)
            {
                _logger.LogWarning("RAG not ready - products collection is empty");
                return false;
            }

            _logger.LogInformation("RAG is ready with product data");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking RAG readiness");
            return false;
        }
    }
}

// Supporting models
public class RagContext
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string ContextText { get; set; } = string.Empty;
    public List<ProductMatch> RelevantProducts { get; set; } = new();
    public bool HasResults { get; set; }
}

public class ProductMatch
{
    public float Score { get; set; }
    public Product Product { get; set; } = new();
}