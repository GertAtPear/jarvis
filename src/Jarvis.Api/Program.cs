using Jarvis.Api.Data;
using Jarvis.Api.Extensions;
using Jarvis.Api.Hubs;
using Mediahost.Auth.Extensions;
using Serilog;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File("logs/jarvis-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddJarvisServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseJarvisAuth();
app.MapControllers();
app.MapHub<DeviceHub>("/hubs/device");

app.Run();
