using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.Integration.Config;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;

namespace IIoT.Edge.Infrastructure.Integration.Auth;

public class AuthService : IAuthService, ICloudAccessTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly LocalAdminConfig _localAdminConfig;

    public UserSession? CurrentUser { get; private set; }
    public string? AccessToken => CurrentUser?.Token;
    public bool IsAuthenticated => CurrentUser is not null;
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
        if (CurrentUser is null) return false;
        if (CurrentUser.IsLocalAdmin) return true;
        if (CurrentUser.Permissions.Contains("Admin")) return true;
        return CurrentUser.Permissions.Contains(permission);
    }

    public Task<AuthResult> LoginLocalAsync(string password)
    {
        var inputHash = ComputeSha256(password);
        if (inputHash != _localAdminConfig.PasswordHash)
        {
            return Task.FromResult(AuthResult.Fail("Invalid password."));
        }

        var session = new UserSession
        {
            DisplayName = "Local Admin",
            IsLocalAdmin = true,
            Permissions = new HashSet<string>(),
            Token = null
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
            });

            if (!response.IsSuccessStatusCode)
            {
                var errors = await response.Content.ReadFromJsonAsync<string[]>();
                var errorMsg = errors?.FirstOrDefault()
                    ?? response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => "Invalid employee number or password.",
                        System.Net.HttpStatusCode.Forbidden => "This account is not allowed to operate the current device.",
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
        CurrentUser = session;
        AuthStateChanged?.Invoke(CurrentUser);
    }

    private static UserSession? ParseJwtToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var displayName = jwtToken.Claims
                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)
                ?.Value ?? "Unknown User";

            var permissions = jwtToken.Claims
                .Where(c => c.Type == "Permission" || c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToHashSet();

            return new UserSession
            {
                DisplayName = displayName,
                IsLocalAdmin = false,
                Permissions = permissions,
                Token = token
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
