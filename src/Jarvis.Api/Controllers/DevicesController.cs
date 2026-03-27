using System.Text.Json;
using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController(
    DeviceRepository repo,
    DeviceConnectionTracker tracker,
    IDeviceToolForwarder forwarder,
    ILogger<DevicesController> logger) : ControllerBase
{
    // GET /api/devices
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var devices = await repo.GetAllAsync();
        // Overlay real-time online status from connection tracker
        return Ok(devices.Select(d => new
        {
            d.Id,
            d.Name,
            d.DeviceType,
            d.OsPlatform,
            d.LahVersion,
            d.Hostname,
            d.IpAddress,
            IsOnline = tracker.IsOnline(d.Id.ToString()),
            Status   = tracker.IsOnline(d.Id.ToString()) ? "online" : d.Status,
            d.LastSeenAt,
            d.AdvertisedModulesJson,
            d.CreatedAt
        }));
    }

    // GET /api/devices/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> GetById(Guid id)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null) return NotFound();

        return Ok(new
        {
            device.Id,
            device.Name,
            device.DeviceType,
            device.OsPlatform,
            device.LahVersion,
            device.Hostname,
            device.IpAddress,
            IsOnline = tracker.IsOnline(id.ToString()),
            Status   = tracker.IsOnline(id.ToString()) ? "online" : device.Status,
            device.LastSeenAt,
            device.AdvertisedModulesJson,
            device.CreatedAt
        });
    }

    // POST /api/devices/register
    // Creates a device record and issues a one-time registration token.
    // The LAH uses this token on first connection to authenticate via DeviceHub.
    [HttpPost("register")]
    public async Task<ActionResult<RegisterDeviceResponse>> Register(
        [FromBody] RegisterDeviceRequest req)
    {
        var deviceId  = await repo.CreateAsync(req.Name, req.OsPlatform);
        var token     = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                              .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var expiresAt = DateTimeOffset.UtcNow.AddYears(10);

        await repo.SetRegistrationTokenAsync(deviceId, token, expiresAt);

        logger.LogInformation("Device registration token issued for '{Name}' ({Id})", req.Name, deviceId);

        return Ok(new RegisterDeviceResponse(deviceId, token, expiresAt));
    }

    // GET /api/devices/{id}/permissions
    [HttpGet("{id:guid}/permissions")]
    public async Task<ActionResult<IEnumerable<DevicePermissionRecord>>> GetPermissions(Guid id)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null) return NotFound();

        var permissions = await repo.GetPermissionsAsync(id);
        return Ok(permissions);
    }

    // PUT /api/devices/{id}/permissions
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> UpdatePermissions(Guid id, [FromBody] UpdatePermissionsRequest req)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null) return NotFound();

        foreach (var p in req.Permissions)
        {
            await repo.UpsertPermissionAsync(
                id, p.AgentName, p.Capability,
                p.IsGranted, p.RequireConfirm, p.PathScope);
        }

        logger.LogInformation("Updated {Count} permissions for device {Id}", req.Permissions.Count, id);
        return NoContent();
    }

    // GET /api/devices/{id}/log
    [HttpGet("{id:guid}/log")]
    public async Task<ActionResult<IEnumerable<DeviceToolLogEntry>>> GetLog(
        Guid id, [FromQuery] int limit = 50)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null) return NotFound();

        var entries = await repo.GetToolLogAsync(id, Math.Min(limit, 200));
        return Ok(entries);
    }

    // POST /api/devices/tools/execute
    // Finds an online device that advertises the requested tool and forwards the call.
    [HttpPost("tools/execute")]
    public async Task<ActionResult> ExecuteTool(
        [FromBody] DeviceToolExecuteRequest req, CancellationToken ct)
    {
        var devices = await repo.GetAllAsync();
        DeviceRecord? target = null;

        foreach (var device in devices)
        {
            if (!tracker.IsOnline(device.Id.ToString())) continue;
            if (device.AdvertisedModulesJson is null) continue;

            var modules = JsonSerializer.Deserialize<List<AdvertisedModule>>(
                device.AdvertisedModulesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (modules?.Any(m => m.Tools.Contains(req.ToolName)) == true)
            {
                target = device;
                break;
            }
        }

        if (target is null)
            return NotFound(new { error = $"No online device has tool '{req.ToolName}'" });

        var parameters = JsonDocument.Parse(req.Parameters.GetRawText());
        var result = await forwarder.ForwardToolAsync(
            target.Id, req.ToolName, parameters,
            req.RequireConfirm, TimeSpan.FromSeconds(30), ct);

        return Ok(new { result });
    }

    // DELETE /api/devices/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null) return NotFound();

        await repo.DeleteAsync(id);
        logger.LogInformation("Device {Id} ({Name}) unregistered", id, device.Name);
        return NoContent();
    }
}
