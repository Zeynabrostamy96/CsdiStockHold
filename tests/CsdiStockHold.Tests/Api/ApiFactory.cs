using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CsdiStockHold.Tests.Api;

/// <summary>
/// Hosts the real application in memory. Without a connection string (the default, like the repository
/// itself) it can only answer requests that are decided before the database is needed, for example
/// a request that cannot be read or a request that breaks a business rule of a new product or customer.
/// The integration tests give it the connection string of their temporary database and a fixed time.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string? ConnectionString { get; init; }

    public TimeProvider? Time { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (ConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:StockHold", ConnectionString);
        }

        if (Time is { } time)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }
    }
}
