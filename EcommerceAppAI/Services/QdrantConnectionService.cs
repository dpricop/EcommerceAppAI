using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using EcommerceAppAI.Models;

namespace EcommerceAppAI.Services;

public class QdrantConnectionService : IDisposable
{
    private readonly QdrantClient _qdrantClient;
    private readonly QdrantSettings _settings;
    private readonly ILogger<QdrantConnectionService> _logger;

    public QdrantConnectionService(IOptions<QdrantSettings> settings, ILogger<QdrantConnectionService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        // Debug: Log the actual connection string being used
        _logger.LogInformation("QdrantConnectionService initializing...");
        _logger.LogInformation("Connection string from settings: '{ConnectionString}'", _settings.ConnectionString);
        _logger.LogInformation("Collection name: '{CollectionName}'", _settings.CollectionName);
        
        try
        {
            // Parse the connection string to extract host and port
            var uri = new Uri(_settings.ConnectionString);
            _logger.LogInformation("URI parsed successfully: Scheme={Scheme}, Host={Host}, Port={Port}", 
                uri.Scheme, uri.Host, uri.Port);
            
            // QdrantClient constructor expects host and port separately, not a full URL
            var useHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            
            // Create QdrantClient with proper gRPC configuration
            _qdrantClient = new QdrantClient(
                host: uri.Host, 
                port: uri.Port, 
                https: useHttps,
                grpcTimeout: TimeSpan.FromSeconds(30)
            );
            
            _logger.LogInformation("QdrantClient created successfully with host={Host}, port={Port}, https={UseHttps}", 
                uri.Host, uri.Port, useHttps);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create QdrantClient with connection string: '{ConnectionString}'", _settings.ConnectionString);
            throw;
        }
        
        _logger.LogInformation("QdrantConnectionService initialized with connection: {ConnectionString}", 
            _settings.ConnectionString);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing gRPC connection to Qdrant at: {ConnectionString}", _settings.ConnectionString);
            
            // Test Qdrant gRPC client directly
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            _logger.LogInformation("gRPC connection successful! Found {CollectionCount} collections", 
                collections.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC connection failed to Qdrant at {ConnectionString}. Exception type: {ExceptionType}, Message: {Message}", 
                _settings.ConnectionString, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public async Task<string> GetQdrantInfoAsync()
    {
        try
        {
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            return $"Connected to Qdrant at {_settings.ConnectionString}. " +
                   $"Found {collections.Count} collections: {string.Join(", ", collections)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Qdrant info");
            return $"Failed to connect to Qdrant: {ex.Message}";
        }
    }

    public async Task<bool> CreateTestCollectionAsync()
    {
        try
        {
            _logger.LogInformation("Creating test collection: {CollectionName}", _settings.CollectionName);
            
            // Check if collection already exists
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            if (collections.Contains(_settings.CollectionName))
            {
                _logger.LogInformation("Collection {CollectionName} already exists", _settings.CollectionName);
                return true;
            }
            
            // Create collection with simple vector configuration
            await _qdrantClient.CreateCollectionAsync(_settings.CollectionName, new VectorParams
            {
                Size = 384, // Standard embedding dimension for sentence transformers
                Distance = Distance.Cosine
            });
            
            _logger.LogInformation("Successfully created collection: {CollectionName}", _settings.CollectionName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection {CollectionName}", _settings.CollectionName);
            return false;
        }
    }

    public async Task<List<string>> GetCollectionsAsync()
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

    public void Dispose()
    {
        _qdrantClient?.Dispose();
    }
}