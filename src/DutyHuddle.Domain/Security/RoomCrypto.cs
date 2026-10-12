using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.Domain.Security;

/// <summary>
/// Key material for a party "room" on the relay. Every member derives the same key on their own
/// from data only party members can see, so the relay stores ciphertext it cannot read and never
/// learns names, worlds or IDs.
/// </summary>
public static class RoomCrypto
{
    public const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Salt = "DutyHuddle/v1/room"u8.ToArray();

    /// <summary>
    /// Preferred derivation: the content IDs of the current members, sorted. They stay in memory on the
    /// client and are neither stored nor transmitted; only a hash of the derived key leaves the machine.
    /// </summary>
    public static byte[] DeriveFromContentIds(IEnumerable<ulong> contentIds)
    {
        var sorted = contentIds.Where(id => id != 0).Distinct().Order().ToArray();
        if (sorted.Length == 0)
            throw new ArgumentException("At least one content ID is required.", nameof(contentIds));

        var ikm = new byte[sorted.Length * 8];
        for (var i = 0; i < sorted.Length; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(ikm.AsSpan(i * 8), sorted[i]);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, KeySize, Salt, "content-id-room"u8.ToArray());
    }

    /// <summary>
    /// Fallback derivation from Name@World. Weaker: anyone who knows the party composition can derive it.
    /// </summary>
    public static byte[] DeriveFromMembers(IEnumerable<MemberId> members)
    {
        var joined = string.Join('\n', members.Select(m => m.Key).Distinct().Order(StringComparer.Ordinal));
        if (joined.Length == 0)
            throw new ArgumentException("At least one member is required.", nameof(members));

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(joined), KeySize, Salt, "member-room"u8.ToArray());
    }

    /// <summary>Public room identifier: 32 hex characters of a hash of the key.</summary>
    public static string RoomId(byte[] key)
    {
        var hash = SHA256.HashData([.. key, .. "room-id"u8]);
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>Per-member slot inside the room. Only room members can map a slot back to a name.</summary>
    public static string SlotId(byte[] key, MemberId member)
    {
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("slot:" + member.Key));
        return Convert.ToHexStringLower(mac.AsSpan(0, 16));
    }

    /// <summary>AES-GCM with the slot ID as associated data. Output: nonce | ciphertext | tag.</summary>
    public static byte[] Seal(byte[] key, string slotId, ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize, plaintext.Length), output.AsSpan(NonceSize + plaintext.Length), Encoding.ASCII.GetBytes(slotId));
        return output;
    }

    public static bool TryOpen(byte[] key, string slotId, ReadOnlySpan<byte> blob, out byte[] plaintext)
    {
        plaintext = [];
        if (blob.Length < NonceSize + TagSize)
            return false;

        var length = blob.Length - NonceSize - TagSize;
        var result = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob[..NonceSize], blob.Slice(NonceSize, length), blob[(NonceSize + length)..], result, Encoding.ASCII.GetBytes(slotId));
            plaintext = result;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
