using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DutyHuddle.Sync;

public sealed record RelaySlot(string Slot, byte[] Blob, DateTimeOffset Updated);

public sealed record RelayRoom(IReadOnlyList<RelaySlot> Slots, string? ETag);

public interface ISyncTransport
{
    Task PutAsync(string roomId, string slotId, byte[] blob, CancellationToken cancellationToken);

    /// <summary>Returns null when the room has not changed since <paramref name="etag"/> (HTTP 304).</summary>
    Task<RelayRoom?> GetAsync(string roomId, string? etag, CancellationToken cancellationToken);
}

/// <summary>HTTP client for the DutyHuddle relay. The relay only ever receives hashes and ciphertext.</summary>
public sealed class RelayTransport : ISyncTransport, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient http;

    public RelayTransport(Uri baseAddress, string userAgent)
    {
        http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public Uri BaseAddress => http.BaseAddress!;

    public async Task PutAsync(string roomId, string slotId, byte[] blob, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(blob);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.PutAsync($"v1/rooms/{roomId}/slots/{slotId}", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RelayRoom?> GetAsync(string roomId, string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/rooms/{roomId}");
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag))
            request.Headers.IfNoneMatch.Add(tag);

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return null;
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new RelayRoom([], null);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<RoomDto>(Json, cancellationToken).ConfigureAwait(false);
        var slots = payload?.Slots?
            .Select(s => new RelaySlot(s.Slot, Convert.FromBase64String(s.Blob), DateTimeOffset.FromUnixTimeSeconds(s.UpdatedUnix)))
            .ToList() ?? [];
        return new RelayRoom(slots, response.Headers.ETag?.ToString());
    }

    public void Dispose() => http.Dispose();

    private sealed record RoomDto(long Version, List<SlotDto>? Slots);

    private sealed record SlotDto(string Slot, string Blob, long UpdatedUnix);
}
