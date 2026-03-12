using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.OpenCodeTaskViewer.Domain.Sessions;

namespace SonarQube.OpenCodeTaskViewer.Serialization;

sealed class ViewerTaskStatusJsonConverter : JsonConverter<ViewerTaskStatus>
{
    public override ViewerTaskStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return ViewerTaskStatus.Pending;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for ViewerTaskStatus.");

        return ViewerTaskStatus.FromRaw(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, ViewerTaskStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}