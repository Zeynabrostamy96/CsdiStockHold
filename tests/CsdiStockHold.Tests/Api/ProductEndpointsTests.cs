using System.Net;
using System.Text;

namespace CsdiStockHold.Tests.Api;

// These tests host the application in memory and need no database: every request here is answered
// before the database is needed (a request that cannot be read, or a product that breaks a business rule).
public class ProductEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("{}")]
    [InlineData("""{"name":"Laptop"}""")]
    [InlineData("""{"stockQuantity":5}""")]
    public async Task Create_WithABodyThatCannotBeRead_Returns400(string body)
    {
        var response = await PostProductAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"","stockQuantity":5}""", "InvalidProductName")]
    [InlineData("""{"name":"   ","stockQuantity":5}""", "InvalidProductName")]
    [InlineData("""{"name":null,"stockQuantity":5}""", "InvalidProductName")]
    [InlineData("""{"name":"Laptop","stockQuantity":-1}""", "InvalidStockQuantity")]
    public async Task Create_WithInvalidValues_Returns400WithTheViolatedRule(string body, string expectedCode)
    {
        var response = await PostProductAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await ApiTestSupport.ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_WithANameLongerThan200Characters_Returns400WithTheViolatedRule()
    {
        var response = await PostProductAsync($$"""{"name":"{{new string('a', 201)}}","stockQuantity":5}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidProductName", await ApiTestSupport.ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task Get_WithAnIdThatIsNotANumber_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostProductAsync(string body)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        return await client.PostAsync("/api/products", content);
    }
}
