using System.Text;
using System.Text.RegularExpressions;
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

    public RagService(
        QdrantClient qdrantClient,
        OllamaEmbeddingService embeddingService,
        ILogger<RagService> logger)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<RagContext> GetRelevantContextAsync(string query, int maxResults = 10)
    {
        try
        {
            _logger.LogInformation("Processing RAG query: {Query}", query);

            // 1. Get all products (we need full dataset for filtering)
            var allProducts = await GetAllProducts();
            
            if (!allProducts.Any())
            {
                return new RagContext
                {
                    OriginalQuery = query,
                    ContextText = "No products are currently available in our catalog.",
                    HasResults = false
                };
            }

            // 2. Parse query for filtering criteria
            var queryFilter = ParseQueryFilters(query);
            
            // 3. Apply filters to get relevant products
            var filteredProducts = ApplyQueryFilters(allProducts, queryFilter);
            
            // 4. If we have specific filters, also do semantic search for ranking
            var rankedProducts = filteredProducts;
            if (filteredProducts.Any() && filteredProducts.Count > 1)
            {
                rankedProducts = await RankProductsBySemantic(query, filteredProducts);
            }
            
            // 5. Get general stats for context
            var generalStats = CalculateProductStats(allProducts);
            var filteredStats = CalculateProductStats(filteredProducts);
            
            // 6. Build intelligent context
            var context = BuildQueryAwareContext(query, queryFilter, rankedProducts, generalStats, filteredStats);
            
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG query: {Query}", query);
            return new RagContext
            {
                OriginalQuery = query,
                ContextText = "I apologize, but I cannot access the product database right now.",
                HasResults = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<List<Product>> GetAllProducts()
    {
        try
        {
            var collections = await _qdrantClient.ListCollectionsAsync();
            if (!collections.Contains(ProductsCollection))
                return new List<Product>();

            var scrollResult = await _qdrantClient.ScrollAsync(ProductsCollection, limit: 100);
            
            var products = new List<Product>();
            foreach (var point in scrollResult.Result)
            {
                var product = new Product
                {
                    Id = (int)point.Id.Num,
                    Name = point.Payload["name"].StringValue,
                    Price = (decimal)point.Payload["price"].DoubleValue,
                    Category = point.Payload["category"].StringValue,
                    Description = point.Payload["description"].StringValue
                };
                products.Add(product);
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all products");
            return new List<Product>();
        }
    }

    private QueryFilter ParseQueryFilters(string query)
    {
        var filter = new QueryFilter();
        var lowerQuery = query.ToLower();

        // Parse price filters
        var pricePatterns = new[]
        {
            @"under\s*\$?(\d+)", // "under $500", "under 500"
            @"below\s*\$?(\d+)", // "below $500"
            @"less than\s*\$?(\d+)", // "less than $500"
            @"<\s*\$?(\d+)", // "< $500"
        };

        foreach (var pattern in pricePatterns)
        {
            var match = Regex.Match(lowerQuery, pattern);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var maxPrice))
            {
                filter.MaxPrice = maxPrice;
                break;
            }
        }

        var minPricePatterns = new[]
        {
            @"over\s*\$?(\d+)", // "over $500"
            @"above\s*\$?(\d+)", // "above $500"
            @"more than\s*\$?(\d+)", // "more than $500"
            @">\s*\$?(\d+)", // "> $500"
        };

        foreach (var pattern in minPricePatterns)
        {
            var match = Regex.Match(lowerQuery, pattern);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var minPrice))
            {
                filter.MinPrice = minPrice;
                break;
            }
        }

        // Parse category filters
        var knownCategories = new[] { "electronics", "footwear", "clothing" };
        foreach (var category in knownCategories)
        {
            if (lowerQuery.Contains(category))
            {
                filter.Category = category;
                break;
            }
        }

        // Parse product name searches
        var productKeywords = new[] { "iphone", "macbook", "airpods", "nike", "adidas", "samsung", "sony" };
        foreach (var keyword in productKeywords)
        {
            if (lowerQuery.Contains(keyword))
            {
                filter.NameKeywords.Add(keyword);
            }
        }

        return filter;
    }

    private List<Product> ApplyQueryFilters(List<Product> products, QueryFilter filter)
    {
        var filtered = products.AsEnumerable();

        // Apply price filters
        if (filter.MinPrice.HasValue)
        {
            filtered = filtered.Where(p => p.Price >= filter.MinPrice.Value);
        }

        if (filter.MaxPrice.HasValue)
        {
            filtered = filtered.Where(p => p.Price <= filter.MaxPrice.Value);
        }

        // Apply category filter
        if (!string.IsNullOrEmpty(filter.Category))
        {
            filtered = filtered.Where(p => p.Category.ToLower().Contains(filter.Category.ToLower()));
        }

        // Apply name keyword filters
        if (filter.NameKeywords.Any())
        {
            filtered = filtered.Where(p => 
                filter.NameKeywords.Any(keyword => 
                    p.Name.ToLower().Contains(keyword) || 
                    p.Description.ToLower().Contains(keyword)));
        }

        return filtered.ToList();
    }

    private async Task<List<Product>> RankProductsBySemantic(string query, List<Product> products)
    {
        try
        {
            if (products.Count <= 1) return products;

            // Generate embedding for the query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
            
            // For simplicity, we'll rank by price for now
            // In a more sophisticated system, you'd rank by semantic similarity
            return products.OrderBy(p => p.Price).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ranking products semantically");
            return products;
        }
    }

    private ProductStats CalculateProductStats(List<Product> products)
    {
        if (!products.Any())
            return new ProductStats();

        var prices = products.Select(p => p.Price).ToList();
        var categories = products
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ProductStats
        {
            TotalCount = products.Count,
            Categories = categories,
            MinPrice = prices.Min(),
            MaxPrice = prices.Max(),
            AvgPrice = prices.Average()
        };
    }

    private RagContext BuildQueryAwareContext(string query, QueryFilter filter, 
        List<Product> filteredProducts, ProductStats generalStats, ProductStats filteredStats)
    {
        var contextBuilder = new StringBuilder();

        // Start with general store information
        contextBuilder.AppendLine("=== STORE CATALOG INFORMATION ===");
        contextBuilder.AppendLine($"Total Products in Store: {generalStats.TotalCount}");
        contextBuilder.AppendLine($"All Categories: {string.Join(", ", generalStats.Categories.Keys)}");
        contextBuilder.AppendLine();

        // Show filter-specific results
        if (filter.HasFilters())
        {
            contextBuilder.AppendLine("=== FILTERED RESULTS FOR YOUR QUERY ===");
            contextBuilder.AppendLine($"Matching Products Found: {filteredStats.TotalCount}");
            
            if (filter.MaxPrice.HasValue)
            {
                contextBuilder.AppendLine($"Price Filter: Under ${filter.MaxPrice.Value}");
            }
            if (filter.MinPrice.HasValue)
            {
                contextBuilder.AppendLine($"Price Filter: Over ${filter.MinPrice.Value}");
            }
            if (!string.IsNullOrEmpty(filter.Category))
            {
                contextBuilder.AppendLine($"Category Filter: {filter.Category}");
            }
            
            contextBuilder.AppendLine();

            if (filteredProducts.Any())
            {
                contextBuilder.AppendLine("=== MATCHING PRODUCTS ===");
                foreach (var product in filteredProducts.Take(8))
                {
                    contextBuilder.AppendLine($"• {product.Name} - ${product.Price:F2} ({product.Category})");
                    contextBuilder.AppendLine($"  Description: {product.Description}");
                    contextBuilder.AppendLine();
                }
            }
            else
            {
                contextBuilder.AppendLine("=== NO MATCHING PRODUCTS ===");
                contextBuilder.AppendLine("No products match your specific criteria.");
                contextBuilder.AppendLine();
            }
        }
        else
        {
            // No specific filters, show general catalog
            contextBuilder.AppendLine("=== CATEGORY BREAKDOWN ===");
            foreach (var category in generalStats.Categories.OrderByDescending(c => c.Value))
            {
                contextBuilder.AppendLine($"{category.Key}: {category.Value} products");
            }
            contextBuilder.AppendLine();

            if (filteredProducts.Any())
            {
                contextBuilder.AppendLine("=== FEATURED PRODUCTS ===");
                foreach (var product in filteredProducts.Take(5))
                {
                    contextBuilder.AppendLine($"• {product.Name} - ${product.Price:F2} ({product.Category})");
                    contextBuilder.AppendLine($"  Description: {product.Description}");
                    contextBuilder.AppendLine();
                }
            }
        }

        return new RagContext
        {
            OriginalQuery = query,
            ContextText = contextBuilder.ToString(),
            RelevantProducts = filteredProducts.Select(p => new ProductMatch { Product = p, Score = 1.0f }).ToList(),
            AllCategories = generalStats.Categories.Keys.ToList(),
            ProductStats = generalStats,
            FilteredStats = filteredStats,
            QueryFilter = filter,
            HasResults = generalStats.TotalCount > 0
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

// Enhanced supporting models
public class RagContext
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string ContextText { get; set; } = string.Empty;
    public List<ProductMatch> RelevantProducts { get; set; } = new();
    public List<string> AllCategories { get; set; } = new();
    public ProductStats ProductStats { get; set; } = new();
    public ProductStats FilteredStats { get; set; } = new();
    public QueryFilter QueryFilter { get; set; } = new();
    public bool HasResults { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class QueryFilter
{
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> NameKeywords { get; set; } = new();

    public bool HasFilters()
    {
        return MinPrice.HasValue || MaxPrice.HasValue || 
               !string.IsNullOrEmpty(Category) || NameKeywords.Any();
    }
}

public class ProductMatch
{
    public float Score { get; set; }
    public Product Product { get; set; } = new();
}

public class ProductStats
{
    public int TotalCount { get; set; }
    public Dictionary<string, int> Categories { get; set; } = new();
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal AvgPrice { get; set; }
}