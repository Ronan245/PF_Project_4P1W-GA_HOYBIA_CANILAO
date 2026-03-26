using System.Security.Claims;

namespace ResourceApi.Security;

public static class HttpContextExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub");

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue("role"), "admin", StringComparison.OrdinalIgnoreCase);
}

