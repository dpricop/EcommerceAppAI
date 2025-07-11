using EcommerceAppAI.Models;
using EcommerceAppAI.Hubs;
using EcommerceAppAI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection("LlmSettings"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("QdrantSettings"));

// Add HttpClient for LLM API communication
builder.Services.AddHttpClient("LlmClient", (serviceProvider, client) =>
{
    var llmSettings = builder.Configuration.GetSection("LlmSettings").Get<LlmSettings>();
    client.BaseAddress = new Uri(llmSettings?.BaseUrl ?? "http://127.0.0.1:1234");
    client.Timeout = TimeSpan.FromSeconds(llmSettings?.Timeout ?? 30);
});

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

app.MapControllerRoute(
    name: "qdrant-test",
    pattern: "qdrant-test/{action=Index}",
    defaults: new { controller = "QdrantTest" });

app.Run();