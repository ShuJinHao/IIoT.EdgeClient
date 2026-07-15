using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.SharedKernel.Security;
using Microsoft.IdentityModel.Tokens;

namespace IIoT.Edge.Infrastructure.Integration.Auth;

public class AuthService : IAuthService
{
    public const string HttpClientName = "CloudAuth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly LocalAdminConfig _localAdminConfig;
    private readonly ILocalAdminCredentialStore _localAdminCredentialStore;
    private readonly CloudJwtValidationConfig _jwtValidationConfig;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private UserSession? _currentUser;
    private int _backgroundRefreshStarted;

    public UserSession? CurrentUser => GetCachedActiveSession();
    public bool IsAuthenticated => GetCachedActiveSession() is not null;
    public LocalAdminCredentialStatus LocalAdminCredentialStatus => GetLocalAdminCredentialStatus();
    public event Action<UserSession?>? AuthStateChanged;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider,
        LocalAdminConfig localAdminConfig,
        ILocalAdminCredentialStore localAdminCredentialStore,
        CloudJwtValidationConfig jwtValidationConfig,
        TimeProvider? timeProvider = null)
    {
        _httpClientFactory = httpClientFactory;
        _endpointProvider = endpointProvider;
        _localAdminConfig = localAdminConfig;
        _localAdminCredentialStore = localAdminCredentialStore;
        _jwtValidationConfig = jwtValidationConfig;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var configuredHash = ResolveLocalAdminPasswordHash();
        if (string.IsNullOrWhiteSpace(configuredHash))
        {
            return Task.FromResult(AuthResult.Fail("本地管理员未配置，请先初始化。"));
        }

        var verification = EdgePasswordHasher.Verify(password, configuredHash);
        if (verification == EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return Task.FromResult(AuthResult.Fail("本地管理员密码使用旧哈希格式，请先重置。"));
        }

        if (verification != EdgePasswordVerificationResult.Verified)
        {
            return Task.FromResult(AuthResult.Fail("密码错误。"));
        }

        SetSession(CreateLocalAdminSession());
        return Task.FromResult(AuthResult.Ok("本地管理员登录成功。"));
    }

    public Task<AuthResult> InitializeLocalAdminAsync(string newPassword)
    {
        var status = GetLocalAdminCredentialStatus();
        if (status is LocalAdminCredentialStatus.Ready or LocalAdminCredentialStatus.RequiresPasswordReset)
        {
            return Task.FromResult(AuthResult.Fail("本地管理员已配置，请使用登录或重置流程。"));
        }

        var passwordPolicyError = EdgePasswordPolicy.ValidateNewPassword(newPassword);
        if (passwordPolicyError is not null)
        {
            return Task.FromResult(AuthResult.Fail(passwordPolicyError));
        }

        return Task.FromResult(SaveLocalAdminPasswordAndLogin(newPassword, "本地紧急管理员初始化成功。"));
    }

    public Task<AuthResult> ResetLocalAdminPasswordAsync(string currentPassword, string newPassword)
    {
        var configuredHash = ResolveLocalAdminPasswordHash();
        if (string.IsNullOrWhiteSpace(configuredHash))
        {
            return Task.FromResult(AuthResult.Fail("本地管理员未配置，请先初始化。"));
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return Task.FromResult(AuthResult.Fail("旧密码不能为空。"));
        }

        var passwordPolicyError = EdgePasswordPolicy.ValidateNewPassword(newPassword);
        if (passwordPolicyError is not null)
        {
            return Task.FromResult(AuthResult.Fail(passwordPolicyError));
        }

        var verification = EdgePasswordHasher.Verify(currentPassword, configuredHash);
        if (verification is not EdgePasswordVerificationResult.Verified
            and not EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return Task.FromResult(AuthResult.Fail("旧密码校验失败。"));
        }

        return Task.FromResult(SaveLocalAdminPasswordAndLogin(newPassword, "本地紧急管理员密码已重置。"));
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
                return AuthResult.Fail(response.IsSuccessStatusCode
                    ? "云端登录令牌无效。"
                    : await BuildAuthFailureMessageAsync(response).ConfigureAwait(false));
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

    private LocalAdminCredentialStatus GetLocalAdminCredentialStatus()
    {
        var configuredHash = ResolveLocalAdminPasswordHash();
        if (string.IsNullOrWhiteSpace(configuredHash))
        {
            return LocalAdminCredentialStatus.NotConfigured;
        }

        if (EdgePasswordHasher.IsLegacySha256Hash(configuredHash))
        {
            return LocalAdminCredentialStatus.RequiresPasswordReset;
        }

        return EdgePasswordHasher.Verify(string.Empty, configuredHash) == EdgePasswordVerificationResult.InvalidHash
            ? LocalAdminCredentialStatus.Invalid
            : LocalAdminCredentialStatus.Ready;
    }

    private string? ResolveLocalAdminPasswordHash()
    {
        var storedHash = _localAdminCredentialStore.ReadPasswordHash();
        return string.IsNullOrWhiteSpace(storedHash)
            ? _localAdminConfig.PasswordHash?.Trim()
            : storedHash.Trim();
    }

    private AuthResult SaveLocalAdminPasswordAndLogin(string newPassword, string successMessage)
    {
        var passwordHash = EdgePasswordHasher.HashPassword(newPassword);
        _localAdminCredentialStore.WritePasswordHash(passwordHash);
        SetSession(CreateLocalAdminSession());
        return AuthResult.Ok(successMessage);
    }

    private static UserSession CreateLocalAdminSession()
        => new()
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
            && _currentUser.ExpiresAtUtc.Value <= _timeProvider.GetUtcNow())
        {
            if (!CanRefreshCloudSession(_currentUser))
            {
                SetSession(null);
                return null;
            }

            TriggerBackgroundRefresh();
            return null;
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

    private bool CanRefreshCloudSession(UserSession session)
        => !string.IsNullOrWhiteSpace(session.RefreshToken)
            && (!session.RefreshTokenExpiresAtUtc.HasValue
                || session.RefreshTokenExpiresAtUtc.Value > _timeProvider.GetUtcNow());

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
                && _currentUser.ExpiresAtUtc.Value > _timeProvider.GetUtcNow())
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

    private async Task<UserSession?> TryReadSessionAsync(HttpResponseMessage response)
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
            CloudAuthHeaders.ReadRefreshTokenExpiresAtUtc(response));
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

    private UserSession? ParseJwtToken(
        string token,
        string? refreshToken,
        DateTimeOffset? refreshTokenExpiresAtUtc)
    {
        try
        {
            var principal = ValidateJwtToken(token);
            if (principal is null)
            {
                return null;
            }

            var claims = principal.Claims.ToArray();

            var displayName = claims
                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)
                ?.Value
                ?? claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                ?? "未知用户";

            var employeeNo = claims
                .FirstOrDefault(c => string.Equals(c.Type, "employeeNo", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?? claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value
                ?? claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            var permissions = claims
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
                ExpiresAtUtc = TryGetExpiresAtUtc(claims),
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

    private ClaimsPrincipal? ValidateJwtToken(string token)
    {
        var signingKey = _jwtValidationConfig.JwtSigningKey?.Trim();
        var issuer = _jwtValidationConfig.JwtIssuer?.Trim();
        var audience = _jwtValidationConfig.JwtAudience?.Trim();
        if (string.IsNullOrWhiteSpace(signingKey)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(audience))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var validationParameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        return handler.ValidateToken(token, validationParameters, out _);
    }

    private static DateTimeOffset? TryGetExpiresAtUtc(IEnumerable<Claim> claims)
    {
        var expClaim = claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
        if (!long.TryParse(expClaim, out var exp))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(exp);
    }
}
