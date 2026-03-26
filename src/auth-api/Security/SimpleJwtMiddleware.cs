namespace AuthApi.Security;

public sealed class SimpleJwtMiddleware
{
    private readonly RequestDelegate _next;

    public SimpleJwtMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, JwtTokenService jwtTokenService)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            var principal = jwtTokenService.ValidateToken(token);
            if (principal is not null)
            {
                context.User = principal;
            }
        }

        await _next(context);
    }
}

