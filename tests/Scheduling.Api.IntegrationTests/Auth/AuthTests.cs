using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Auth;

namespace Scheduling.Api.IntegrationTests.Auth;

public class AuthTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Register_WithValidData_Returns201AndCustomer()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { name = "Maria", email = "Maria@Test.local", password = "Password123" }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = await ReadAsync<UserDto>(response);
        user.Email.Should().Be("maria@test.local");
        user.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task Register_WithInvalidFields_Returns400WithFieldErrors()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { name = "", email = "not-an-email", password = "short" }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.Errors.Should().ContainKeys("name", "email", "password");
        problem.TraceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_WithExistingEmail_Returns409()
    {
        using var client = CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new { name = "Maria", email = "maria@test.local", password = "Password123" }, Json, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { name = "Other", email = "MARIA@test.local", password = "Password123" }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(response)).Code.Should().Be("Auth.EmailTaken");
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenThatExpiresInOneHour()
    {
        using var client = await CreateCustomerClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "maria@test.local", password = "Password123" }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await ReadAsync<AccessToken>(response);
        token.Token.Should().NotBeNullOrWhiteSpace();
        token.ExpiresAt.Should().Be(Clock.GetUtcNow().AddHours(1));
    }

    [Theory]
    [InlineData("maria@test.local", "WrongPassword1")]
    [InlineData("nobody@test.local", "Password123")]
    public async Task Login_WithBadCredentials_Returns401WithoutRevealingWhichPartIsWrong(string email, string password)
    {
        using var _ = await CreateCustomerClientAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(response)).Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithTamperedToken_Returns401()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.invalid-signature");

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsCurrentUser()
    {
        using var client = await CreateCustomerClientAsync();

        var user = await client.GetFromJsonAsync<UserDto>("/api/auth/me", Json, Ct);

        user!.Email.Should().Be("maria@test.local");
    }

    [Fact]
    public async Task Me_AfterTokenExpires_Returns401()
    {
        using var client = await CreateCustomerClientAsync();

        Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class AuthRateLimitTests(DatabaseFixture database) : ApiTest(database)
{
    protected override SchedulingApiFactory CreateFactory() =>
        new(Database, new Dictionary<string, string> { ["RateLimiting:AuthPermitLimit"] = "3" });

    [Fact]
    public async Task Login_AfterTooManyAttempts_Returns429()
    {
        using var client = CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "x@test.local", password = "Wrong123" }, Json, Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(
            HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
    }
}
