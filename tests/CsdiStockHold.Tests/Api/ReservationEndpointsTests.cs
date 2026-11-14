using System.Net;
using System.Text;

namespace CsdiStockHold.Tests.Api;

// These tests host the application in memory and need no database: every request here is answered
// before the database is needed (a request that cannot be read, or a missing Idempotency-Key header).
public class ReservationEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ValidBody = """{"customerId":1,"productId":1,"quantity":1}""";

    [Fact]
    public async Task Reserve_WithoutTheIdempotencyKeyHeader_Returns400()
    {
        var response = await PostReservationAsync(ValidBody, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("{}")]
    [InlineData("""{"customerId":1,"productId":1}""")]
    [InlineData("""{"customerId":1,"quantity":1}""")]
    [InlineData("""{"productId":1,"quantity":1}""")]
    public async Task Reserve_WithABodyThatCannotBeRead_Returns400(string body)
    {
        var response = await PostReservationAsync(body, idempotencyKey: "order-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    public async Task Get_WithAnIdThatIsNotAGuid_Returns404(string id)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/reservations/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostReservationAsync(string body, string? idempotencyKey)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }
}
