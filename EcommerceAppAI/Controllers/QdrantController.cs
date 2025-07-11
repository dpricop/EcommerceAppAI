using EcommerceAppAI.Models;
using EcommerceAppAI.Services;
using Microsoft.AspNetCore.Mvc;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace EcommerceAppAI.Controllers;

public class QdrantController : Controller
{
    private readonly QdrantConnectionService _qdrantService;
    private readonly QdrantClient _qdrantClient;
    private readonly ILogger<QdrantController> _logger;
    
    // Collection names
    private const string ProductsCollection = "products";
    private const string DocumentsCollection = "documents";

    public QdrantController(QdrantConnectionService qdrantService, QdrantClient qdrantClient, ILogger<QdrantController> logger)
    {
        _qdrantService = qdrantService;
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var model = new QdrantViewModel();
        
        try
        {
            _logger.LogInformation("Qdrant Index called");
            
            // Test connection
            model.IsConnected = await _qdrantService.TestConnectionAsync();
            model.ConnectionInfo = await _qdrantService.GetQdrantInfoAsync();
            model.Collections = await GetCollectionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Qdrant connection in controller");
            model.IsConnected = false;
            model.ConnectionInfo = $"Error: {ex.Message}";
            model.Collections = new List<string>();
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> InitializeCollections()
    {
        try
        {
            _logger.LogInformation("Starting collection initialization...");
            
            // Create both collections
            var productsCreated = await CreateCollectionIfNotExistsAsync(ProductsCollection);
            var documentsCreated = await CreateCollectionIfNotExistsAsync(DocumentsCollection);
            
            if (productsCreated && documentsCreated)
            {
                // Add mock product data
                await AddMockProductDataAsync();
                
                TempData["Success"] = "Collections initialized successfully! Vector database is ready with products and documents collections, including mock product data.";
                _logger.LogInformation("Successfully initialized both collections with mock data");
            }
            else
            {
                TempData["Error"] = "Failed to initialize collections. Please check Qdrant connection.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing collections");
            TempData["Error"] = $"Error initializing collections: {ex.Message}";
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> DropCollections()
    {
        try
        {
            _logger.LogInformation("Starting collections deletion...");
            
            // Get list of all collections first
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            int deletedCount = 0;
            var deletedCollections = new List<string>();
            
            // Delete each collection
            foreach (var collectionName in collections)
            {
                try
                {
                    await _qdrantClient.DeleteCollectionAsync(collectionName);
                    deletedCollections.Add(collectionName);
                    deletedCount++;
                    _logger.LogInformation("Successfully deleted collection: {CollectionName}", collectionName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete collection: {CollectionName}", collectionName);
                }
            }
            
            if (deletedCount > 0)
            {
                TempData["Success"] = $"Successfully deleted {deletedCount} collection(s): {string.Join(", ", deletedCollections)}. Vector database has been reset.";
            }
            else if (collections.Count == 0)
            {
                TempData["Success"] = "No collections found to delete. Vector database is already empty.";
            }
            else
            {
                TempData["Error"] = "Failed to delete any collections. Please check the logs for details.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dropping collections");
            TempData["Error"] = $"Error dropping collections: {ex.Message}";
        }

        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> GetConnectionStatus()
    {
        try
        {
            var isConnected = await _qdrantService.TestConnectionAsync();
            var info = await _qdrantService.GetQdrantInfoAsync();
            
            return Json(new { 
                isConnected = isConnected, 
                info = info,
                timestamp = DateTime.Now.ToString("HH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            return Json(new { 
                isConnected = false, 
                info = $"Error: {ex.Message}",
                timestamp = DateTime.Now.ToString("HH:mm:ss")
            });
        }
    }

    // Private helper methods for collection operations
    private async Task<bool> CreateCollectionIfNotExistsAsync(string collectionName)
    {
        try
        {
            // Check if collection already exists
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            if (collections.Contains(collectionName))
            {
                _logger.LogInformation("Collection {CollectionName} already exists", collectionName);
                return true;
            }
            
            // Create collection with vector configuration
            await _qdrantClient.CreateCollectionAsync(collectionName, new VectorParams
            {
                Size = 384, // Standard embedding dimension for sentence transformers
                Distance = Distance.Cosine
            });
            
            _logger.LogInformation("Successfully created collection: {CollectionName}", collectionName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection {CollectionName}", collectionName);
            return false;
        }
    }
    
    private async Task AddMockProductDataAsync()
    {
        try
        {
            _logger.LogInformation("Adding mock product data to {ProductsCollection}", ProductsCollection);
            
            // Check if products already exist
            var existingPoints = await _qdrantClient.ScrollAsync(ProductsCollection, limit: 1);
            if (existingPoints.Result.Any())
            {
                _logger.LogInformation("Product data already exists, skipping mock data insertion");
                return;
            }
            
            var mockProducts = GetMockProducts();
            var points = new List<PointStruct>();
            
            foreach (var product in mockProducts)
            {
                // Generate simple embeddings (in real scenario, use actual embedding service)
                var vector = GenerateMockEmbedding(product);
                
                var point = new PointStruct
                {
                    Id = (ulong)product.Id,
                    Vectors = vector,
                    Payload = 
                    {
                        ["name"] = product.Name,
                        ["price"] = (double)product.Price,
                        ["category"] = product.Category,
                        ["description"] = product.Description
                    }
                };
                
                points.Add(point);
            }
            
            // Insert all points at once
            await _qdrantClient.UpsertAsync(ProductsCollection, points);
            
            _logger.LogInformation("Successfully added {ProductCount} mock products to {ProductsCollection}", 
                points.Count, ProductsCollection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add mock product data");
        }
    }
    
    private List<Product> GetMockProducts()
    {
        return new List<Product>
        {
            new Product 
            { 
                Id = 1, 
                Name = "iPhone 15 Pro", 
                Price = 999.99m, 
                Category = "Electronics",
                Description = "Latest iPhone with Pro features and advanced camera system"
            },
            new Product 
            { 
                Id = 2, 
                Name = "MacBook Air M2", 
                Price = 1199.00m, 
                Category = "Electronics",
                Description = "Lightweight laptop with M2 chip and all-day battery life"
            },
            new Product 
            { 
                Id = 3, 
                Name = "AirPods Pro", 
                Price = 249.00m, 
                Category = "Electronics",
                Description = "Wireless earbuds with active noise cancellation"
            },
            new Product 
            { 
                Id = 4, 
                Name = "Nike Air Jordan", 
                Price = 180.00m, 
                Category = "Footwear",
                Description = "Classic basketball sneakers with iconic design"
            },
            new Product 
            { 
                Id = 5, 
                Name = "Levi's 501 Jeans", 
                Price = 89.99m, 
                Category = "Clothing",
                Description = "Original straight fit denim jeans in classic blue"
            },
            new Product 
            { 
                Id = 6, 
                Name = "Samsung Galaxy S24", 
                Price = 899.99m, 
                Category = "Electronics",
                Description = "Android smartphone with AI-powered camera and display"
            },
            new Product 
            { 
                Id = 7, 
                Name = "Sony WH-1000XM4", 
                Price = 349.99m, 
                Category = "Electronics",
                Description = "Premium noise-canceling over-ear headphones"
            },
            new Product 
            { 
                Id = 8, 
                Name = "Adidas Ultraboost", 
                Price = 160.00m, 
                Category = "Footwear",
                Description = "High-performance running shoes with responsive cushioning"
            },
            new Product 
            { 
                Id = 9, 
                Name = "The North Face Jacket", 
                Price = 299.00m, 
                Category = "Clothing",
                Description = "Waterproof outdoor jacket for all weather conditions"
            },
            new Product 
            { 
                Id = 10, 
                Name = "iPad Pro 12.9", 
                Price = 1099.00m, 
                Category = "Electronics",
                Description = "Professional tablet with M2 chip and Liquid Retina display"
            }
        };
    }
    
    private float[] GenerateMockEmbedding(Product product)
    {
        // Generate simple mock embeddings based on product properties
        // In a real scenario, you'd use an actual embedding service
        var random = new Random(product.Id); // Use ID as seed for consistency
        var embedding = new float[384];
        
        for (int i = 0; i < 384; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0); // Range: -1 to 1
        }
        
        // Add some semantic meaning based on category
        switch (product.Category.ToLower())
        {
            case "electronics":
                for (int i = 0; i < 50; i++) embedding[i] += 0.3f;
                break;
            case "clothing":
                for (int i = 50; i < 100; i++) embedding[i] += 0.3f;
                break;
            case "footwear":
                for (int i = 100; i < 150; i++) embedding[i] += 0.3f;
                break;
        }
        
        return embedding;
    }

    private async Task<List<string>> GetCollectionsAsync()
    {
        try
        {
            var collections = await _qdrantClient.ListCollectionsAsync();
            return collections.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collections list");
            return new List<string>();
        }
    }
}
