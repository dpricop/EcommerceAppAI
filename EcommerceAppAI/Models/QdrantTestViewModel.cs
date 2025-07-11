namespace EcommerceAppAI.Models;

public class QdrantTestViewModel
{
    public bool IsConnected { get; set; }
    public string ConnectionInfo { get; set; } = string.Empty;
    public List<string> Collections { get; set; } = new();
}