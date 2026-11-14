using CsdiStockHold.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CsdiStockHold.Api;

/// <summary>
/// Reports a violated business rule as an RFC 9457 problem details response. This is the only place
/// that decides which HTTP status code belongs to which <see cref="DomainErrorCode"/>.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var statusCode = GetStatusCode(domainException.Code);
        httpContext.Response.StatusCode = statusCode;

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Detail = domainException.Message,
                Extensions = { ["code"] = domainException.Code.ToString() },
            },
        });

        return true;
    }

    /// <summary>
    /// The HTTP status code of a business rule violation. The specification only asks for a suitable
    /// message, so the codes follow the usual meaning of HTTP:
    /// invalid input is 400, an unknown customer or product is 404, a lack of stock is a conflict with the
    /// current state of the product (409), and the customer limit and a reused idempotency key are rules about
    /// the request itself (422).
    /// </summary>
    public static int GetStatusCode(DomainErrorCode code) => code switch
    {
        DomainErrorCode.InvalidCustomerType
            or DomainErrorCode.InvalidProductName
            or DomainErrorCode.InvalidStockQuantity
            or DomainErrorCode.InvalidQuantity
            or DomainErrorCode.InvalidIdempotencyKey => StatusCodes.Status400BadRequest,
        DomainErrorCode.CustomerNotFound
            or DomainErrorCode.ProductNotFound => StatusCodes.Status404NotFound,
        DomainErrorCode.InsufficientStock => StatusCodes.Status409Conflict,
        DomainErrorCode.CustomerLimitExceeded
            or DomainErrorCode.IdempotencyKeyConflict => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}
