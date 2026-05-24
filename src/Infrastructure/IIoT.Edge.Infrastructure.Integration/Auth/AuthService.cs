using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Auth.LocalAccounts;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;

namespace IIoT.Edge.Infrastructure.Integration.Auth;

public class AuthService : IAuthService
{
    public const string HttpClientName = "CloudAuth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly LocalAdminConfig _localAdminConfig;
    private readonly ILocalAccountAuthService _localAccountAuthService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private UserSession? _currentUser;
    private int _backgroundRefreshStarted;

    public UserSession? CurrentUser => GetCachedActiveSession();
    public bool IsAuthenticated => GetCachedActiveSession() is not null;
    public event Action<UserSession?>? AuthStateChanged;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider,
        LocalAdminConfig localAdminConfig,
        ILocalAccountAuthService localAccountAuthService)
    {
        _httpClientFactory = httpClientFactory;
        _endpointProvider = endpointProvider;
        _localAdminConfig = localAdminConfig;
        _localAccountAuthService = localAccountAuthService;
    }

    public bool HasPermission(string permission)
    {
        var session = GetCachedActiveSession();
        if (session is null)
        {
            return false;
        }

        if (session.IsLocalAdmin)
        {
            return true;
        }

        if (session.Permissions.Contains("Admin"))
        {
            return true;
        }

        return session.Permissions.Contains(permission);
    }

    public Task<AuthResult> LoginLocalAsync(string password)
    {
        var configuredHash = _localAdminConfig.PasswordHash?.Trim();
        if (string.IsNullOrWhiteSpace(configuredHash))
        {
            return Task.FromResult(AuthResult.Fail("本地管理员未配置。"));
        }

        var inputHash = ComputeSha256(password);
        if (!string.Equals(inputHash, configuredHash, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthResult.Fail("密码错误。"));
        }

        var session = new UserSession
        {
            DisplayName = "本地管理员",
            EmployeeNo = "LOCAL_ADMIN",
            IsLocalAdmin = true,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ExpiresAtUtc = null,
            AccessToken = null,
            RefreshToken = null,
            RefreshTokenExpiresAtUtc = null
        };

        SetSession(session);
        return Task.FromResult(AuthResult.Ok("本地管理员登录成功。"));
    }

    public Task<AuthResult> LoginLocalAccountAsync(string userName, string password)
    {
        var result = _localAccountAuthService.Authenticate(userName, password);
        if (!result.Success)
        {
            return Task.FromResult(AuthResult.Fail(result.ErrorMessage ?? "本地账号登录失败。"));
        }

        var session = new UserSession
        {
            DisplayName = result.DisplayName ?? result.UserName ?? "本地账号",
            EmployeeNo = result.UserName,
            IsLocalAdmin = true,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ExpiresAtUtc = null,
            AccessToken = null,
            RefreshToken = null,
            RefreshTokenExpiresAtUtc = null
        };

        SetSession(session);
        return Task.FromResult(AuthResult.Ok($"欢迎：{session.DisplayName}"));
    }

    public async Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
    {
        try
        {
            var httpClient = CreateHttpClient();
            var loginUrl = _endpointProvider.BuildUrl(_endpointProvider.GetIdentityDeviceLoginPath());
            using var response = await httpClient.PostAsJsonAsync(loginUrl, new
            {
                employeeNo,
                password,
                deviceId
            }).ConfigureAwait(false);

            var session = await TryReadSessionAsync(response).ConfigureAwait(false);
            if (session is null)
            {
                return AuthResult.Fail(await BuildAuthFailureMessageAsync(response).ConfigureAwait(false));
            }

            SetSession(session);
            return AuthResult.Ok($"欢迎，{session.DisplayName}");
        }
        catch (TaskCanceledException)
        {
            return AuthResult.Fail("连接超时。");
        }
        catch (HttpRequestException)
        {
            return AuthResult.Fail("无法连接到服务器。");
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"登录异常：{ex.Message}");
        }
    }

    public void Logout() => SetSession(null);

    public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
        => RefreshCloudSessionAsync(cancellationToken);

    private void SetSession(UserSession? session)
    {
        _currentUser = session;
        AuthStateChanged?.Invoke(_currentUser);
    }

    private UserSession? GetCachedActiveSession()
    {
        if (_currentUser is null)
        {
            return null;
        }

        if (_currentUser.IsLocalAdmin)
        {
            return _currentUser;
        }

        if (_currentUser.ExpiresAtUtc.HasValue
            && _currentUser.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            if (!CanRefreshCloudSession(_currentUser))
            {
                SetSession(null);
                return null;
            }

            TriggerBackgroundRefresh();
        }

        return _currentUser;
    }

    private void TriggerBackgroundRefresh()
    {
        if (Interlocked.Exchange(ref _backgroundRefreshStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshCloudSessionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundRefreshStarted, 0);
            }
        });
    }

    private static bool CanRefreshCloudSession(UserSession session)
        => !string.IsNullOrWhiteSpace(session.RefreshToken)
            && (!session.RefreshTokenExpiresAtUtc.HasValue
                || session.RefreshTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow);

    private async Task<bool> RefreshCloudSessionAsync(CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentUser is null)
            {
                return false;
            }

            if (_currentUser.IsLocalAdmin)
            {
                return true;
            }

            if (_currentUser.ExpiresAtUtc.HasValue
                && _currentUser.ExpiresAtUtc.Value > DateTimeOffset.UtcNow)
            {
                return true;
            }

            if (!CanRefreshCloudSession(_currentUser))
            {
                SetSession(null);
                return false;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _endpointProvider.BuildUrl(_endpointProvider.GetHumanIdentityRefreshPath()));
            request.Headers.TryAddWithoutValidation(CloudAuthHeaders.RefreshToken, _currentUser.RefreshToken);

            using var response = await CreateHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            var refreshedSession = await TryReadSessionAsync(response).ConfigureAwait(false);
            if (refreshedSession is null)
            {
                SetSession(null);
                return false;
            }

            SetSession(refreshedSession);
            return true;
        }
        catch
        {
            SetSession(null);
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static async Task<UserSession?> TryReadSessionAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var token = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim('"', ' ', '\r', '\n', '\t');
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return ParseJwtToken(
            token,
            CloudAuthHeaders.ReadRefreshToken(response),
            CloudAuthHeaders.ReadRefreshTokenExpiresAtUtc(response),
            CloudAuthHeaders.ReadAccessTokenExpiresAtUtc(response));
    }

    private static async Task<string> BuildAuthFailureMessageAsync(HttpResponseMessage response)
    {
        var errorEnvelope = await TryReadErrorEnvelopeAsync(response).ConfigureAwait(false);
        var firstError = errorEnvelope?.Errors?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstError))
        {
            return firstError;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "工号或密码错误。",
            HttpStatusCode.Forbidden => "当前账号无权操作这台设备。",
            HttpStatusCode.BadRequest => "登录请求被拒绝。",
            >= HttpStatusCode.InternalServerError => "服务器暂时不可用。",
            _ => $"登录失败：{response.StatusCode}"
        };
    }

    private static async Task<ApiErrorEnvelope?> TryReadErrorEnvelopeAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private HttpClient CreateHttpClient()
        => _httpClientFactory.CreateClient(HttpClientName);

    private static UserSession? ParseJwtToken(
        string token,
        string? refreshToken,
        DateTimeOffset? refreshTokenExpiresAtUtc,
        DateTimeOffset? accessTokenExpiresAtUtc)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var displayName = jwtToken.Claims
                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)
                ?.Value
                ?? jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                ?? "未知用户";

            var employeeNo = jwtToken.Claims
                .FirstOrDefault(c => string.Equals(c.Type, "employeeNo", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?? jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value
                ?? jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            var permissions = jwtToken.Claims
                .Where(c =>
                    string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new UserSession
            {
                DisplayName = displayName,
                EmployeeNo = employeeNo,
                IsLocalAdmin = false,
                Permissions = permissions,
                ExpiresAtUtc = accessTokenExpiresAtUtc ?? TryGetExpiresAtUtc(jwtToken),
                AccessToken = token,
                RefreshToken = refreshToken,
                RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc
            };
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetExpiresAtUtc(JwtSecurityToken jwtToken)
    {
        var expClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
        if (!long.TryParse(expClaim, out var exp))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(exp);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
