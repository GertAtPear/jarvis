using Jarvis.Ui.Auth;
using Jarvis.Ui.Components;
using Jarvis.Ui.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<JarvisAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JarvisAuthStateProvider>());

// Named HTTP client for auth calls (no token needed)
builder.Services.AddHttpClient("jarvis-unauth", client =>
    client.BaseAddress = new Uri(builder.Configuration["JarvisApi:BaseUrl"] ?? "http://localhost:5000/"));

var jarvisBaseUrl = new Uri(builder.Configuration["JarvisApi:BaseUrl"] ?? "http://localhost:5000/");

builder.Services.AddHttpClient<ChatApiService>(client =>
{
    client.BaseAddress = jarvisBaseUrl;
});

builder.Services.AddHttpClient<UsageApiService>(client =>
{
    client.BaseAddress = jarvisBaseUrl;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
