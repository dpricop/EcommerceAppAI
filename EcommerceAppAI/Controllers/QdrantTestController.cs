using EcommerceAppAI.Models;
using EcommerceAppAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceAppAI.Controllers;

public class QdrantTestController : Controller
{
    private readonly QdrantConnectionService _qdrantService;
    private readonly ILogger<QdrantTestController> _logger;

    public QdrantTestController(QdrantConnectionService qdrantService, ILogger<QdrantTestController> logger)
    {
        _qdrantService = qdrantService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var model = new QdrantTestViewModel();
        
        try
        {
            _logger.LogInformation("QdrantTest Index called");
            
            // Test connection
            model.IsConnected = await _qdrantService.TestConnectionAsync();
            model.ConnectionInfo = await _qdrantService.GetQdrantInfoAsync();
            model.Collections = await _qdrantService.GetCollectionsAsync();
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
    public async Task<IActionResult> CreateTestCollection()
    {
        try
        {
            var success = await _qdrantService.CreateTestCollectionAsync();
            
            if (success)
            {
                TempData["Success"] = "Test collection created successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to create test collection.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating test collection");
            TempData["Error"] = $"Error creating collection: {ex.Message}";
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
}