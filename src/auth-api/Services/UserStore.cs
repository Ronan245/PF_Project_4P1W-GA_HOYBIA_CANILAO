using System.Text.Json;

namespace AuthApi.Services;

public sealed class UserStore
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private List<AppUser> _users;

    public UserStore(IHostEnvironment environment, PasswordService passwordService)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "users.json");
        _users = LoadUsers();

        if (_users.Count == 0)
        {
            Seed(passwordService);
        }

        UpgradeSeedUsers(passwordService);
    }

    public bool EmailExists(string email)
    {
        lock (_sync)
        {
            return _users.Any(user => string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public AppUser CreatePlayer(string email, string password, string displayName, PasswordService passwordService)
    {
        var (hash, salt) = passwordService.HashPassword(password);
        var user = new AppUser
        {
            Email = email.Trim(),
            DisplayName = displayName.Trim(),
            Role = "player",
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAtUtc = DateTime.UtcNow
        };

        lock (_sync)
        {
            _users.Add(user);
            SaveUsers();
        }

        return user;
    }

    public AppUser? Authenticate(string email, string password, PasswordService passwordService)
    {
        lock (_sync)
        {
            var user = _users.FirstOrDefault(candidate =>
                string.Equals(candidate.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return null;
            }

            return passwordService.VerifyPassword(password, user.PasswordHash, user.PasswordSalt)
                ? user
                : null;
        }
    }

    public AppUser? GetById(string id)
    {
        lock (_sync)
        {
            return _users.FirstOrDefault(user => user.Id == id);
        }
    }

    private List<AppUser> LoadUsers()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<AppUser>>(json, _jsonOptions) ?? [];
    }

    private void SaveUsers()
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_users, _jsonOptions));
    }

    private void Seed(PasswordService passwordService)
    {
        _users =
        [
            CreateSeedUser("admin01@gmail.com", "Admin User", "admin", "admin123", passwordService),
            CreateSeedUser("player01@gmail.com", "Player One", "player", "player456", passwordService)
        ];

        SaveUsers();
    }

    private void UpgradeSeedUsers(PasswordService passwordService)
    {
        var changed = false;

        changed |= EnsureDefaultUser(
            currentEmails: ["admin01@gmail.com", "admin@4pics.local"],
            desiredEmail: "admin01@gmail.com",
            displayName: "Admin User",
            role: "admin",
            password: "admin123",
            passwordService: passwordService);

        changed |= EnsureDefaultUser(
            currentEmails: ["player01@gmail.com", "player@4pics.local"],
            desiredEmail: "player01@gmail.com",
            displayName: "Player One",
            role: "player",
            password: "player456",
            passwordService: passwordService);

        if (changed)
        {
            SaveUsers();
        }
    }

    private bool EnsureDefaultUser(
        IReadOnlyCollection<string> currentEmails,
        string desiredEmail,
        string displayName,
        string role,
        string password,
        PasswordService passwordService)
    {
        var existing = _users.FirstOrDefault(user =>
            currentEmails.Any(email => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(user.Role, role, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (_users.Any(user => string.Equals(user.Email, desiredEmail, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _users.Add(CreateSeedUser(desiredEmail, displayName, role, password, passwordService));
            return true;
        }

        var (hash, salt) = passwordService.HashPassword(password);
        existing.Email = desiredEmail;
        existing.DisplayName = displayName;
        existing.Role = role;
        existing.PasswordHash = hash;
        existing.PasswordSalt = salt;
        return true;
    }

    private static AppUser CreateSeedUser(string email, string displayName, string role, string password, PasswordService passwordService)
    {
        var (hash, salt) = passwordService.HashPassword(password);

        return new AppUser
        {
            Email = email,
            DisplayName = displayName,
            Role = role,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
