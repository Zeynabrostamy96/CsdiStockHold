using System.Text.Json.Serialization;
using CsdiStockHold.Api;
using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Enum values are read and written as text (for example "Regular"), which is easier to use than numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// A request that cannot be read (broken JSON, a missing property, ...) is answered with 400.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

// Errors are reported as problem details. A violated business rule is mapped to its HTTP status code
// by DomainExceptionHandler.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// The connection string is not set yet. It is provided later through configuration
// (appsettings.json or the ConnectionStrings__StockHold environment variable).
builder.Services.AddDbContext<StockHoldDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockHold")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReservationService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapProductEndpoints();
app.MapCustomerEndpoints();

app.Run();

// Makes the entry point visible to the tests, which host the application in memory.
public partial class Program;
