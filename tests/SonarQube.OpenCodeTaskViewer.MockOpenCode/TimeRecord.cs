namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class TimeRecord
{
    public string Created { get; set; } = string.Empty;
    public string Updated { get; set; } = string.Empty;
    public long? Archived { get; set; }
}
