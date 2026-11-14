using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace CsdiStockHold.Tests.Api;

// These tests host the application in memory and need no database.
public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task OpenApiDocument_IsServedAsJson()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
    }

    [Theory]
    [InlineData("/api/products", "post")]
    [InlineData("/api/products/{id}", "get")]
    [InlineData("/api/customers", "post")]
    [InlineData("/api/customers/{id}", "get")]
    [InlineData("/api/reservations", "post")]
    [InlineData("/api/reservations/expired", "get")]
    [InlineData("/api/reservations/{id}", "get")]
    public async Task OpenApiDocument_DescribesEveryEndpoint(string path, string method)
    {
        using var document = await GetDocumentAsync();

        var operations = document.RootElement.GetProperty("paths").GetProperty(path);

        Assert.True(operations.TryGetProperty(method, out _), $"{method.ToUpperInvariant()} {path} is not documented.");
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheIdempotencyKeyHeaderOfAReservationAsRequired()
    {
        using var document = await GetDocumentAsync();

        var parameters = document.RootElement.GetProperty("paths").GetProperty("/api/reservations")
            .GetProperty("post").GetProperty("parameters").EnumerateArray();

        var header = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheResponsesOfAReservation()
    {
        using var document = await GetDocumentAsync();

        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/reservations")
            .GetProperty("post").GetProperty("responses");

        foreach (var status in new[] { "200", "201", "400", "404", "409", "422" })
        {
            Assert.True(responses.TryGetProperty(status, out _), $"The response {status} of a reservation is not documented.");
        }
    }

    [Fact]
    public async Task OpenApiDocument_ShowsTheCustomerTypesAsText()
    {
        using var document = await GetDocumentAsync();

        var customerTypes = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("CustomerType").GetProperty("enum").EnumerateArray().Select(value => value.GetString());

        Assert.Equal(["Regular", "Special"], customerTypes);
    }

    [Fact]
    public async Task SwaggerUi_IsServedAndPointsToTheOpenApiDocument()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        // The page loads its settings, which name the OpenAPI document, from index.js.
        var settings = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains("/openapi/v1.json", settings);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task OpenApiDocumentAndSwaggerUi_AreAlsoAvailableOutsideDevelopment(string path)
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var client = factory.CreateClient();

        return await JsonDocument.ParseAsync(await client.GetStreamAsync("/openapi/v1.json"));
    }
}
