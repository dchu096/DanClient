using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class MicrosoftDeviceCodeAuthService : IAuthenticationService
{
    private const string MinecraftNintendoSwitchClientId = "00000000441cc96b";
    private const string LiveScope = "service::user.auth.xboxlive.com::MBI_SSL";
    private const string LiveDeviceCodeEndpoint = "https://login.live.com/oauth20_connect.srf";
    private const string LiveTokenEndpoint = "https://login.live.com/oauth20_token.srf";
    private const string XboxAuthRelyingParty = "http://auth.xboxlive.com";
    private const string MinecraftJavaRelyingParty = "rp://api.minecraftservices.com/";
    private const string XboxUserAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XboxDeviceAuthEndpoint = "https://device.auth.xboxlive.com/device/authenticate";
    private const string XboxTitleAuthEndpoint = "https://title.auth.xboxlive.com/title/authenticate";
    private const string XboxXstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions XboxJsonOptions = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly ECDsa _proofKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public MicrosoftDeviceCodeAuthService(HttpClient httpClient)
        : this(httpClient, MinecraftNintendoSwitchClientId)
    {
    }

    public MicrosoftDeviceCodeAuthService(HttpClient httpClient, string clientId)
    {
        _httpClient = httpClient;
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? MinecraftNintendoSwitchClientId
            : clientId.Trim();
    }

    public async Task<MinecraftAccount> SignInWithDeviceCodeAsync(
        Func<DeviceCodeInfo, Task> showDeviceCode,
        IProgress<AuthProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new AuthProgress("microsoft", "Requesting Microsoft device code."));
        var device = await RequestLiveDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        await showDeviceCode(new DeviceCodeInfo(
            device.UserCode,
            device.VerificationUri,
            device.Message,
            device.ExpiresIn,
            device.Interval)).ConfigureAwait(false);

        progress?.Report(new AuthProgress("microsoft", "Waiting for Microsoft authorization."));
        var liveToken = await PollLiveTokenAsync(device, cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("xbox", "Authenticating Xbox user token."));
        var userToken = await AuthenticateXboxUserAsync(liveToken.AccessToken, cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("xbox", "Authenticating Xbox device token."));
        var deviceToken = await AuthenticateXboxDeviceAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("xbox", "Authenticating Minecraft title token."));
        var titleToken = await AuthenticateXboxTitleAsync(
            liveToken.AccessToken,
            deviceToken.Token,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("xsts", "Requesting Minecraft XSTS token."));
        var xsts = await AuthorizeXstsAsync(
            userToken.Token,
            deviceToken.Token,
            titleToken.Token,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("minecraft", "Exchanging Xbox token for Minecraft token."));
        var minecraft = await LoginMinecraftAsync(xsts.UserHash, xsts.Token, cancellationToken).ConfigureAwait(false);

        progress?.Report(new AuthProgress("minecraft", "Reading Minecraft profile."));
        var profile = await GetMinecraftProfileAsync(minecraft.AccessToken, cancellationToken).ConfigureAwait(false);

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, minecraft.ExpiresIn - 60));
        return new MinecraftAccount(
            minecraft.AccessToken,
            profile.Name,
            profile.Id,
            expiresAt);
    }

    private async Task<LiveDeviceCodeResponse> RequestLiveDeviceCodeAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["scope"] = LiveScope,
            ["client_id"] = _clientId,
            ["response_type"] = "device_code"
        });

        using var response = await _httpClient.PostAsync(
            LiveDeviceCodeEndpoint,
            content,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Microsoft Live device code request", cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var device = await JsonSerializer.DeserializeAsync<LiveDeviceCodeResponse>(
            stream,
            ResponseJsonOptions,
            cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Microsoft device code response was empty.");

        if (string.IsNullOrWhiteSpace(device.Message))
        {
            device.Message = $"To sign in, open {device.VerificationUri} and enter code {device.UserCode}.";
        }

        return device;
    }

    private async Task<LiveTokenResponse> PollLiveTokenAsync(
        LiveDeviceCodeResponse device,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn);
        var delay = TimeSpan.FromSeconds(Math.Max(1, device.Interval));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = device.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            using var response = await _httpClient.PostAsync(
                $"{LiveTokenEndpoint}?client_id={Uri.EscapeDataString(_clientId)}",
                content,
                cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var token = DeserializeLiveToken(body);
            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return token;
            }

            if (token?.Error == "authorization_pending")
            {
                continue;
            }

            if (token?.Error == "slow_down")
            {
                delay += TimeSpan.FromSeconds(5);
                continue;
            }

            throw new InvalidOperationException(
                $"Microsoft Live device flow failed: {token?.ErrorDescription ?? token?.Error ?? body}");
        }

        throw new TimeoutException("Microsoft device code expired before authorization completed.");
    }

    private async Task<XboxTokenResponse> AuthenticateXboxUserAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            RelyingParty = XboxAuthRelyingParty,
            TokenType = "JWT",
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"t={microsoftAccessToken}"
            }
        };

        return await PostSignedXboxJsonAsync<XboxTokenResponse>(
            XboxUserAuthEndpoint,
            payload,
            "Xbox user authentication",
            cancellationToken,
            xboxContractVersion: "2").ConfigureAwait(false);
    }

    private async Task<XboxTokenResponse> AuthenticateXboxDeviceAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "ProofOfPossession",
                Id = FormatXboxGuid(),
                DeviceType = "Nintendo",
                SerialNumber = FormatXboxGuid(),
                Version = "0.0.0",
                ProofKey = CreateProofKeyJwk()
            },
            RelyingParty = XboxAuthRelyingParty,
            TokenType = "JWT"
        };

        return await PostSignedXboxJsonAsync<XboxTokenResponse>(
            XboxDeviceAuthEndpoint,
            payload,
            "Xbox device authentication",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<XboxTokenResponse> AuthenticateXboxTitleAsync(
        string microsoftAccessToken,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                DeviceToken = deviceToken,
                RpsTicket = $"t={microsoftAccessToken}",
                SiteName = "user.auth.xboxlive.com",
                ProofKey = CreateProofKeyJwk()
            },
            RelyingParty = XboxAuthRelyingParty,
            TokenType = "JWT"
        };

        return await PostSignedXboxJsonAsync<XboxTokenResponse>(
            XboxTitleAuthEndpoint,
            payload,
            "Xbox title authentication",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<XstsResponse> AuthorizeXstsAsync(
        string userToken,
        string deviceToken,
        string titleToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            RelyingParty = MinecraftJavaRelyingParty,
            TokenType = "JWT",
            Properties = new
            {
                UserTokens = new[] { userToken },
                DeviceToken = deviceToken,
                TitleToken = titleToken,
                ProofKey = CreateProofKeyJwk(),
                SandboxId = "RETAIL"
            }
        };

        var response = await PostSignedXboxJsonAsync<XstsResponse>(
            XboxXstsEndpoint,
            payload,
            "Minecraft XSTS authorization",
            cancellationToken).ConfigureAwait(false);

        var userHash = response.DisplayClaims.Xui.FirstOrDefault()?.Uhs
                       ?? throw new InvalidDataException("XSTS response did not include a user hash.");
        return response with { UserHash = userHash };
    }

    private async Task<MinecraftLoginResponse> LoginMinecraftAsync(
        string userHash,
        string xstsToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        return await PostJsonAsync<MinecraftLoginResponse>(
            MinecraftLoginEndpoint,
            payload,
            "Minecraft login",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MinecraftProfileResponse> GetMinecraftProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Minecraft profile request", cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<MinecraftProfileResponse>(
            stream,
            ResponseJsonOptions,
            cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Minecraft profile response was empty.");
    }

    private async Task<T> PostJsonAsync<T>(
        string uri,
        object payload,
        string operation,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, ResponseJsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = content
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(
            stream,
            ResponseJsonOptions,
            cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException($"{typeof(T).Name} response was empty.");
    }

    private async Task<T> PostSignedXboxJsonAsync<T>(
        string uri,
        object payload,
        string operation,
        CancellationToken cancellationToken,
        string xboxContractVersion = "1")
    {
        var json = JsonSerializer.Serialize(payload, XboxJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MustRevalidate = true
        };
        request.Headers.TryAddWithoutValidation("Signature", CreateXboxSignature(uri, string.Empty, json));
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", xboxContractVersion);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(
            stream,
            XboxJsonOptions,
            cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException($"{typeof(T).Name} response was empty.");
    }

    private string CreateXboxSignature(string uri, string authorizationToken, string payload)
    {
        var timestamp = checked((ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11_644_473_600L) * 10_000_000UL);
        var path = new Uri(uri).AbsolutePath;
        using var signedData = new MemoryStream();
        WriteInt32BigEndian(signedData, 1);
        signedData.WriteByte(0);
        WriteUInt64BigEndian(signedData, timestamp);
        signedData.WriteByte(0);
        WriteNullTerminated(signedData, "POST");
        WriteNullTerminated(signedData, path);
        WriteNullTerminated(signedData, authorizationToken);
        WriteNullTerminated(signedData, payload);

        var signature = _proofKey.SignData(
            signedData.ToArray(),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        using var header = new MemoryStream();
        WriteInt32BigEndian(header, 1);
        WriteUInt64BigEndian(header, timestamp);
        header.Write(signature);
        return Convert.ToBase64String(header.ToArray());
    }

    private Dictionary<string, string> CreateProofKeyJwk()
    {
        var parameters = _proofKey.ExportParameters(false);
        return new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncode(parameters.Q.X ?? []),
            ["y"] = Base64UrlEncode(parameters.Q.Y ?? []),
            ["alg"] = "ES256",
            ["use"] = "sig"
        };
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = TryExtractErrorDetail(body) ?? body;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = response.ReasonPhrase ?? "Unknown error";
        }

        if (TryExtractXboxError(body, response, out var xboxMessage))
        {
            detail = xboxMessage;
        }

        var responseIds = GetResponseIds(response);
        if (!string.IsNullOrWhiteSpace(responseIds))
        {
            detail = $"{detail} ({responseIds})";
        }

        throw new HttpRequestException(
            $"{operation} failed with {(int)response.StatusCode} {response.StatusCode}: {detail}",
            inner: null,
            response.StatusCode);
    }

    private static bool TryExtractXboxError(
        string body,
        HttpResponseMessage response,
        out string message)
    {
        message = string.Empty;
        var errorCode = TryReadLongProperty(body, "XErr");
        if (errorCode is null &&
            response.Headers.TryGetValues("x-err", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var headerErrorCode))
        {
            errorCode = headerErrorCode;
        }

        if (errorCode is null)
        {
            return false;
        }

        message = errorCode switch
        {
            2148916227 => "Your account was banned by Xbox and cannot be used.",
            2148916229 => "Your Xbox account is restricted. A guardian must allow online play at https://account.microsoft.com/family/.",
            2148916233 => "Your Microsoft account does not have an Xbox profile. Create one at https://signup.live.com/signup.",
            2148916234 => "Your Xbox account has not accepted the Xbox Terms of Service.",
            2148916235 => "Xbox has blocked sign-in from this account region.",
            2148916236 => "Your Xbox account requires proof of age.",
            2148916237 => "Your Xbox account has reached its playtime limit.",
            2148916238 => "This Xbox account is under 18 and must be added to a family by an adult.",
            _ => $"Xbox Live authentication failed with XErr {errorCode}: {body}"
        };
        return true;
    }

    private static long? TryReadLongProperty(string body, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt64(out var value) => value,
                JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetResponseIds(HttpResponseMessage response)
    {
        var parts = new List<string>();
        AddHeader("MS-CV");
        AddHeader("X-XblCorrelationId");
        AddHeader("x-ms-request-id");
        AddHeader("x-ms-correlation-id");
        return parts.Count == 0 ? null : string.Join(", ", parts);

        void AddHeader(string name)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                parts.Add($"{name}: {string.Join("/", values)}");
            }
        }
    }

    private static string? TryExtractErrorDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();
            AddString("error_description");
            AddString("error");
            AddString("message");
            AddString("Message");
            AddString("XErr");
            AddString("Identity");
            return parts.Count == 0 ? null : string.Join(" ", parts);

            void AddString(string propertyName)
            {
                if (!root.TryGetProperty(propertyName, out var property))
                {
                    return;
                }

                var value = property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : property.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add($"{propertyName}: {value}");
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LiveTokenResponse? DeserializeLiveToken(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<LiveTokenResponse>(body, ResponseJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt64BigEndian(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteNullTerminated(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
        stream.WriteByte(0);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string FormatXboxGuid() => $"{{{Guid.NewGuid()}}}";

    private sealed class LiveDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; init; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; init; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("interval")]
        public int Interval { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class LiveTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string ErrorDescription { get; init; } = string.Empty;
    }

    private sealed record XboxTokenResponse(
        [property: JsonPropertyName("Token")] string Token,
        [property: JsonPropertyName("NotAfter")] string? NotAfter);

    private sealed record XstsResponse(
        [property: JsonPropertyName("Token")] string Token,
        [property: JsonPropertyName("DisplayClaims")] XstsDisplayClaims DisplayClaims)
    {
        public string UserHash { get; init; } = string.Empty;
    }

    private sealed record XstsDisplayClaims(
        [property: JsonPropertyName("xui")] XstsUser[] Xui);

    private sealed record XstsUser(
        [property: JsonPropertyName("uhs")] string Uhs,
        [property: JsonPropertyName("xid")] string? Xid);

    private sealed record MinecraftLoginResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record MinecraftProfileResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
}
