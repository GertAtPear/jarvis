namespace Jarvis.Api.Models;

/// <summary>
/// Carries attachment data through the orchestration pipeline
/// (from controller → orchestrator → LLM content blocks).
/// </summary>
public record AttachmentDto(
    Guid   AttachmentId,
    string FileName,
    string MimeType,
    string Base64Data   // full file content for LLM submission
);
