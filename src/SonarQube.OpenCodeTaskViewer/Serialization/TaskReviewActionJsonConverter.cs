using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Serialization;

sealed class TaskReviewActionJsonConverter : JsonConverter<TaskReviewAction>
{
    public override TaskReviewAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for TaskReviewAction.");

        return TaskReviewAction.FromRaw(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, TaskReviewAction value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}