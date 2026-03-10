using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Domain.Sessions;

namespace SonarQube.OpenCodeTaskViewer.Serialization;

public static class TaskViewerJsonSerializerOptionsExtensions
{
    public static JsonSerializerOptions AddTaskViewerJsonConverters(this JsonSerializerOptions options)
    {
        AddConverter<SonarIssueTypeJsonConverter>(options);
        AddConverter<SonarIssueSeverityJsonConverter>(options);
        AddConverter<SonarIssueStatusJsonConverter>(options);
        AddConverter<QueueStateJsonConverter>(options);
        AddConverter<ViewerTaskStatusJsonConverter>(options);
        AddConverter<TaskReviewActionJsonConverter>(options);

        return options;
    }

    static void AddConverter<TConverter>(JsonSerializerOptions options)
        where TConverter : JsonConverter, new()
    {
        if (options.Converters.OfType<TConverter>().Any())
            return;

        options.Converters.Add(new TConverter());
    }
}

sealed class SonarIssueTypeJsonConverter : JsonConverter<SonarIssueType>
{
    public override SonarIssueType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for SonarIssueType.");

        return SonarIssueType.FromRaw(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, SonarIssueType value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}

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

sealed class SonarIssueStatusJsonConverter : JsonConverter<SonarIssueStatus>
{
    public override SonarIssueStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for SonarIssueStatus.");

        return SonarIssueStatus.FromRaw(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, SonarIssueStatus value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}

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
