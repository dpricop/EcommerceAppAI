using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using EcommerceAppAI.Models;
using System.Text;
using System.Text.Json;

namespace EcommerceAppAI.Controllers;

public class OllamaTestController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<OllamaTestController> _logger;

    public OllamaTestController(
        IHttpClientFactory httpClientFactory, 
        IOptions<LlmSettings> llmSettings, 
        ILogger<OllamaTestController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _llmSettings = llmSettings.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("LlmClient");
            
            // Test Ollama version endpoint
            var versionResponse = await httpClient.GetAsync("/api/version");
            var versionContent = await versionResponse.Content.ReadAsStringAsync();
            
            if (!versionResponse.IsSuccessStatusCode)
            {
                return Json(new { 
                    success = false, 
                    error = $"Failed to connect to Ollama: {versionResponse.StatusCode}",
                    details = versionContent
                });
            }

            // Test model availability
            var modelRequest = new { name = _llmSettings.ModelName };
            var modelJson = JsonSerializer.Serialize(modelRequest);
            var modelContent = new StringContent(modelJson, Encoding.UTF8, "application/json");
            
            var modelResponse = await httpClient.PostAsync("/api/show", modelContent);
            var modelResponseContent = await modelResponse.Content.ReadAsStringAsync();

            return Json(new { 
                success = true, 
                version = versionContent,
                model = _llmSettings.ModelName,
                modelStatus = modelResponse.IsSuccessStatusCode ? "Available" : "Not Found",
                modelDetails = modelResponseContent,
                baseUrl = _llmSettings.BaseUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Ollama connection");
            return Json(new { 
                success = false, 
                error = ex.Message,
                baseUrl = _llmSettings.BaseUrl
            });
        }
    }
}