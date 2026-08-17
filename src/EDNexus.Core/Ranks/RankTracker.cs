using EDNexus.Core.Journal;

namespace EDNexus.Core.Ranks;

/// <summary>
/// Feature service tracking the commander's pilot ranks — where each stands and how far it is to the
/// next tier. Fed by the journal's <c>Rank</c>, <c>Progress</c> and <c>Promotion</c> events, each of
/// which carries every rank in a single payload, so one service covers all five ladders. It owns its
/// own derived state and never mutates <see cref="State.CommanderState"/>.
/// </summary>
/// <remarks>
/// The three events do different jobs and none is sufficient alone. <c>Rank</c> is a full snapshot of
/// the indices, written at startup. <c>Progress</c> is a full snapshot of the percentages, written
/// alongside it. <c>Promotion</c> carries only the rank that just moved, and the game does not always
/// follow it with a fresh <c>Progress</c> — so a promotion resets that rank's percentage here rather
/// than waiting for a number that may not arrive.
/// </remarks>
public sealed class RankTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<RankKind, int> _index = new();
    private readonly Dictionary<RankKind, int> _percent = new();

    /// <summary>Raised after any rank event changes the tracked picture.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on a live promotion, never during the start-up replay — a warm-up must not fire
    /// callouts for promotions the commander earned days ago.
    /// </summary>
    public event Action<RankProgress>? Promoted;

    public RankTracker(JournalEventBus bus)
    {
        bus.Subscribe("Rank", OnRank);
        bus.Subscribe("Progress", OnProgress);
        bus.Subscribe("Promotion", OnPromotion);
    }

    /// <summary>True once the journal has told us about at least one rank.</summary>
    public bool HasData
    {
        get { lock (_gate) return _index.Count > 0; }
    }

    /// <summary>Every tracked rank, in ladder order. Ranks not yet seen are reported at index 0.</summary>
    public IReadOnlyList<RankProgress> All
    {
        get
        {
            lock (_gate) return RankLadders.All.Select(SnapshotLocked).ToList();
        }
    }

    /// <summary>The standing of one rank.</summary>
    public RankProgress this[RankKind kind]
    {
        get { lock (_gate) return SnapshotLocked(RankLadders.For(kind)); }
    }

    // --- event handlers -------------------------------------------------------------------

    // Rank and Progress are both whole-picture snapshots, so each simply overwrites what it owns.
    private void OnRank(JournalEntry e) => ApplySnapshot(e, _index, clampToLadder: true);

    private void OnProgress(JournalEntry e) => ApplySnapshot(e, _percent, clampToLadder: false);

    private void ApplySnapshot(JournalEntry e, Dictionary<RankKind, int> target, bool clampToLadder)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var ladder in RankLadders.All)
            {
                // Defensive: a rank the payload omits keeps whatever we already had. Frontier has
                // added ranks over time (Exobiologist and Soldier arrived with Odyssey) and an older
                // journal simply will not mention them.
                if (e.GetInt64(ladder.JournalField) is not { } raw) continue;

                var value = clampToLadder
                    ? (int)Math.Max(0, raw)
                    : (int)Math.Clamp(raw, 0, 100);

                if (target.TryGetValue(ladder.Kind, out var existing) && existing == value) continue;
                target[ladder.Kind] = value;
                changed = true;
            }
        }

        if (changed) Changed?.Invoke();
    }

    private void OnPromotion(JournalEntry e)
    {
        var promotions = new List<RankProgress>();
        lock (_gate)
        {
            foreach (var ladder in RankLadders.All)
            {
                if (e.GetInt64(ladder.JournalField) is not { } raw) continue;

                var value = (int)Math.Max(0, raw);
                _index[ladder.Kind] = value;
                // A promotion means the bar starts over; the game does not reliably re-send Progress.
                _percent[ladder.Kind] = 0;
                promotions.Add(SnapshotLocked(ladder));
            }
        }

        if (promotions.Count == 0) return;
        Changed?.Invoke();

        // Silent during the start-up replay: warming state must not fire alerts for old news.
        if (e.IsHistorical) return;
        foreach (var promotion in promotions) Promoted?.Invoke(promotion);
    }

    // --- helpers --------------------------------------------------------------------------

    private RankProgress SnapshotLocked(RankLadder ladder)
    {
        var index = _index.TryGetValue(ladder.Kind, out var i) ? i : 0;
        var percent = _percent.TryGetValue(ladder.Kind, out var p) ? p : 0;
        var maxed = index >= ladder.MaxIndex;

        return new RankProgress(ladder.Kind, ladder.Label, index, ladder.NameFor(index), percent)
        {
            IsMaxed = maxed,
        };
    }
}
