namespace EcommerceAppAI.Models;

public class QdrantViewModel
{
    public bool IsConnected { get; set; }
    public string ConnectionInfo { get; set; } = string.Empty;
    public List<string> Collections { get; set; } = new();
}