using CalAssistant.Components;
using CalAssistant.Services;
using MaIN.Core;
using MaIN.Domain.Configuration;
using MaIN.Domain.Models.Abstract;
using Microsoft.AspNetCore.HttpOverrides;

// qwen3:4b is not in MaIN.NET's built-in ModelRegistry — register it at startup.
ModelRegistry.RegisterOrReplace(
    new GenericCloudModel(AssistantService.ModelName, BackendType.Ollama, "Qwen3 4B (Ollama)"));

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (interactive)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MaIN.NET → local Ollama (qwen3:4b)
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
