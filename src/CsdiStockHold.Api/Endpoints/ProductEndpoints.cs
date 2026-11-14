using CsdiStockHold.Domain;
using CsdiStockHold.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products").WithTags("Products");

        products.MapPost("/", CreateAsync)
            .WithSummary("Registers a new product.")
            .WithDescription("A product has a name and a stock quantity. The available quantity of a new product is its stock.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        products.MapGet("/{id:int}", GetAsync)
            .WithSummary("Gets a product with its available quantity.")
            .WithDescription("The available quantity is the stock minus the units held by active reservations; expired reservations do not count.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Created<ProductResponse>> CreateAsync(
        CreateProductRequest request,
        StockHoldDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // The domain checks the name and the stock quantity.
        var product = new Product(request.Name, request.StockQuantity);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/products/{product.Id}", ProductResponse.From(product, reservedQuantity: 0));
    }

    private static async Task<Results<Ok<ProductResponse>, NotFound>> GetAsync(
        int id,
        StockHoldDbContext dbContext,
        ReservationService reservations,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var reservedQuantity = await reservations.GetActiveReservedQuantityAsync(id, cancellationToken);

        return TypedResults.Ok(ProductResponse.From(product, reservedQuantity));
    }
}

public sealed record CreateProductRequest
{
    public required string Name { get; init; }

    public required int StockQuantity { get; init; }
}

public sealed record ProductResponse(int Id, string Name, int StockQuantity, int AvailableQuantity)
{
    public static ProductResponse From(Product product, int reservedQuantity) =>
        new(product.Id, product.Name, product.StockQuantity, product.GetAvailableQuantity(reservedQuantity));
}
