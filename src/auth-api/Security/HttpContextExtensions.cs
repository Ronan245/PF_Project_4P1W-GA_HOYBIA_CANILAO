using System.Security.Claims;

namespace AuthApi.Security;

public static class HttpContextExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub");
}

