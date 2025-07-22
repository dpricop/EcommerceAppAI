namespace EcommerceAppAI.Models;

public class LlmSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "llama3.2:latest";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public int Timeout { get; set; } = 30;
}
