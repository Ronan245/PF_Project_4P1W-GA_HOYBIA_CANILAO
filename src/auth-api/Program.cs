using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using AuthApi;
using AuthApi.Security;
using AuthApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("default", config =>
    {
        config.PermitLimit = 60;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueLimit = 0;
    });
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<UserStore>();

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<SimpleJwtMiddleware>();

app.MapGet("/", () => Results.Ok(new
{
    service = "auth-api",
    status = "ok"
})).RequireRateLimiting("default");

app.MapGet("/health", () => Results.Ok(new
{
    service = "auth-api",
    status = "healthy",
    timestampUtc = DateTime.UtcNow
})).RequireRateLimiting("default");

app.MapPost("/auth/register", (RegisterRequest request, UserStore userStore, PasswordService passwordService, JwtTokenService jwtTokenService) =>
{
    var validationError = request.Validate();
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    if (userStore.EmailExists(request.Email))
    {
        return Results.Conflict(new { error = "An account with this email already exists." });
    }

    var user = userStore.CreatePlayer(request.Email, request.Password, request.DisplayName, passwordService);
    var token = jwtTokenService.CreateToken(user);

    return Results.Ok(AuthResponses.FromUser(user, token));
}).RequireRateLimiting("default");

app.MapPost("/auth/login", (LoginRequest request, UserStore userStore, PasswordService passwordService, JwtTokenService jwtTokenService) =>
{
    var validationError = request.Validate();
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var user = userStore.Authenticate(request.Email, request.Password, passwordService);
    if (user is null)
    {
        return Results.BadRequest(new { error = "Invalid email or password." });
    }

    var token = jwtTokenService.CreateToken(user);
    return Results.Ok(AuthResponses.FromUser(user, token));
}).RequireRateLimiting("default");

app.MapGet("/auth/me", (HttpContext httpContext, UserStore userStore) =>
{
    var userId = httpContext.User.GetUserId();
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var user = userStore.GetById(userId);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(AuthResponses.Me(user));
}).RequireRateLimiting("default");

app.Run();
