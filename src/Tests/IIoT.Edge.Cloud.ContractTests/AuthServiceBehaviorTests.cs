using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.SharedKernel.Security;
using Microsoft.IdentityModel.Tokens;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class AuthServiceBehaviorTests
{
    private const string LegacySha256Password123456 = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";
    private const string TestJwtSigningKey = "edge-client-test-signing-key-2026-minimum-32-bytes";
    private const string OtherJwtSigningKey = "edge-client-other-signing-key-2026-minimum-32-bytes";
    private const string TestJwtIssuer = "iiot-cloud-test";
    private const string TestJwtAudience = "iiot-edge-client-test";

    [Fact]
    public async Task LoginLocalAsync_WhenHashIsMissing_ShouldFail()
    {
        var store = new FakeLocalAdminCredentialStore();
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = string.Empty },
            store);

        var result = await service.LoginLocalAsync("123456");

        Assert.False(result.Success);
        Assert.Equal("本地管理员未配置，请先初始化。", result.Message);
        Assert.Equal(LocalAdminCredentialStatus.NotConfigured, service.LocalAdminCredentialStatus);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeLocalAdminAsync_WhenHashIsMissing_ShouldPersistPbkdf2AndCreateSession()
    {
        var store = new FakeLocalAdminCredentialStore();
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = string.Empty },
            store);

        var result = await service.InitializeLocalAdminAsync("NewPass123!");

        Assert.True(result.Success);
        Assert.StartsWith("pbkdf2-sha256$v1$", store.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(LocalAdminCredentialStatus.Ready, service.LocalAdminCredentialStatus);
        Assert.True(service.IsAuthenticated);
        Assert.True(service.CurrentUser?.IsLocalAdmin);
    }

    [Fact]
    public async Task LoginLocalAsync_WhenPasswordMatches_ShouldCreateLocalAdminSession()
    {
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig
            {
                PasswordHash = EdgePasswordHasher.HashPassword("123456")
            });

        var result = await service.LoginLocalAsync("123456");

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.True(service.CurrentUser!.IsLocalAdmin);
        Assert.Equal("LOCAL_ADMIN", service.CurrentUser.EmployeeNo);
    }

    [Fact]
    public async Task LoginLocalAsync_WhenStoredHashIsLegacySha256_ShouldRequireResetWithoutSession()
    {
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = LegacySha256Password123456 });

        var result = await service.LoginLocalAsync("123456");

        Assert.False(result.Success);
        Assert.Equal("本地管理员密码使用旧哈希格式，请先重置。", result.Message);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
    }

    [Fact]
    public async Task ResetLocalAdminPasswordAsync_WhenStoredHashIsLegacySha256_ShouldPersistPbkdf2AndCreateSession()
    {
        var store = new FakeLocalAdminCredentialStore(LegacySha256Password123456);
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = string.Empty },
            store);

        var result = await service.ResetLocalAdminPasswordAsync("123456", "NewPass123!");

        Assert.True(result.Success);
        Assert.StartsWith("pbkdf2-sha256$v1$", store.PasswordHash, StringComparison.Ordinal);
        Assert.NotEqual(LegacySha256Password123456, store.PasswordHash);
        Assert.Equal(LocalAdminCredentialStatus.Ready, service.LocalAdminCredentialStatus);
        Assert.True(service.CurrentUser?.IsLocalAdmin);
    }

    [Fact]
    public async Task LoginLocalAsync_WhenStoreHasHashAndConfigIsEmpty_ShouldCreateSession()
    {
        var store = new FakeLocalAdminCredentialStore(EdgePasswordHasher.HashPassword("NewPass123!"));
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new LocalAdminConfig { PasswordHash = string.Empty },
            store);

        var result = await service.LoginLocalAsync("NewPass123!");

        Assert.True(result.Success);
        Assert.True(service.CurrentUser?.IsLocalAdmin);
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
    public async Task LoginCloudAsync_WhenJwtSignatureDoesNotMatch_ShouldRejectSession()
    {
        var token = CreateJwtToken(
            DateTimeOffset.UtcNow.AddMinutes(10),
            OtherJwtSigningKey,
            new Claim(JwtRegisteredClaimNames.UniqueName, "E001"),
            new Claim("Permission", "HardwareConfig"));
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(token)
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenJwtIssuerOrAudienceIsMissing_ShouldRejectSessionWithoutBlockingStartup()
    {
        var token = CreateJwtToken(
            DateTimeOffset.UtcNow.AddMinutes(10),
            new Claim(JwtRegisteredClaimNames.UniqueName, "E001"),
            new Claim("Permission", "HardwareConfig"));
        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(token)
            },
            new LocalAdminConfig { PasswordHash = EdgePasswordHasher.HashPassword("123456") },
            jwtValidationConfig: new CloudJwtValidationConfig { JwtSigningKey = TestJwtSigningKey });

        var cloudResult = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());
        var localResult = await service.LoginLocalAsync("123456");

        Assert.False(cloudResult.Success);
        Assert.Equal("云端登录令牌无效。", cloudResult.Message);
        Assert.True(localResult.Success);
        Assert.True(service.CurrentUser?.IsLocalAdmin);
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
    public async Task LoginCloudAsync_WhenAccessTokenIsExpired_ShouldRejectSession()
    {
        var token = CreateJwtToken(
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            new Claim(JwtRegisteredClaimNames.UniqueName, "E002"));

        var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(token)
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
    }

    [Fact]
    public async Task IsAuthenticated_WhenCloudRefreshTokenIsExpired_ShouldClearSession()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var issuedToken = CreateJwtToken(
            expiresAtUtc: timeProvider.GetUtcNow().AddSeconds(1),
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
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, timeProvider.GetUtcNow().AddSeconds(1).ToString("O"));
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" },
            timeProvider: timeProvider);

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenCloudSessionIsExpired_ShouldRefreshCurrentUser()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var issuedTokens = new Queue<string>(new[]
        {
            CreateJwtToken(
                expiresAtUtc: timeProvider.GetUtcNow().AddSeconds(1),
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
                return response;
            },
            new LocalAdminConfig { PasswordHash = "unused" },
            timeProvider: timeProvider);

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await service.EnsureAuthenticatedAsync(TestContext.Current.CancellationToken));
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
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var issuedToken = CreateJwtToken(
            expiresAtUtc: timeProvider.GetUtcNow().AddSeconds(1),
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
            new LocalAdminConfig { PasswordHash = "unused" },
            timeProvider: timeProvider);

        var result = await service.LoginCloudAsync("E003", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.False(await service.EnsureAuthenticatedAsync(TestContext.Current.CancellationToken));
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(2, requestCount);
    }

    private static AuthService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        LocalAdminConfig config,
        ILocalAdminCredentialStore? credentialStore = null,
        CloudJwtValidationConfig? jwtValidationConfig = null,
        TimeProvider? timeProvider = null)
    {
        return new AuthService(
            new TestHttpClientFactory(new HttpClient(new StubMessageHandler(responseFactory))),
            new FakeCloudApiEndpointProvider(),
            config,
            credentialStore ?? new FakeLocalAdminCredentialStore(),
            jwtValidationConfig
            ?? new CloudJwtValidationConfig
            {
                JwtSigningKey = TestJwtSigningKey,
                JwtIssuer = TestJwtIssuer,
                JwtAudience = TestJwtAudience
            },
            timeProvider);
    }

    private static string CreateJwtToken(
        DateTimeOffset expiresAtUtc,
        params Claim[] extraClaims)
        => CreateJwtToken(expiresAtUtc, TestJwtSigningKey, extraClaims);

    private static string CreateJwtToken(
        DateTimeOffset expiresAtUtc,
        string signingKey,
        params Claim[] extraClaims)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: extraClaims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class FakeLocalAdminCredentialStore(string? passwordHash = null) : ILocalAdminCredentialStore
    {
        public string? PasswordHash { get; private set; } = passwordHash;

        public string? ReadPasswordHash() => PasswordHash;

        public void WritePasswordHash(string passwordHash)
        {
            PasswordHash = passwordHash;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
