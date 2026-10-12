using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Security;
using DutyHuddle.Domain.Serialization;
using DutyHuddle.Domain.Time;

namespace DutyHuddle.Domain.Tests;

public class SerializationTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Codec_round_trips_a_full_snapshot_in_under_70_bytes()
    {
        var snapshot = Sample();

        var bytes = SnapshotCodec.Encode(snapshot);
        Assert.True(SnapshotCodec.TryDecode(bytes, snapshot.Member, SnapshotSource.Relay, out var decoded));

        Assert.True(bytes.Length < 70);
        Assert.NotNull(decoded);
        Assert.Equal(snapshot.CapturedUtc, decoded!.CapturedUtc);
        Assert.Equal(snapshot.DailyEpoch, decoded.DailyEpoch);
        Assert.Equal(snapshot.Roulettes, decoded.Roulettes);
        Assert.Equal(snapshot.WondrousTails!.OrderIds, decoded.WondrousTails!.OrderIds);
        Assert.Equal(snapshot.WondrousTails.Status, decoded.WondrousTails.Status);
        Assert.Equal(snapshot.WondrousTails.StickerMask, decoded.WondrousTails.StickerMask);
        Assert.Equal(snapshot.WondrousTails.SecondChancePoints, decoded.WondrousTails.SecondChancePoints);
        Assert.Equal(snapshot.WondrousTails.ExpiresUtc, decoded.WondrousTails.ExpiresUtc);
        Assert.Equal(SnapshotSource.Relay, decoded.Source);
    }

    [Fact]
    public void Codec_rejects_garbage_and_truncated_input()
    {
        var bytes = SnapshotCodec.Encode(Sample());

        Assert.False(SnapshotCodec.TryDecode([], default, SnapshotSource.Relay, out _));
        Assert.False(SnapshotCodec.TryDecode(bytes.AsSpan(0, bytes.Length - 3), default, SnapshotSource.Relay, out _));
        Assert.False(SnapshotCodec.TryDecode([9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], default, SnapshotSource.Relay, out _));
    }

    [Fact]
    public void Share_message_is_readable_and_decodable()
    {
        var catalog = TestCatalog.Build();
        var snapshot = Sample();

        var message = ShareMessage.Build(snapshot, catalog, Now);

        Assert.StartsWith("[DutyHuddle] roulettes left: ", message);
        Assert.Contains("Trials", message);
        Assert.Contains("WT 3/9, 4 pts", message);
        Assert.True(message.Length <= ShareMessage.MaxChatLength);
        Assert.True(ShareMessage.TryExtractCode("Kaia: " + message, out var payload));
        Assert.True(SnapshotCodec.TryDecode(payload, snapshot.Member, SnapshotSource.ChatCode, out var decoded));
        Assert.Equal(snapshot.Roulettes, decoded!.Roulettes);
    }

    [Fact]
    public void Room_key_does_not_depend_on_member_order()
    {
        var a = RoomCrypto.DeriveFromContentIds([3, 1, 2]);
        var b = RoomCrypto.DeriveFromContentIds([2, 3, 1, 0]);
        var other = RoomCrypto.DeriveFromContentIds([1, 2]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        Assert.Equal(32, RoomCrypto.RoomId(a).Length);
        Assert.Equal(RoomCrypto.RoomId(a), RoomCrypto.RoomId(b));
    }

    [Fact]
    public void Sealed_blob_opens_only_with_the_right_key_and_slot()
    {
        var key = RoomCrypto.DeriveFromMembers([new MemberId("Tu", 1), new MemberId("Kaia", 2)]);
        var slot = RoomCrypto.SlotId(key, new MemberId("Tu", 1));
        var otherSlot = RoomCrypto.SlotId(key, new MemberId("Kaia", 2));
        var payload = SnapshotCodec.Encode(Sample());

        var blob = RoomCrypto.Seal(key, slot, payload);

        Assert.True(RoomCrypto.TryOpen(key, slot, blob, out var opened));
        Assert.Equal(payload, opened);
        Assert.False(RoomCrypto.TryOpen(key, otherSlot, blob, out _));
        Assert.False(RoomCrypto.TryOpen(RoomCrypto.DeriveFromContentIds([42]), slot, blob, out _));

        blob[^1] ^= 1;
        Assert.False(RoomCrypto.TryOpen(key, slot, blob, out _));
    }

    [Theory]
    [InlineData("2026-10-12T14:59:59Z", "2026-10-11")]
    [InlineData("2026-10-12T15:00:00Z", "2026-10-12")]
    [InlineData("2026-10-13T03:00:00Z", "2026-10-12")]
    public void Daily_epoch_flips_at_15_utc(string utc, string expected)
    {
        var time = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(DateOnly.Parse(expected), GameClock.DailyEpoch(time));
    }

    [Theory]
    [InlineData("2026-10-13T07:59:00Z", "2026-10-06")]
    [InlineData("2026-10-13T08:00:00Z", "2026-10-13")]
    [InlineData("2026-10-18T12:00:00Z", "2026-10-13")]
    public void Weekly_epoch_flips_on_tuesday_at_8_utc(string utc, string expected)
    {
        var time = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(DateOnly.Parse(expected), GameClock.WeeklyEpoch(time));
    }

    private static MemberSnapshot Sample()
    {
        var status = new WtTaskStatus[16];
        status[0] = WtTaskStatus.Claimed;
        status[3] = WtTaskStatus.Claimable;
        status[15] = WtTaskStatus.Claimed;
        return new MemberSnapshot(
            new MemberId("Tu", 1),
            Now,
            GameClock.DailyEpoch(Now),
            new Dictionary<uint, Availability>
            {
                [1] = Availability.Completed,
                [2] = Availability.Available,
                [6] = Availability.Available,
                [9] = Availability.Locked,
                [15] = Availability.Available,
            },
            new WtBoard
            {
                OrderIds = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray(),
                Status = status,
                StickerMask = 0b1000_0000_0010_0001,
                SecondChancePoints = 4,
                ExpiresUtc = Now.AddDays(9),
            },
            SnapshotSource.Local);
    }
}
