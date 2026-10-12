using System.Text.RegularExpressions;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Time;

namespace DutyHuddle.Domain.Serialization;

/// <summary>
/// The chat fallback: one message that people without the plugin can read and that clients with the
/// plugin decode from the trailing code. It is only ever pasted by the player, never sent automatically.
/// </summary>
public static partial class ShareMessage
{
    public const string CodePrefix = "DH1.";
    public const int MaxChatLength = 480;

    public static string Build(MemberSnapshot snapshot, DutyCatalog catalog, DateTime nowUtc)
    {
        var code = CodePrefix + SnapshotCodec.ToBase64Url(SnapshotCodec.Encode(snapshot));
        var epoch = GameClock.DailyEpoch(nowUtc);

        var left = catalog.Roulettes
            .Where(r => snapshot.GetRoulette(r.Id, epoch) == Availability.Available)
            .Select(r => r.ShortName)
            .ToList();
        var roulettePart = left.Count == 0 ? "no roulettes left" : "roulettes left: " + string.Join(", ", left);

        var board = snapshot.GetBoard(nowUtc);
        var wtPart = board is null ? "no WT" : $"WT {board.PlacedSeals}/9, {board.SecondChancePoints} pts";

        var text = $"[DutyHuddle] {roulettePart} | {wtPart} | {code}";
        if (text.Length <= MaxChatLength)
            return text;

        // Keep the code intact; shorten the readable part instead.
        var room = MaxChatLength - code.Length - wtPart.Length - 20;
        var shortRoulettes = roulettePart.Length > room ? roulettePart[..Math.Max(0, room)] + "..." : roulettePart;
        return $"[DutyHuddle] {shortRoulettes} | {wtPart} | {code}";
    }

    public static bool TryExtractCode(string message, out byte[] payload)
    {
        payload = [];
        var match = CodeRegex().Match(message);
        return match.Success && SnapshotCodec.TryFromBase64Url(match.Groups[1].Value, out payload);
    }

    [GeneratedRegex(@"DH1\.([A-Za-z0-9_\-]{12,})")]
    private static partial Regex CodeRegex();
}
