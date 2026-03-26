namespace ResourceApi.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "PF_Project.AuthApi";

    public string Audience { get; set; } = "PF_Project.Clients";

    public string Key { get; set; } = "PF_Project_4P1W_dev_signing_key_please_change";
}

