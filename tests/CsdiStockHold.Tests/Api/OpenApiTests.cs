using System.Net;
using System.Text.Json;

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
}
