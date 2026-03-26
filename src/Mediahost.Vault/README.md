# Mediahost.Vault

A reusable .NET library that gives any Mediahost application access to secrets stored in [Infisical](https://infisical.com/).

The library is framework-agnostic — it depends only on `Microsoft.Extensions.*` abstractions, so it works in any ASP.NET Core app, worker service, or console app.

---

## Adding to your project

Add a project reference (or, once published, a NuGet package reference):

```xml
<!-- in your .csproj -->
<ProjectReference Include="..\Mediahost.Vault\Mediahost.Vault.csproj" />
```

---

## Two lines in Program.cs

```csharp
using Mediahost.Vault.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register the vault — reads all Infisical config from IConfiguration automatically.
builder.Services.AddInfisicalVault(builder.Configuration);
```

Then inject `IVaultService` wherever you need secrets.

---

## Required configuration

Set the following values (environment variables, `appsettings.json`, or a secrets manager):

| Key | Example | Notes |
|-----|---------|-------|
| `Infisical:BaseUrl` | `http://infisical:8080` | URL of your Infisical instance |
| `Infisical:ClientId` | `abc123...` | Universal Auth machine identity client ID |
| `Infisical:ClientSecret` | `secret...` | Universal Auth machine identity client secret |
| `Infisical:ProjectId` | `proj_xyz...` | Infisical project (workspace) ID |
| `Infisical:Environment` | `prod` | Target environment (default: `prod`) |

---

## Fetching a MySQL connection string at startup

```csharp
var app = builder.Build();

// Fetch the connection string from the vault before the app starts accepting traffic.
var vault = app.Services.GetRequiredService<IVaultService>();
var connString = await vault.GetSecretAsync("/databases/mysql", "CONNECTION_STRING");

// Use it, e.g. reconfigure EF Core or store in a singleton config object.
```

Or fetch a batch of secrets for a whole path in one call:

```csharp
var dbSecrets = await vault.GetSecretsBulkAsync("/databases/mysql");
// dbSecrets["HOST"], dbSecrets["PORT"], dbSecrets["PASSWORD"], ...
```

---

## Available methods

```csharp
// Fetch a single secret value (null if not found).
Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default);

// Fetch all secrets under a path as a dictionary.
Task<Dictionary<string, string>> GetSecretsBulkAsync(string path, CancellationToken ct = default);

// Create or update a secret.
Task SetSecretAsync(string path, string key, string value, CancellationToken ct = default);

// Check whether a secret exists without fetching its value.
Task<bool> SecretExistsAsync(string path, string key, CancellationToken ct = default);
```

---

## Notes

- **Authentication is transparent.** The service uses Universal Auth and handles token acquisition and renewal automatically. Callers never deal with tokens.
- **Secret values are never logged.** Only paths and key names appear in log output.
- **Resilience.** The underlying `HttpClient` retries transient errors up to 3 times with exponential backoff (1 s, 2 s, 4 s).
