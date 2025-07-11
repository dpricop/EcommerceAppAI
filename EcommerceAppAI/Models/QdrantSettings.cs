namespace EcommerceAppAI.Models;

public class QdrantSettings
{
    public string ConnectionString { get; set; } = "http://localhost:6333";
    public string CollectionName { get; set; } = "test_collection";
}