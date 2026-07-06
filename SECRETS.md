# Secrets & Configuration

## Local Development

`appsettings.Development.json` is gitignored. Copy the template below and fill in your values:

```json
{
  "DatabaseSettings": {
    "Server": "<host>",
    "Port": 1433,
    "User": "<user>",
    "Password": "<password>",
    "Database": "<db>"
  },
  "JwtSettings": {
    "SecretKey": "<min-32-char-random-string>"
  },
  "Encryption": {
    "Key": "<encryption-key>",
    "IV": "<encryption-iv>"
  },
  "Email": {
    "FromEmail": "<from-email>",
    "GmailAppPassword": "<gmail-app-password>"
  },
  "PayFast": {
    "MerchantId": "<merchant-id>",
    "SecuredKey": "<secured-key>"
  }
}
```

Alternatively use [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):
```bash
dotnet user-secrets set "JwtSettings:SecretKey" "<value>" --project WorkNest.API
```

## Production

Use **Azure Key Vault** or environment variables. In `Program.cs`, add:

```csharp
// Azure Key Vault (recommended)
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://<vault-name>.vault.azure.net/"),
    new DefaultAzureCredential());

// Or environment variables (override appsettings.json)
// Set: DatabaseSettings__Password, JwtSettings__SecretKey, etc.
```

Never commit real credentials to source control.
