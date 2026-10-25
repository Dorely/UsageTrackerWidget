using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.Providers.Codex;

public sealed class CodexAuthException : Exception
{
    public CodexAuthException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class CodexReauthRequiredException : Exception
{
    public CodexReauthRequiredException(string message)
        : base(message)
    {
    }
}

internal sealed class CodexUsageSourceException : Exception
{
    public CodexUsageSourceException(string message, UsageErrorKind kind, int? statusCode = null)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public UsageErrorKind Kind { get; }

    public int? StatusCode { get; }
}
