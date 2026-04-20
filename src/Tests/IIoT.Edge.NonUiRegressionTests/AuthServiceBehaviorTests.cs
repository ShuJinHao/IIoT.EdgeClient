using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Config;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

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
        Assert.Equal("Local admin is not configured.", result.Message);
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
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(token)
            },
            new LocalAdminConfig { PasswordHash = "unused" });

        var result = await service.LoginCloudAsync("E001", "pwd", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("E001", service.CurrentUser!.DisplayName);
        Assert.Equal("E001", service.CurrentUser.EmployeeNo);
        Assert.True(service.HasPermission("hardwareconfig"));
        Assert.True(service.HasPermission("anything-because-admin"));
    }

    [Fact]
    public async Task IsAuthenticated_WhenCloudSessionIsExpired_ShouldClearCurrentUser()
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

        Assert.True(result.Success);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
    }

    private static AuthService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        LocalAdminConfig config)
    {
        return new AuthService(
            new HttpClient(new StubMessageHandler(responseFactory)),
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

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
