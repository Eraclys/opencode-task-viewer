using System.Text.Json.Nodes;

sealed class ProjectsCache
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
    public List<JsonObject> Data { get; set; } = [];
}