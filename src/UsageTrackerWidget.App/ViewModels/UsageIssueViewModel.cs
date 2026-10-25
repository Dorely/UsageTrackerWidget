namespace UsageTrackerWidget.App.ViewModels;

internal sealed record UsageIssueViewModel(
    string Title,
    string Message,
    string Detail,
    bool IsWarning);
