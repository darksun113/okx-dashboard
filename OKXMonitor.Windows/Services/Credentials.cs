namespace OKXMonitor.Services;

/// The three OKX credential fields plus a demo-trading flag. Mirrors the Swift
/// `Credentials` struct. All four live together in a single Windows Credential
/// Manager entry (see CredentialStore) — never split across entries or files.
public sealed record Credentials(string ApiKey, string SecretKey, string Passphrase, bool Demo)
{
    public bool IsComplete =>
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(SecretKey) &&
        !string.IsNullOrEmpty(Passphrase);

    public static Credentials Empty => new("", "", "", false);
}
