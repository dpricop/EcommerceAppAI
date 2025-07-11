using EcommerceAppAI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EcommerceAppAI.Controllers;

public class DebugController : Controller
{
    private readonly QdrantSettings _qdrantSettings;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<DebugController> _logger;

    public DebugController(IOptions<QdrantSettings> qdrantSettings, IOptions<LlmSettings> llmSettings, ILogger<DebugController> logger)
    {
        _qdrantSettings = qdrantSettings.Value;
        _llmSettings = llmSettings.Value;
        _logger = logger;
    }

    public IActionResult Config()
    {
        var model = new
        {
            QdrantConnectionString = _qdrantSettings.ConnectionString ?? "NULL",
            QdrantCollectionName = _qdrantSettings.CollectionName ?? "NULL",
            LlmBaseUrl = _llmSettings.BaseUrl ?? "NULL",
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "NULL",
            AllEnvironmentVariables = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(x => x.Key.ToString()!.Contains("Qdrant") || x.Key.ToString()!.Contains("Llm"))
                .ToDictionary(x => x.Key.ToString()!, x => x.Value?.ToString() ?? "NULL")
        };

        return Json(model);
    }
}