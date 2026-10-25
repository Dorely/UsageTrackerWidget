using System.Security.Cryptography;
using System.Text;

namespace UsageTrackerWidget.Providers.Codex;

internal sealed record CodexPkcePair(string Verifier, string Challenge, string State);

internal static class CodexPkce
{
    public static CodexPkcePair Create()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new CodexPkcePair(verifier, challenge, state);
    }

    public static string BuildAuthorizeUrl(CodexPkcePair pair, Uri redirectUri)
    {
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = CodexConstants.ClientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["scope"] = CodexConstants.Scope,
            ["state"] = pair.State,
            ["code_challenge"] = pair.Challenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["originator"] = CodexConstants.Originator,
        };

        var query = string.Join(
            "&",
            parameters.Select(pairValue =>
                $"{Uri.EscapeDataString(pairValue.Key)}={Uri.EscapeDataString(pairValue.Value)}"));

        return $"{CodexConstants.AuthorizationEndpoint}?{query}";
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
