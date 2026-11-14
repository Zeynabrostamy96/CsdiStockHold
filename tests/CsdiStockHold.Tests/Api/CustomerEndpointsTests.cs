using System.Net;
using System.Text;

namespace CsdiStockHold.Tests.Api;

// These tests host the application in memory and need no database: every request here is answered
// before the database is needed (a request that cannot be read, or a customer type that does not exist).
public class CustomerEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("{}")]
    [InlineData("""{"type":"Gold"}""")]
    public async Task Create_WithABodyThatCannotBeRead_Returns400(string body)
    {
        var response = await PostCustomerAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"type":0}""")]
    [InlineData("""{"type":99}""")]
    public async Task Create_WithATypeNumberThatIsNoCustomerType_Returns400WithTheViolatedRule(string body)
    {
        var response = await PostCustomerAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidCustomerType", await ApiTestSupport.ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task Get_WithAnIdThatIsNotANumber_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostCustomerAsync(string body)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        return await client.PostAsync("/api/customers", content);
    }
}
