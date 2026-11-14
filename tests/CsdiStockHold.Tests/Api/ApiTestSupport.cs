using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsdiStockHold.Tests.Api;

public static class ApiTestSupport
{
    /// <summary>The JSON settings of the API: web defaults and enum values as text.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The "code" of a problem details response: the name of the business rule that was violated.</summary>
    public static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        return problem.RootElement.GetProperty("code").GetString();
    }
}
