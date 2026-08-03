using System.Net;
using System.Net.Http.Json;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class AuthServiceBehaviorTests
{
    private const string LegacySha256Password123456 = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";
    private const string HumanSessionValidationPath = "/api/v1/human/identity/session";

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
    public async Task LoginCloudAsync_ShouldBuildTrustedHumanSessionAndSupportCaseInsensitivePermissions()
    {
        const string token = "opaque-access-token-1";

        var requestPaths = new List<string>();
        var service = CreateService(
            request =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                if (request.RequestUri.AbsolutePath == HumanSessionValidationPath)
                {
                    Assert.Equal($"Bearer {token}", request.Headers.Authorization?.ToString());
                    return CreateSessionResponse(
                        employeeNo: "E001",
                        displayName: "张三",
                        roles: ["Admin"],
                        permissions: ["HardwareConfig"]);
                }

                return CreateTokenResponse(token, "refresh-token-1");
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("张三", service.CurrentUser!.DisplayName);
        Assert.Equal("E001", service.CurrentUser.EmployeeNo);
        Assert.Equal(token, service.CurrentUser.AccessToken);
        Assert.Equal("refresh-token-1", service.CurrentUser.RefreshToken);
        Assert.True(service.HasPermission("hardwareconfig"));
        Assert.True(service.HasPermission("anything-because-admin"));
        Assert.Equal(
            ["/api/v1/bootstrap/edge-login", HumanSessionValidationPath],
            requestPaths);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenCloudRejectsExactBearerToken_ShouldRejectSession()
    {
        const string token = "opaque-access-token-rejected";
        var requestPaths = new List<string>();
        var service = CreateService(
            request =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                return request.RequestUri.AbsolutePath == HumanSessionValidationPath
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : CreateTokenResponse(token);
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(
            ["/api/v1/bootstrap/edge-login", HumanSessionValidationPath],
            requestPaths);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenAccessTokenIsOpaque_ShouldUseCloudSessionWithoutParsingToken()
    {
        const string token = "this-is-intentionally-not-a-jwt";
        var service = CreateService(
            request => request.RequestUri!.AbsolutePath == HumanSessionValidationPath
                ? CreateSessionResponse(
                    employeeNo: "E001",
                    displayName: "李四",
                    permissions: ["HardwareConfig"])
                : CreateTokenResponse(token),
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.Equal("E001", service.CurrentUser?.EmployeeNo);
        Assert.Equal("李四", service.CurrentUser?.DisplayName);
        Assert.Equal(token, service.CurrentUser?.AccessToken);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenCloudValidationIsUnavailable_ShouldFailClosedWithoutBlockingLocalLogin()
    {
        const string token = "opaque-access-token-unavailable";
        var service = CreateService(
            request => request.RequestUri!.AbsolutePath == HumanSessionValidationPath
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : CreateTokenResponse(token),
            new LocalAdminConfig { PasswordHash = EdgePasswordHasher.HashPassword("123456") });

        var cloudResult = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());
        var localResult = await service.LoginLocalAsync("123456");

        Assert.False(cloudResult.Success);
        Assert.Equal("云端登录令牌无效。", cloudResult.Message);
        Assert.True(localResult.Success);
        Assert.True(service.CurrentUser?.IsLocalAdmin);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenSessionDtoIsMissingRequiredField_ShouldRejectSession()
    {
        const string token = "opaque-access-token-malformed-session";
        var service = CreateService(
            request => request.RequestUri!.AbsolutePath == HumanSessionValidationPath
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        userId = Guid.NewGuid(),
                        employeeNo = "E001",
                        roles = Array.Empty<string>(),
                        permissions = Array.Empty<string>()
                    })
                }
                : CreateTokenResponse(token),
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenSessionDtoContainsInvalidPermission_ShouldRejectSession()
    {
        const string token = "opaque-access-token-invalid-permission";
        var service = CreateService(
            request => request.RequestUri!.AbsolutePath == HumanSessionValidationPath
                ? CreateSessionResponse(
                    employeeNo: "E001",
                    displayName: "王五",
                    permissions: ["HardwareConfig", " "])
                : CreateTokenResponse(token),
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.Null(service.CurrentUser);
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
        const string token = "opaque-access-token-expired";

        var service = CreateService(
            request => request.RequestUri!.AbsolutePath == HumanSessionValidationPath
                ? CreateSessionResponse(employeeNo: "E002", displayName: "赵六")
                : CreateTokenResponse(
                    token,
                    accessTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1)),
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
    }

    [Fact]
    public async Task LoginCloudAsync_WhenAccessTokenExpiryHeaderIsMissing_ShouldRejectBeforeSessionRequest()
    {
        const string token = "opaque-access-token-no-expiry";
        var requestPaths = new List<string>();
        var service = CreateService(
            request =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                return CreateTokenResponse(token, includeAccessTokenExpiresAt: false);
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("云端登录令牌无效。", result.Message);
        Assert.Null(service.CurrentUser);
        Assert.Equal(["/api/v1/bootstrap/edge-login"], requestPaths);
    }

    [Fact]
    public async Task IsAuthenticated_WhenCloudRefreshTokenIsExpired_ShouldClearSession()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        const string issuedToken = "opaque-access-token-short-lived";
        var requestCount = 0;

        var service = CreateService(
            request =>
            {
                requestCount++;
                if (request.RequestUri!.AbsolutePath == HumanSessionValidationPath)
                {
                    return CreateSessionResponse(employeeNo: "E002", displayName: "钱七");
                }

                return CreateTokenResponse(
                    issuedToken,
                    "refresh-token-1",
                    timeProvider.GetUtcNow().AddSeconds(1),
                    timeProvider.GetUtcNow().AddSeconds(1));
            },
            new LocalAdminConfig { PasswordHash = "unused" },
            timeProvider: timeProvider);

        var result = await service.LoginCloudAsync("E002", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenCloudSessionIsExpired_ShouldRefreshCurrentUser()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var issuedTokens = new Queue<string>(new[]
        {
            "opaque-access-token-before-refresh",
            "opaque-access-token-after-refresh"
        });
        var requestPaths = new List<string>();
        var bearerHeaders = new List<string?>();
        var sessionRequestCount = 0;

        var service = CreateService(
            request =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                if (request.RequestUri.AbsolutePath == HumanSessionValidationPath)
                {
                    bearerHeaders.Add(request.Headers.Authorization?.ToString());
                    sessionRequestCount++;
                    return sessionRequestCount == 1
                        ? CreateSessionResponse(employeeNo: "E002", displayName: "孙八")
                        : CreateSessionResponse(
                            employeeNo: "E002",
                            displayName: "刷新后姓名",
                            permissions: ["Recipe.Read"]);
                }

                var token = issuedTokens.Dequeue();
                return CreateTokenResponse(
                    token,
                    request.RequestUri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal)
                        ? "refresh-token-2"
                        : "refresh-token-1",
                    accessTokenExpiresAtUtc:
                        request.RequestUri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal)
                            ? timeProvider.GetUtcNow().AddMinutes(10)
                            : timeProvider.GetUtcNow().AddSeconds(1));
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
        Assert.Equal("刷新后姓名", service.CurrentUser.DisplayName);
        Assert.True(service.HasPermission("recipe.read"));
        Assert.Equal(
            [
                "Bearer opaque-access-token-before-refresh",
                "Bearer opaque-access-token-after-refresh"
            ],
            bearerHeaders);
        Assert.Equal(
            [
                "/api/v1/bootstrap/edge-login",
                HumanSessionValidationPath,
                "/api/v1/human/identity/refresh",
                HumanSessionValidationPath
            ],
            requestPaths);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenRefreshFails_ShouldClearCurrentUser()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        const string issuedToken = "opaque-access-token-refresh-fails";
        var requestCount = 0;

        var service = CreateService(
            request =>
            {
                requestCount++;
                if (request.RequestUri!.AbsolutePath == HumanSessionValidationPath)
                {
                    return CreateSessionResponse(employeeNo: "E003", displayName: "周九");
                }

                if (request.RequestUri.AbsolutePath == "/api/v1/bootstrap/edge-login")
                {
                    return CreateTokenResponse(
                        issuedToken,
                        "refresh-token-3",
                        accessTokenExpiresAtUtc: timeProvider.GetUtcNow().AddSeconds(1));
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
        Assert.Equal(3, requestCount);
    }

    private static AuthService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        LocalAdminConfig config,
        ILocalAdminCredentialStore? credentialStore = null,
        TimeProvider? timeProvider = null)
    {
        return new AuthService(
            new TestHttpClientFactory(new HttpClient(new StubMessageHandler(responseFactory))),
            new FakeCloudApiEndpointProvider(),
            config,
            credentialStore ?? new FakeLocalAdminCredentialStore(),
            timeProvider);
    }

    private static HttpResponseMessage CreateTokenResponse(
        string token,
        string? refreshToken = null,
        DateTimeOffset? refreshTokenExpiresAtUtc = null,
        DateTimeOffset? accessTokenExpiresAtUtc = null,
        bool includeAccessTokenExpiresAt = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(token)
        };
        if (includeAccessTokenExpiresAt)
        {
            response.Headers.Add(
                CloudAuthHeaders.AccessTokenExpiresAt,
                (accessTokenExpiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(10)).ToString("O"));
        }

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            response.Headers.Add(CloudAuthHeaders.RefreshToken, refreshToken);
            response.Headers.Add(
                CloudAuthHeaders.RefreshTokenExpiresAt,
                (refreshTokenExpiresAtUtc ?? DateTimeOffset.UtcNow.AddDays(7)).ToString("O"));
        }

        return response;
    }

    private static HttpResponseMessage CreateSessionResponse(
        string employeeNo,
        string displayName,
        string[]? roles = null,
        string[]? permissions = null,
        Guid? userId = null)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                userId = userId ?? Guid.NewGuid(),
                employeeNo,
                displayName,
                roles = roles ?? Array.Empty<string>(),
                permissions = permissions ?? Array.Empty<string>()
            })
        };

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
