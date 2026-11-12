using CsdiStockHold.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The connection string is not set yet. It is provided later through configuration
// (appsettings.json or the ConnectionStrings__StockHold environment variable).
builder.Services.AddDbContext<StockHoldDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockHold")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
