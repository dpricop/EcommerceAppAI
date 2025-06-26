namespace EcommerceAppAI.Models;

public class LlmSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:1234";
    public string ModelName { get; set; } = "llama-3.2-3b-instruct";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public int Timeout { get; set; } = 30;
}
