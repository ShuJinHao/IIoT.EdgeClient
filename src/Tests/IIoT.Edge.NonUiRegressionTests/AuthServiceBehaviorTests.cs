using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class AuthServiceBehaviorTests
{
    [Fact]
    public async Task LoginLocalAsync_WhenHashIsMissing_ShouldFail()
    {
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = string.Empty });

        var result = await service.LoginLocalAsync("123456");

        Assert.False(result.Success);
        Assert.Equal("本地管理员未配置。", result.Message);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task LoginLocalAsync_WhenPasswordMatches_ShouldCreateLocalAdminSession()
    {
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig
            {
                PasswordHash = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92"
            });

        var result = await service.LoginLocalAsync("123456");

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.True(service.CurrentUser!.IsLocalAdmin);
        Assert.Equal("LOCAL_ADMIN", service.CurrentUser.EmployeeNo);
    }

    [Fact]
    public async Task LoginCloudAsync_ShouldParseHumanSessionAndSupportCaseInsensitivePermissions()
    {
        var token = CreateJwtToken(
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            new Claim(JwtRegisteredClaimNames.UniqueName, "E001"),
            new Claim("employeeNo", "E001"),
            new Claim("Permission", "HardwareConfig"),
            new Claim(ClaimTypes.Role, "Admin"));

        var service = CreateService(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(token)
                };
                response.Headers.Add(CloudAuthHeaders.RefreshToken, "refresh-token-1");
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
                response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("E001", service.CurrentUser!.DisplayName);
        Assert.Equal("E001", service.CurrentUser.EmployeeNo);
        Assert.Equal(token, service.CurrentUser.AccessToken);
        Assert.Equal("refresh-token-1", service.CurrentUser.RefreshToken);
        Assert.True(service.HasPermission("hardwareconfig"));
        Assert.True(service.HasPermission("anything-because-admin"));
    }

    [Fact]
    public async Task LoginCloudAsync_WhenServerReturnsErrorEnvelope_ShouldSurfaceFirstError()
    {
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    errors = new[] { "Employee is not assigned to this device." }
                })
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E009", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("Employee is not assigned to this device.", result.Message);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task IsAuthenticated_WhenCloudSessionIsExpired_ShouldKeepSessionAndRefreshInBackground()
    {
        var issuedTokens = new Queue<string>(new[]
        {
            CreateJwtToken(
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                new Claim(JwtRegisteredClaimNames.UniqueName, "E002")),
            CreateJwtToken(
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
                new Claim(JwtRegisteredClaimNames.UniqueName, "E002"))
        });
        var requestCount = 0;

        var service = CreateService(
            request =>
            {
                var currentRequest = Interlocked.Increment(ref requestCount);
                var token = issuedTokens.Dequeue();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(token)
                };
                response.Headers.Add(CloudAuthHeaders.RefreshToken, currentRequest == 1 ? "refresh-token-1" : "refresh-token-2");
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
                response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, currentRequest == 1
                    ? DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
                    : DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);

        await WaitUntilAsync(() =>
            Volatile.Read(ref requestCount) == 2
            && string.Equals(service.CurrentUser?.RefreshToken, "refresh-token-2", StringComparison.Ordinal));

        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("refresh-token-2", service.CurrentUser!.RefreshToken);
    }

    [Fact]
    public async Task IsAuthenticated_WhenCloudRefreshTokenIsExpired_ShouldClearSession()
    {
        var issuedToken = CreateJwtToken(
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            new Claim(JwtRegisteredClaimNames.UniqueName, "E002"));
        var requestCount = 0;

        var service = CreateService(
            _ =>
            {
                requestCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(issuedToken)
                };
                response.Headers.Add(CloudAuthHeaders.RefreshToken, "refresh-token-1");
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
                response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenCloudSessionIsExpired_ShouldRefreshCurrentUser()
    {
        var issuedTokens = new Queue<string>(new[]
        {
            CreateJwtToken(
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                new Claim(JwtRegisteredClaimNames.UniqueName, "E002")),
            CreateJwtToken(
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
                new Claim(JwtRegisteredClaimNames.UniqueName, "E002"))
        });
        var requestPaths = new List<string>();

        var service = CreateService(
            request =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                var token = issuedTokens.Dequeue();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(token)
                };
                response.Headers.Add(CloudAuthHeaders.RefreshToken, requestPaths.Count == 1 ? "refresh-token-1" : "refresh-token-2");
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
                response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, requestPaths.Count == 1
                    ? DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
                    : DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(await service.EnsureAuthenticatedAsync());
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("refresh-token-2", service.CurrentUser!.RefreshToken);
        Assert.Equal(
            ["/api/v1/bootstrap/edge-login", "/api/v1/human/identity/refresh"],
            requestPaths);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenRefreshFails_ShouldClearCurrentUser()
    {
        var issuedToken = CreateJwtToken(
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            new Claim(JwtRegisteredClaimNames.UniqueName, "E003"));
        var requestCount = 0;

        var service = CreateService(
            request =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(issuedToken)
                    };
                    response.Headers.Add(CloudAuthHeaders.RefreshToken, "refresh-token-3");
                    response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
                    response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
                    return response;
                }

                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = JsonContent.Create(new
                    {
                        errors = new[] { "Refresh token is invalid or expired." }
                    })
                };
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E003", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.False(await service.EnsureAuthenticatedAsync());
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(2, requestCount);
    }

    private static AuthService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        LocalAdminConfig config)
    {
        return new AuthService(
            new TestHttpClientFactory(new HttpClient(new StubMessageHandler(responseFactory))),
            new FakeCloudApiEndpointProvider(),
            config);
    }

    private static string CreateJwtToken(
        DateTimeOffset expiresAtUtc,
        params Claim[] extraClaims)
    {
        var token = new JwtSecurityToken(
            claims: extraClaims,
            expires: expiresAtUtc.UtcDateTime);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
