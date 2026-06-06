using System;
using System.Text.Json;
using Meziantou.Framework.Win32;

namespace OKXMonitor.Services;

/// Stores ALL credentials as a single JSON blob in Windows Credential Manager
/// (DPAPI-backed). Mirrors Keychain.swift's single-item design. Never writes to
/// files/registry/argv. Reads are defensive — a missing entry yields Empty.
public static class CredentialStore
{
    const string AppName = "OKXMonitor";

    sealed record Blob(string ApiKey, string SecretKey, string Passphrase, bool Demo);

    public static void Save(Credentials c)
    {
        var json = JsonSerializer.Serialize(new Blob(c.ApiKey, c.SecretKey, c.Passphrase, c.Demo));
        CredentialManager.WriteCredential(
            applicationName: AppName,
            userName: "default",
            secret: json,
            persistence: CredentialPersistence.LocalMachine);
    }

    public static Credentials Load()
    {
        try
        {
            var cred = CredentialManager.ReadCredential(AppName);
            if (cred?.Password is { Length: > 0 } json)
            {
                var b = JsonSerializer.Deserialize<Blob>(json);
                if (b is not null)
                    return new Credentials(b.ApiKey, b.SecretKey, b.Passphrase, b.Demo);
            }
        }
        catch { /* fall through to Empty — UI shows "open Settings" */ }
        return Credentials.Empty;
    }

    public static void Delete()
    {
        try { CredentialManager.DeleteCredential(AppName); } catch { }
    }
}
