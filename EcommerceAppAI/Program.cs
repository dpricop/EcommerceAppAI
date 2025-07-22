using EcommerceAppAI.Models;
using EcommerceAppAI.Hubs;
using EcommerceAppAI.Services;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection("LlmSettings"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("QdrantSettings"));

// Add HttpClient for Ollama API communication
builder.Services.AddHttpClient("LlmClient", (serviceProvider, client) =>
{
    var llmSettings = builder.Configuration.GetSection("LlmSettings").Get<LlmSettings>();
    client.BaseAddress = new Uri(llmSettings?.BaseUrl ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromSeconds(llmSettings?.Timeout ?? 45);
    
    // Add headers for Ollama
    client.DefaultRequestHeaders.Add("User-Agent", "EcommerceAppAI/1.0");
});

// Add Qdrant Client
builder.Services.AddSingleton<QdrantClient>(serviceProvider =>
{
    var qdrantSettings = builder.Configuration.GetSection("QdrantSettings").Get<QdrantSettings>();
    var uri = new Uri(qdrantSettings?.ConnectionString ?? "http://localhost:6333");
    var useHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    
    return new QdrantClient(
        host: uri.Host, 
        port: uri.Port, 
        https: useHttps,
        grpcTimeout: TimeSpan.FromSeconds(30)
    );
});

// Add RAG Services
builder.Services.AddSingleton<OllamaEmbeddingService>();
builder.Services.AddSingleton<RagService>();

// Add Qdrant Connection Service
builder.Services.AddSingleton<QdrantConnectionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();
app.MapHub<ChatHub>("/chathub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();