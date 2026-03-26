using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthApi.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Security;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateToken(AppUser user)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpirationMinutes);
        var payload = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["email"] = user.Email,
            ["role"] = user.Role,
            ["name"] = user.DisplayName,
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["exp"] = expiresAt.ToUnixTimeSeconds()
        };

        var headerSegment = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signatureSegment = Base64Url(Sign($"{headerSegment}.{payloadSegment}"));

        return $"{headerSegment}.{payloadSegment}.{signatureSegment}";
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var expectedSignature = Base64Url(Sign($"{parts[0]}.{parts[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(parts[2])))
        {
            return null;
        }

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("iss", out var issuer) || issuer.GetString() != _options.Issuer)
        {
            return null;
        }

        if (!root.TryGetProperty("aud", out var audience) || audience.GetString() != _options.Audience)
        {
            return null;
        }

        if (!root.TryGetProperty("exp", out var exp) || exp.GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new("sub", root.GetProperty("sub").GetString() ?? string.Empty),
            new("email", root.GetProperty("email").GetString() ?? string.Empty),
            new("role", root.GetProperty("role").GetString() ?? string.Empty),
            new("name", root.GetProperty("name").GetString() ?? string.Empty)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "SimpleJwt"));
    }

    private byte[] Sign(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

