using System.Text.Json;
using Conformer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var port = Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } p ? int.Parse(p) : 8080;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.ClearProviders();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));

app.MapPost("/execute", async (HttpRequest req) =>
{
    ExecuteRequest? payload;
    try
    {
        payload = await JsonSerializer.DeserializeAsync<ExecuteRequest>(req.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (Exception e)
    {
        return ErrorResponse(400, e.Message);
    }

    if (payload?.Schema is null || payload.Query is null)
        return ErrorResponse(400, "schema and query are required strings");

    try
    {
        var resultJson = await GraphQLExecutor.ExecuteAsync(payload);
        return Results.Content(resultJson, "application/json", null, 200);
    }
    catch (Exception e)
    {
        return ErrorResponse(500, e.Message);
    }
});

app.Run();
return 0;

static IResult ErrorResponse(int status, string message)
{
    var body = JsonSerializer.Serialize(new { errors = new[] { new { message } } });
    return Results.Content(body, "application/json", null, status);
}
