namespace UsageTrackerWidget.Providers.Codex;

internal static class CodexConstants
{
    public const string ProviderKey = "codex";
    public const string ProviderDisplayName = "Codex";
    public const string AccountLabel = "Default";

    public const string AuthorizationEndpoint = "https://auth.openai.com/oauth/authorize";
    public const string TokenEndpoint = "https://auth.openai.com/oauth/token";
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string Scope = "openid profile email offline_access";
    public const string CallbackHost = "127.0.0.1";
    public const string CallbackPath = "/auth/callback";

    public const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    public const string ImagesEndpoint = "https://chatgpt.com/backend-api/codex/images/generations";
    public const string ImageMeterModel = "gpt-image-2.5-flare";
    public const string Originator = "pi";
    public const string UserAgent = "UsageTrackerWidget/0.1";

    public const string AccountIdClaim = "https://api.openai.com/auth";
    public const string AccountIdProperty = "chatgpt_account_id";

    public static readonly TimeSpan TokenRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan UsageRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);
}
