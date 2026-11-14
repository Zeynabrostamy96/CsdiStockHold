using CsdiStockHold.Domain;
using CsdiStockHold.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/customers").WithTags("Customers");

        customers.MapPost("/", CreateAsync)
            .WithSummary("Registers a new customer.")
            .WithDescription("A Regular customer can reserve up to 3 units and a reservation stays valid for 10 minutes. "
                + "A Special customer can reserve up to 10 units and a reservation stays valid for 30 minutes.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        customers.MapGet("/{id:int}", GetAsync)
            .WithSummary("Gets a customer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Created<CustomerResponse>> CreateAsync(
        CreateCustomerRequest request,
        StockHoldDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // The domain checks that the customer type is one of the two defined types.
        var customer = new Customer(request.Type);
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/customers/{customer.Id}", CustomerResponse.From(customer));
    }

    private static async Task<Results<Ok<CustomerResponse>, NotFound>> GetAsync(
        int id,
        StockHoldDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

        return customer is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CustomerResponse.From(customer));
    }
}

public sealed record CreateCustomerRequest
{
    public required CustomerType Type { get; init; }
}

public sealed record CustomerResponse(int Id, CustomerType Type)
{
    public static CustomerResponse From(Customer customer) => new(customer.Id, customer.Type);
}
