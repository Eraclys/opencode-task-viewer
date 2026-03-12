using System.Text.Json;
using System.Text.Json.Serialization;

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
