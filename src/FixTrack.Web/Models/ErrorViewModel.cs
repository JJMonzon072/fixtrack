namespace FixTrack.Web.Models;

public sealed record ErrorViewModel(
    int StatusCode,
    string Title,
    string Message,
    string Icon,
    string? RequestId);
