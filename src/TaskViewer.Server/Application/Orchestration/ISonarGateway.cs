using System.Text.Json.Nodes;

namespace TaskViewer.Server.Application.Orchestration;

public interface ISonarGateway
{
    Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query);
}
