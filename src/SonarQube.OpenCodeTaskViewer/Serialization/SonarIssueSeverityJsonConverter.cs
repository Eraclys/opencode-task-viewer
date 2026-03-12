using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Serialization;

sealed class SonarIssueSeverityJsonConverter : JsonConverter<SonarIssueSeverity>
{
    public override SonarIssueSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for SonarIssueSeverity.");

        return SonarIssueSeverity.FromRaw(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, SonarIssueSeverity value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}