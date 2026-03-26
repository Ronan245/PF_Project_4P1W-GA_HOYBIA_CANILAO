using AuthApi.Services;

namespace AuthApi;

public sealed record RegisterRequest(string Email, string Password, string DisplayName)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            return "A valid email is required.";
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            return "Display name is required.";
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 8)
        {
            return "Password must be at least 8 characters.";
        }

        return null;
    }
}

public sealed record LoginRequest(string Email, string Password)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            return "Email and password are required.";
        }

        return null;
    }
}

public static class AuthResponses
{
    public static object FromUser(AppUser user, string token) => new
    {
        token,
        user = Me(user)
    };

    public static object Me(AppUser user) => new
    {
        id = user.Id,
        email = user.Email,
        displayName = user.DisplayName,
        role = user.Role,
        createdAtUtc = user.CreatedAtUtc
    };
}
