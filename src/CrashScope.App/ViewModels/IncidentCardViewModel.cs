namespace CrashScope.App.ViewModels;

public sealed class IncidentCardViewModel
{
    public string Id { get; init; } = string.Empty;
    public string TimeText { get; init; } = "Unknown time";
    public string SessionText { get; init; } = "Unknown session";
    public string EvidenceText { get; init; } = "No classified evidence";
    public string DirectoryPath { get; init; } = string.Empty;
    public int EvidenceCount { get; init; }
}
