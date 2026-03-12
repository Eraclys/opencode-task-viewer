using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Serialization;

sealed class QueueStateJsonConverter : JsonConverter<QueueState>
{
    public override QueueState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for QueueState.");

        return QueueState.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, QueueState value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}