using System.Buffers.Binary;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.Domain.Serialization;

/// <summary>
/// Compact binary form of a <see cref="MemberSnapshot"/> (about 60 bytes). It never contains a name:
/// the relay slot or the chat sender identifies the member.
/// </summary>
/// <remarks>
/// Layout v1, little endian:
/// version(1) flags(1) captured unix seconds(4) daily epoch day number(4) roulette count(1)
/// [roulette id(1) availability(1)]* and, when flag bit 0 is set:
/// 16 order IDs, 4 bytes of 2-bit statuses, sticker mask(2), points(1), expiry unix seconds(4).
/// </remarks>
public static class SnapshotCodec
{
    public const byte Version = 1;
    private const byte FlagHasWondrousTails = 1;

    public static byte[] Encode(MemberSnapshot snapshot)
    {
        var roulettes = snapshot.Roulettes.Where(kv => kv.Key <= byte.MaxValue).OrderBy(kv => kv.Key).ToList();
        var board = snapshot.WondrousTails;
        var size = 11 + roulettes.Count * 2 + (board is null ? 0 : 27);
        var buffer = new byte[size];
        var span = buffer.AsSpan();

        span[0] = Version;
        span[1] = board is null ? (byte)0 : FlagHasWondrousTails;
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], ToUnix(snapshot.CapturedUtc));
        BinaryPrimitives.WriteInt32LittleEndian(span[6..], snapshot.DailyEpoch.DayNumber);
        span[10] = (byte)roulettes.Count;

        var offset = 11;
        foreach (var (id, state) in roulettes)
        {
            span[offset++] = (byte)id;
            span[offset++] = (byte)state;
        }

        if (board is not null)
        {
            for (var i = 0; i < WtBoard.CellCount; i++)
                span[offset++] = board.OrderIds[i];

            for (var i = 0; i < WtBoard.CellCount; i++)
                span[offset + i / 4] |= (byte)(((byte)board.Status[i] & 0b11) << (i % 4 * 2));
            offset += 4;

            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], board.StickerMask);
            offset += 2;
            span[offset++] = board.SecondChancePoints;
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], ToUnix(board.ExpiresUtc));
        }

        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, MemberId member, SnapshotSource source, out MemberSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            if (data.Length < 11 || data[0] != Version)
                return false;

            var hasBoard = (data[1] & FlagHasWondrousTails) != 0;
            var captured = FromUnix(BinaryPrimitives.ReadInt32LittleEndian(data[2..]));
            var epoch = DateOnly.FromDayNumber(BinaryPrimitives.ReadInt32LittleEndian(data[6..]));
            int count = data[10];
            var expected = 11 + count * 2 + (hasBoard ? 27 : 0);
            if (data.Length < expected)
                return false;

            var roulettes = new Dictionary<uint, Availability>(count);
            var offset = 11;
            for (var i = 0; i < count; i++)
            {
                var id = data[offset++];
                var state = data[offset++];
                roulettes[id] = state <= (byte)Availability.Locked ? (Availability)state : Availability.Unknown;
            }

            WtBoard? board = null;
            if (hasBoard)
            {
                var orders = data.Slice(offset, WtBoard.CellCount).ToArray();
                offset += WtBoard.CellCount;
                var status = new WtTaskStatus[WtBoard.CellCount];
                for (var i = 0; i < WtBoard.CellCount; i++)
                {
                    var raw = (data[offset + i / 4] >> (i % 4 * 2)) & 0b11;
                    status[i] = raw <= (int)WtTaskStatus.Claimed ? (WtTaskStatus)raw : WtTaskStatus.Open;
                }

                offset += 4;
                var stickers = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                offset += 2;
                var points = data[offset++];
                var expires = FromUnix(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));
                board = new WtBoard
                {
                    OrderIds = orders,
                    Status = status,
                    StickerMask = stickers,
                    SecondChancePoints = Math.Min(points, (byte)9),
                    ExpiresUtc = expires,
                };
            }

            snapshot = new MemberSnapshot(member, captured, epoch, roulettes, board, source);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static string ToBase64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryFromBase64Url(string text, out byte[] data)
    {
        data = [];
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        try
        {
            data = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int ToUnix(DateTime utc)
        => (int)Math.Clamp(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds(), 0, int.MaxValue);

    private static DateTime FromUnix(int seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
