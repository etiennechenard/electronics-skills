using System.Text.Json;

namespace PartsSearch;

internal sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class DigiKeyAuth
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly KeyConfig _keyConfig;
    private readonly HttpSend _http;
    private readonly IClock _clock;
    private readonly RateLimiter _rateLimiter;
    private readonly string _tokenFilePath;

    public DigiKeyAuth(KeyConfig keyConfig, HttpSend http, IClock clock, RateLimiter rateLimiter, string? tokenFilePath = null)
    {
        _keyConfig = keyConfig;
        _http = http;
        _clock = clock;
        _rateLimiter = rateLimiter;
        _tokenFilePath = tokenFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "parts-search", "cache", "digikey-token.json");
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var cached = ReadCachedToken();
        if (cached is not null && cached.ExpiresAt - _clock.UtcNow > RefreshMargin)
        {
            TraceLog.Info($"digikey token reused (expires in {(int)(cached.ExpiresAt - _clock.UtcNow).TotalMinutes} min)");
            return cached.AccessToken;
        }

        await _rateLimiter.WaitAsync(VendorId.Digikey);
        var (accessToken, expiresIn) = await AcquireTokenAsync(cancellationToken);
        WriteCachedToken(accessToken, _clock.UtcNow + TimeSpan.FromSeconds(expiresIn));
        TraceLog.Info($"digikey token acquired (expires in {expiresIn / 60} min)");
        return accessToken;
    }

    private async Task<(string AccessToken, int ExpiresIn)> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = _keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientIdField)
            ?? throw new InvalidOperationException("digikey credentials are not configured");
        var clientSecret = _keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientSecretField)
            ?? throw new InvalidOperationException("digikey credentials are not configured");

        var form = await new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "OAuth2:basic",
        }).ReadAsStringAsync(cancellationToken);
        var request = new RawHttpRequest(
            HttpMethod.Post.Method,
            "https://api.digikey.com/v1/oauth2/token",
            new Dictionary<string, string>(),
            form,
            "application/x-www-form-urlencoded");

        var result = await _http.SendAsync(VendorId.Digikey, request, cancellationToken);
        if (!result.Succeeded || result.StatusCode != 200)
            throw new InvalidOperationException($"digikey token request failed: {result.Error ?? $"http {result.StatusCode}"}");

        using var document = JsonDocument.Parse(result.Body);
        return (
            document.RootElement.GetProperty("access_token").GetString()!,
            document.RootElement.GetProperty("expires_in").GetInt32());
    }

    private CachedToken? ReadCachedToken()
    {
        if (!File.Exists(_tokenFilePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedToken>(File.ReadAllText(_tokenFilePath), WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteCachedToken(string accessToken, DateTimeOffset expiresAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenFilePath)!);
        var json = JsonSerializer.Serialize(new CachedToken(accessToken, expiresAt), WireJson.Options);
        var tmp = $"{_tokenFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _tokenFilePath, overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
            throw;
        }
    }
}
