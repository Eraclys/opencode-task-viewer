using System.Text.Json.Nodes;

sealed class SessionCache
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
    public List<JsonObject> Data { get; set; } = [];
    public Dictionary<string, JsonObject> ById { get; set; } = new(StringComparer.Ordinal);
}