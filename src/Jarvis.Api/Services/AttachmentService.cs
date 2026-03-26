using Dapper;
using Jarvis.Api.Data;
using Jarvis.Api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Jarvis.Api.Services;

public class AttachmentService(
    DbConnectionFactory db,
    IConfiguration config,
    ILogger<AttachmentService> logger)
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif",
        "application/pdf", "text/plain", "text/csv"
    };

    private readonly long _maxBytes = config.GetValue<long>("Attachments:MaxFileSizeBytes", 20 * 1024 * 1024);
    private readonly string _uploadRoot = config.GetValue<string>("Attachments:UploadRoot", "./uploads")!;

    public bool IsAllowedType(string mimeType) => AllowedTypes.Contains(mimeType);

    public async Task<AttachmentRecord> SaveAttachmentAsync(
        Guid sessionId,
        string fileName,
        string mimeType,
        byte[] data,
        string? base64Preview = null)
    {
        if (!IsAllowedType(mimeType))
            throw new InvalidOperationException($"Unsupported media type: {mimeType}");

        if (data.Length > _maxBytes)
            throw new InvalidOperationException(
                $"File exceeds maximum size of {_maxBytes / (1024 * 1024)} MB.");

        var attachmentId = Guid.NewGuid();

        // Persist to disk
        var dir = Path.Combine(_uploadRoot, sessionId.ToString());
        Directory.CreateDirectory(dir);
        var safeFileName = Path.GetFileName(fileName);   // prevent path traversal
        var storagePath  = Path.Combine(dir, $"{attachmentId}_{safeFileName}");
        await File.WriteAllBytesAsync(storagePath, data);

        // Generate thumbnail for images (max 200px wide)
        if (base64Preview is null && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                base64Preview = await GenerateThumbnailAsync(data, mimeType);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not generate thumbnail for {FileName}", fileName);
            }
        }

        // Persist metadata to DB
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO jarvis_schema.attachments
                (id, file_name, mime_type, file_size_bytes, storage_path, base64_preview)
            VALUES
                (@attachmentId, @fileName, @mimeType, @fileSize, @storagePath, @base64Preview)
            RETURNING id
            """;
        await conn.ExecuteAsync(sql, new
        {
            attachmentId,
            fileName,
            mimeType,
            fileSize = (long)data.Length,
            storagePath,
            base64Preview
        });

        return new AttachmentRecord
        {
            Id            = attachmentId,
            FileName      = fileName,
            MimeType      = mimeType,
            FileSizeBytes = data.Length,
            StoragePath   = storagePath,
            Base64Preview = base64Preview,
            CreatedAt     = DateTimeOffset.UtcNow
        };
    }

    public async Task<AttachmentRecord?> GetAttachmentAsync(Guid attachmentId)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<AttachmentRecord>(
            """
            SELECT id, conversation_id AS ConversationId, file_name AS FileName,
                   mime_type AS MimeType, file_size_bytes AS FileSizeBytes,
                   storage_path AS StoragePath, base64_preview AS Base64Preview,
                   created_at AS CreatedAt
            FROM jarvis_schema.attachments
            WHERE id = @attachmentId
            """,
            new { attachmentId });
    }

    public async Task<byte[]?> ReadFileAsync(AttachmentRecord attachment)
    {
        if (attachment.StoragePath is null || !File.Exists(attachment.StoragePath))
            return null;
        return await File.ReadAllBytesAsync(attachment.StoragePath);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static async Task<string> GenerateThumbnailAsync(byte[] imageData, string mimeType)
    {
        using var image = Image.Load(imageData);

        // Scale to max 200px wide, preserving aspect ratio
        if (image.Width > 200)
            image.Mutate(x => x.Resize(200, 0));

        using var ms = new MemoryStream();
        await image.SaveAsWebpAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }
}
