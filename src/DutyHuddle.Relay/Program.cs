using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using DutyHuddle.Relay;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Relay").Get<RelayOptions>() ?? new RelayOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddHostedService<PruneService>();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.RequestsPerMinutePerClient,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// The relay stores nothing worth logging; keep request logging off by default.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/v1/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/rooms/{roomId}", (string roomId, HttpContext context, RoomStore store) =>
{
    if (!Ids.IsValid(roomId))
        return Results.BadRequest();

    var room = store.Get(roomId);
    if (room is null)
        return Results.NotFound();

    // Proxies that compress responses may weaken the ETag (W/"n"); compare without the prefix.
    var etag = $"\"{room.Version}\"";
    var presented = context.Request.Headers.IfNoneMatch
        .SelectMany(value => (value ?? string.Empty).Split(','))
        .Select(tag => tag.Trim());
    if (presented.Any(tag => tag == etag || tag == "W/" + etag))
        return Results.StatusCode(StatusCodes.Status304NotModified);

    context.Response.Headers.ETag = etag;
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(room);
});

app.MapPut("/v1/rooms/{roomId}/slots/{slotId}", async (string roomId, string slotId, HttpContext context, RoomStore store, RelayOptions relay) =>
{
    if (!Ids.IsValid(roomId) || !Ids.IsValid(slotId))
        return Results.BadRequest();

    if (context.Request.ContentLength > relay.MaxBlobBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false })
        limit.MaxRequestBodySize = relay.MaxBlobBytes;

    // Read at most one byte past the limit, whatever the client claims.
    var buffer = new byte[relay.MaxBlobBytes + 1];
    var read = 0;
    try
    {
        int n;
        while (read < buffer.Length && (n = await context.Request.Body.ReadAsync(buffer.AsMemory(read), context.RequestAborted)) > 0)
            read += n;
    }
    catch (BadHttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    if (read == 0)
        return Results.BadRequest();
    if (read > relay.MaxBlobBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    return store.Put(roomId, slotId, buffer[..read]) switch
    {
        PutResult.Stored => Results.NoContent(),
        PutResult.RoomFull => Results.Conflict(),
        _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
    };
});

app.Run();

/// <summary>Rooms and slots are 32 lowercase hex characters (128-bit hashes computed by the client).</summary>
internal static partial class Ids
{
    public static bool IsValid(string value) => HexId().IsMatch(value);

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex HexId();
}

internal sealed class PruneService(RoomStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            store.Prune();
    }
}

public partial class Program;
