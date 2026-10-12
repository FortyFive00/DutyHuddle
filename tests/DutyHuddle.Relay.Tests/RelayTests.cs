using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DutyHuddle.Relay;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DutyHuddle.Relay.Tests;

public class RoomStoreTests
{
    private const string Room = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Slots_expire_after_the_time_to_live()
    {
        var clock = new ManualClock();
        var store = new RoomStore(new RelayOptions { SlotTimeToLive = TimeSpan.FromHours(1) }, clock);

        store.Put(Room, "a", [1]);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(store.Get(Room));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(store.Get(Room));
        Assert.Equal(1, store.Prune());
        Assert.Equal(0, store.RoomCount);
    }

    [Fact]
    public void Room_refuses_more_slots_than_the_limit_but_allows_updates()
    {
        var store = new RoomStore(new RelayOptions { MaxSlotsPerRoom = 2 }, new ManualClock());

        Assert.Equal(PutResult.Stored, store.Put(Room, "a", [1]));
        Assert.Equal(PutResult.Stored, store.Put(Room, "b", [2]));
        Assert.Equal(PutResult.RoomFull, store.Put(Room, "c", [3]));
        Assert.Equal(PutResult.Stored, store.Put(Room, "a", [4]));
    }

    [Fact]
    public void Version_changes_on_every_write()
    {
        var store = new RoomStore(new RelayOptions(), new ManualClock());

        store.Put(Room, "a", [1]);
        var first = store.Get(Room)!.Version;
        store.Put(Room, "a", [2]);

        Assert.NotEqual(first, store.Get(Room)!.Version);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}

public class RelayHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Room = "00112233445566778899aabbccddeeff";
    private const string Slot = "ffeeddccbbaa99887766554433221100";
    private readonly WebApplicationFactory<Program> factory;

    public RelayHttpTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Put_then_get_returns_the_blob_and_supports_etag()
    {
        var client = factory.CreateClient();
        var blob = new byte[] { 1, 2, 3, 4, 5 };

        var put = await client.PutAsync($"/v1/rooms/{Room}/slots/{Slot}", new ByteArrayContent(blob));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await client.GetAsync($"/v1/rooms/{Room}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var room = await get.Content.ReadFromJsonAsync<RoomView>();
        Assert.NotNull(room);
        var slot = Assert.Single(room!.Slots);
        Assert.Equal(Slot, slot.Slot);
        Assert.Equal(blob, Convert.FromBase64String(slot.Blob));

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/v1/rooms/{Room}");
        conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(get.Headers.ETag!.ToString()));
        var notModified = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        // A proxy that compresses the response may hand the client a weak tag; it must still match.
        var weak = new HttpRequestMessage(HttpMethod.Get, $"/v1/rooms/{Room}");
        weak.Headers.TryAddWithoutValidation("If-None-Match", "W/" + get.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(weak)).StatusCode);
    }

    [Fact]
    public async Task Invalid_ids_and_oversized_blobs_are_rejected()
    {
        var client = factory.CreateClient();

        var badId = await client.PutAsync($"/v1/rooms/not-a-room/slots/{Slot}", new ByteArrayContent([1]));
        var tooBig = await client.PutAsync($"/v1/rooms/{Room}/slots/{Slot}", new ByteArrayContent(new byte[513]));
        var empty = await client.PutAsync($"/v1/rooms/{Room}/slots/{Slot}", new ByteArrayContent([]));
        var missing = await client.GetAsync("/v1/rooms/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
