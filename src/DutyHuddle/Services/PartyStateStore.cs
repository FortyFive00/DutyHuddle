using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Models;
using DutyHuddle.GameAdapters;

namespace DutyHuddle.Services;

/// <summary>
/// The single source of truth for the UI: current party members, the local snapshot and the snapshots
/// received from other members. Thread-safe; <see cref="Version"/> changes on every update.
/// </summary>
public sealed class PartyStateStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, MemberSnapshot> remote = new(StringComparer.Ordinal);
    private IReadOnlyList<PartyMemberInfo> members = [];
    private MemberSnapshot? local;
    private long version;

    public event Action? Changed;

    public long Version => Interlocked.Read(ref version);

    public IReadOnlyList<PartyMemberInfo> Members
    {
        get
        {
            lock (gate)
                return members;
        }
    }

    public MemberSnapshot? Local
    {
        get
        {
            lock (gate)
                return local;
        }
    }

    public void SetMembers(IReadOnlyList<PartyMemberInfo> value)
    {
        lock (gate)
        {
            if (members.SequenceEqual(value))
                return;
            members = value;
        }

        Bump();
    }

    public void SetLocal(MemberSnapshot snapshot)
    {
        lock (gate)
            local = snapshot;
        Bump();
    }

    /// <summary>Stores a snapshot received from another member, unless an equal or newer one is known.</summary>
    public bool UpdateRemote(MemberSnapshot snapshot)
    {
        lock (gate)
        {
            if (remote.TryGetValue(snapshot.Member.Key, out var existing) && existing.CapturedUtc >= snapshot.CapturedUtc)
                return false;
            remote[snapshot.Member.Key] = snapshot;
        }

        Bump();
        return true;
    }

    public IReadOnlyList<PartyMemberState> GetStates()
    {
        lock (gate)
        {
            if (members.Count == 0)
                return local is null ? [] : [new PartyMemberState(local.Member, true, local)];

            return members
                .Select(m => m.IsLocal
                    ? new PartyMemberState(m.Id, true, local, m.ClassJobId)
                    : new PartyMemberState(m.Id, false, remote.GetValueOrDefault(m.Id.Key), m.ClassJobId))
                .ToList();
        }
    }

    public void ClearRemote()
    {
        lock (gate)
            remote.Clear();
        Bump();
    }

    private void Bump()
    {
        Interlocked.Increment(ref version);
        Changed?.Invoke();
    }
}
