using CalAssistant.Components;
using CalAssistant.Services;
using MaIN.Core;
using MaIN.Domain.Configuration;
using MaIN.Domain.Models.Abstract;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (interactive)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Local model (configurable). Default: qwen3:1.7b — fits fully in 4 GB VRAM (fast, GPU-only).
// Override with Assistant:Model (appsettings) or Assistant__Model (env var).
var modelName = builder.Configuration["Assistant:Model"] ?? AssistantService.DefaultModel;

// The model is not in MaIN.NET's built-in ModelRegistry — register it at startup.
ModelRegistry.RegisterOrReplace(
    new GenericCloudModel(modelName, BackendType.Ollama, $"{modelName} (Ollama)"));

// MaIN.NET → local Ollama
builder.Services.AddMaIN(builder.Configuration, options =>
{
    options.BackendType = BackendType.Ollama;
});

// Our services
builder.Services.AddSingleton<CalendarService>();      // single user → singleton
builder.Services.AddScoped<AssistantService>();        // conversation state per Blazor circuit

var app = builder.Build();

// Trust reverse proxy headers (nginx gateway in Docker)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// Initialize MaIN (warms up hub / AIHub)
app.Services.UseMaIN();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS redirect is off by default in containers (no TLS certs inside).
// Enable via config/env: EnableHttpsRedirection=true
var enableHttpsRedirection = app.Configuration.GetValue<bool>("EnableHttpsRedirection", false);
if (enableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
