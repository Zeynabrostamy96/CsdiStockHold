using System.Text.Json;
using CsdiStockHold.Api;
using CsdiStockHold.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CsdiStockHold.Tests.Api;

public class DomainExceptionHandlerTests
{
    [Theory]
    [InlineData(DomainErrorCode.InvalidCustomerType, 400)]
    [InlineData(DomainErrorCode.InvalidProductName, 400)]
    [InlineData(DomainErrorCode.InvalidStockQuantity, 400)]
    [InlineData(DomainErrorCode.InvalidQuantity, 400)]
    [InlineData(DomainErrorCode.InvalidIdempotencyKey, 400)]
    [InlineData(DomainErrorCode.CustomerNotFound, 404)]
    [InlineData(DomainErrorCode.ProductNotFound, 404)]
    [InlineData(DomainErrorCode.InsufficientStock, 409)]
    [InlineData(DomainErrorCode.CustomerLimitExceeded, 422)]
    [InlineData(DomainErrorCode.IdempotencyKeyConflict, 422)]
    public void GetStatusCode_MapsEachBusinessRuleToItsDocumentedStatus(DomainErrorCode code, int expectedStatus)
    {
        Assert.Equal(expectedStatus, DomainExceptionHandler.GetStatusCode(code));
    }

    [Fact]
    public void GetStatusCode_HasAMappingForEveryDomainErrorCode()
    {
        // 500 is only the fallback for a code nobody mapped, so a new DomainErrorCode must not end up there.
        foreach (var code in Enum.GetValues<DomainErrorCode>())
        {
            Assert.NotEqual(StatusCodes.Status500InternalServerError, DomainExceptionHandler.GetStatusCode(code));
        }
    }

    [Fact]
    public async Task TryHandleAsync_WithDomainException_WritesProblemDetailsWithStatusDetailAndCode()
    {
        var (handler, httpContext) = CreateHandler();
        var exception = new DomainException(DomainErrorCode.InsufficientStock, "Not enough stock. Available: 1, requested: 4.");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);

        httpContext.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not enough stock. Available: 1, requested: 4.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("InsufficientStock", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_WithAnyOtherException_DoesNotHandleIt()
    {
        var (handler, httpContext) = CreateHandler();

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    // The real problem details service, without a web host.
    private static (DomainExceptionHandler Handler, DefaultHttpContext HttpContext) CreateHandler()
    {
        var services = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        return (new DomainExceptionHandler(services.GetRequiredService<IProblemDetailsService>()), httpContext);
    }
}
