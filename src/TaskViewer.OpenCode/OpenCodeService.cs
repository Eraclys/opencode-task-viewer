using System.Text;

namespace TaskViewer.OpenCode;

public sealed class OpenCodeService : IOpenCodeService
{
    readonly Func<OpenCodeHttpClient> _createClient;
    readonly OpenCodeClientOptions _options;

    public OpenCodeService(Func<OpenCodeHttpClient> createClient, OpenCodeClientOptions options)
    {
        _createClient = createClient;
        _options = options;
    }

    public string? BuildSessionUrl(string sessionId, string? directory)
    {
        var sid = (sessionId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(sid))
            return null;

        var baseUrl = (_options.Url ?? string.Empty).TrimEnd('/');

        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var normalizedDirectory = NormalizeDirectory(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return $"{baseUrl}/session/{Uri.EscapeDataString(sid)}";

        var bytes = Encoding.UTF8.GetBytes(normalizedDirectory);
        var slug = Convert
            .ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"{baseUrl}/{slug}/session/{Uri.EscapeDataString(sid)}";
    }

    public Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
        => _createClient().ReadWorkingStatusMapAsync(directory, cancellationToken);

    Task<Dictionary<string, string>> IOpenCodeStatusReader.ReadWorkingStatusMapAsync(string directory)
        => ReadWorkingStatusMapAsync(directory);

    public Task<List<OpenCodeTodoTransport>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => _createClient().ReadTodosAsync(sessionId, directory, cancellationToken);

    public Task<List<OpenCodeMessageTransport>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
        => _createClient().ReadMessagesAsync(sessionId, limit, cancellationToken);

    public Task<List<OpenCodeProjectTransport>> ReadProjectsAsync(CancellationToken cancellationToken = default)
        => _createClient().ReadProjectsAsync(cancellationToken);

    public Task<List<OpenCodeSessionTransport>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
        => _createClient().ReadSessionsAsync(directory, limit, cancellationToken);

    public Task<OpenCodeSessionTransport?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => _createClient().ReadSessionAsync(sessionId, directory, cancellationToken);

    public Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => _createClient().ArchiveSessionAsync(sessionId, directory, cancellationToken);

    public Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
        => _createClient().CreateSessionAsync(directory, title, cancellationToken);

    Task<string> IOpenCodeDispatchClient.CreateSessionAsync(string directory, string title)
        => CreateSessionAsync(directory, title);

    public Task SendPromptAsync(string directory, string sessionId, string prompt, CancellationToken cancellationToken = default)
        => _createClient().SendPromptAsync(directory, sessionId, prompt, cancellationToken);

    Task IOpenCodeDispatchClient.SendPromptAsync(string directory, string sessionId, string prompt)
        => SendPromptAsync(directory, sessionId, prompt);

    static string? NormalizeDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (trimmed is "/" or "\\")
            return trimmed;

        if (trimmed.Length == 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
            return trimmed;

        return trimmed.TrimEnd('/', '\\');
    }
}
