namespace Browser.Agent.Models;

public record BrowserResult(
    bool    Success,
    string? ErrorMessage,
    string? ScreenshotBase64,  // PNG after action, base64-encoded
    string? ExtractedText,
    string? PageTitle,
    string  PageUrl,
    int     DurationMs
);
