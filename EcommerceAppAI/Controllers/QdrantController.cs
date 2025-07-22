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
    private readonly OllamaEmbeddingService _embeddingService;
    private readonly ILogger<QdrantController> _logger;
    
    // Collection names
    private const string ProductsCollection = "products";
    private const string DocumentsCollection = "documents";

    public QdrantController(
        QdrantConnectionService qdrantService, 
        QdrantClient qdrantClient,
        OllamaEmbeddingService embeddingService,
        ILogger<QdrantController> logger)
    {
        _qdrantService = qdrantService;
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
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
            _logger.LogInformation("Starting collection initialization with real embeddings...");
            
            // Create both collections with correct vector size for nomic-embed-text (768 dimensions)
            var productsCreated = await CreateCollectionIfNotExistsAsync(ProductsCollection, 768);
            var documentsCreated = await CreateCollectionIfNotExistsAsync(DocumentsCollection, 768);
            
            if (productsCreated && documentsCreated)
            {
                // Add mock product data with real embeddings
                await AddMockProductDataWithRealEmbeddingsAsync();
                
                TempData["Success"] = "Collections initialized successfully with real Ollama embeddings! Vector database is ready for RAG functionality.";
                _logger.LogInformation("Successfully initialized both collections with real embeddings");
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
    private async Task<bool> CreateCollectionIfNotExistsAsync(string collectionName, uint vectorSize = 768)
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
            
            // Create collection with vector configuration for nomic-embed-text
            await _qdrantClient.CreateCollectionAsync(collectionName, new VectorParams
            {
                Size = vectorSize, // nomic-embed-text produces 768-dimensional embeddings
                Distance = Distance.Cosine
            });
            
            _logger.LogInformation("Successfully created collection: {CollectionName} with {VectorSize}D vectors", 
                collectionName, vectorSize);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection {CollectionName}", collectionName);
            return false;
        }
    }
    
    private async Task AddMockProductDataWithRealEmbeddingsAsync()
    {
        try
        {
            _logger.LogInformation("Adding mock product data with real embeddings to {ProductsCollection}", ProductsCollection);
            
            // Check if products already exist
            var existingPoints = await _qdrantClient.ScrollAsync(ProductsCollection, limit: 1);
            if (existingPoints.Result.Any())
            {
                _logger.LogInformation("Product data already exists, skipping mock data insertion");
                return;
            }
            
            var mockProducts = MockData.GetMockProducts();
            var points = new List<PointStruct>();
            
            // Process products in batches to avoid overwhelming the embedding service
            var batchSize = 3;
            var batches = mockProducts.Chunk(batchSize);
            
            foreach (var batch in batches)
            {
                var batchPoints = new List<PointStruct>();
                
                foreach (var product in batch)
                {
                    try
                    {
                        // Create searchable text combining all product information
                        var searchableText = $"{product.Name} {product.Category} {product.Description}";
                        
                        // Generate real embedding using Ollama
                        _logger.LogDebug("Generating embedding for product: {ProductName}", product.Name);
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(searchableText);
                        
                        var point = new PointStruct
                        {
                            Id = (ulong)product.Id,
                            Vectors = embedding,
                            Payload = 
                            {
                                ["name"] = product.Name,
                                ["price"] = (double)product.Price,
                                ["category"] = product.Category,
                                ["description"] = product.Description
                            }
                        };
                        
                        batchPoints.Add(point);
                        _logger.LogDebug("Generated {EmbeddingDims}D embedding for {ProductName}", 
                            embedding.Length, product.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to generate embedding for product {ProductName}", product.Name);
                    }
                }
                
                // Insert batch
                if (batchPoints.Any())
                {
                    await _qdrantClient.UpsertAsync(ProductsCollection, batchPoints);
                    points.AddRange(batchPoints);
                    _logger.LogInformation("Inserted batch of {BatchSize} products", batchPoints.Count);
                    
                    // Small delay between batches to be respectful to Ollama
                    await Task.Delay(100);
                }
            }
            
            _logger.LogInformation("Successfully added {ProductCount} products with real embeddings to {ProductsCollection}", 
                points.Count, ProductsCollection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add mock product data with real embeddings");
            throw;
        }
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