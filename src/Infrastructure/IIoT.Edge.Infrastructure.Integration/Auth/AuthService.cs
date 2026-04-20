using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.Integration.Config;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;

namespace IIoT.Edge.Infrastructure.Integration.Auth;

public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly LocalAdminConfig _localAdminConfig;
    private UserSession? _currentUser;

    public UserSession? CurrentUser => GetCurrentActiveSession();
    public bool IsAuthenticated => GetCurrentActiveSession() is not null;
    public event Action<UserSession?>? AuthStateChanged;

    public AuthService(
        HttpClient httpClient,
        ICloudApiEndpointProvider endpointProvider,
        LocalAdminConfig localAdminConfig)
    {
        _httpClient = httpClient;
        _endpointProvider = endpointProvider;
        _localAdminConfig = localAdminConfig;
    }

    public bool HasPermission(string permission)
    {
        var session = GetCurrentActiveSession();
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
            return Task.FromResult(AuthResult.Fail("Local admin is not configured."));
        }

        var inputHash = ComputeSha256(password);
        if (!string.Equals(inputHash, configuredHash, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthResult.Fail("Invalid password."));
        }

        var session = new UserSession
        {
            DisplayName = "Local Admin",
            EmployeeNo = "LOCAL_ADMIN",
            IsLocalAdmin = true,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ExpiresAtUtc = null
        };

        SetSession(session);
        return Task.FromResult(AuthResult.Ok("Local admin login succeeded."));
    }

    public async Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
    {
        try
        {
            var loginUrl = _endpointProvider.BuildUrl(_endpointProvider.GetIdentityDeviceLoginPath());
            var response = await _httpClient.PostAsJsonAsync(loginUrl, new
            {
                employeeNo,
                password,
                deviceId
            }).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errors = await response.Content.ReadFromJsonAsync<string[]>();
                var errorMsg = errors?.FirstOrDefault()
                    ?? response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => "Invalid employee number or password.",
                        System.Net.HttpStatusCode.Forbidden => "This account is not allowed to operate the current device.",
                        System.Net.HttpStatusCode.BadRequest => "Login request was rejected.",
                        >= System.Net.HttpStatusCode.InternalServerError => "Server is temporarily unavailable.",
                        _ => $"Login failed: {response.StatusCode}"
                    };

                return AuthResult.Fail(errorMsg);
            }

            var token = await response.Content.ReadAsStringAsync();
            token = token.Trim('"');

            if (string.IsNullOrEmpty(token))
            {
                return AuthResult.Fail("Server returned an empty token.");
            }

            var session = ParseJwtToken(token);
            if (session is null)
            {
                return AuthResult.Fail("Token parse failed.");
            }

            SetSession(session);
            return AuthResult.Ok($"Welcome, {session.DisplayName}");
        }
        catch (TaskCanceledException)
        {
            return AuthResult.Fail("Connection timeout.");
        }
        catch (HttpRequestException)
        {
            return AuthResult.Fail("Cannot reach the server.");
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Login exception: {ex.Message}");
        }
    }

    public void Logout() => SetSession(null);

    private void SetSession(UserSession? session)
    {
        _currentUser = session;
        AuthStateChanged?.Invoke(_currentUser);
    }

    private UserSession? GetCurrentActiveSession()
    {
        if (_currentUser is null)
        {
            return null;
        }

        if (_currentUser.ExpiresAtUtc.HasValue
            && _currentUser.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            SetSession(null);
            return null;
        }

        return _currentUser;
    }

    private static UserSession? ParseJwtToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var displayName = jwtToken.Claims
                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)
                ?.Value
                ?? jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                ?? "Unknown User";

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

            var expiresAtUtc = TryGetExpiresAtUtc(jwtToken);

            return new UserSession
            {
                DisplayName = displayName,
                EmployeeNo = employeeNo,
                IsLocalAdmin = false,
                Permissions = permissions,
                ExpiresAtUtc = expiresAtUtc
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
