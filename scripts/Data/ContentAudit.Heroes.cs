using System.Collections.Generic;
using System.Linq;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Data;

/// <summary>
/// The heroes trio in the audit (Meta/Characters.Heroes.cs, Data/Catalog.Heroes.cs,
/// Combat/Weapons/Signatures.cs):
///   * each hero starts with its own signature, and each signature has exactly
///     one owner, who is a hero; the signature resolves, has an icon of its
///     own, rolls at least three upgrade lines and has both milestones named;
///   * exclusivity: thousands of level-up rolls never offer a signature as a
///     new weapon, held or not, and the Toggler never lists one;
///   * upgrades: when this run's character holds its signature (run the audit
///     with --character=&lt;hero&gt;), its rolled upgrade cards do turn up, and the
///     card that reaches a milestone names it;
///   * looks, skins (six each, every extra one a real repaint), passive, gate quest.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditHeroes(WeaponSystem system)
    {
        Line($"heroes ids={string.Join(",", Characters.HeroIds)} signatures={string.Join(",", Catalog.SignatureIds)}");
        Check(Catalog.SignatureIds.Count == 3 && Characters.HeroIds.Length == 3, "heroes: want 3 heroes and 3 signatures");

        var owners = new Dictionary<string, string>();
        foreach (string id in Characters.HeroIds)
        {
            CharacterDef? c = Characters.ById(id);
            Check(c is not null, $"hero {id} missing from the roster");
            if (c is null) continue;
            string w = c.StartingWeapon;
            Check(Signatures.Is(w) && Catalog.SignatureIds.Contains(w), $"hero {id} starts with {w}, not a signature");
            Check(Signatures.Owner.TryGetValue(w, out string? owner) && owner == id, $"signature {w} is not owned by {id}");
            Check(owners.TryAdd(w, id), $"signature {w} started by both {owners.GetValueOrDefault(w)} and {id}");
            Check(c.Passive is PassiveKind.CalmLookout or PassiveKind.OldSalt or PassiveKind.SeaBreeze && c.PassiveText.Length > 0,
                  $"hero {id} has no hero passive");
            Check(c.LookId.StartsWith("pet-") && Visuals.PlayerLooks.Get(c.LookId).Id == "hero:" + c.LookId, $"hero {id} look {c.LookId} does not resolve");
            Check(c.Skins.Count >= 6, $"hero {id} has {c.Skins.Count} skins (want 6+)");
            foreach (SkinDef s in c.Skins.Skip(1))
                Check(!s.Paint.IsIdentity && Unlocks.SourceOf(s.Id) is not null, $"hero skin {s.Id} is not a repaint earned by a quest");
            Check(Unlocks.SourceOf(id) is { } gate && Unlocks.ById(gate)?.Reward == RewardKind.Character, $"hero {id} has no gate quest");
        }

        // No other character starts with a signature.
        foreach (CharacterDef c in Characters.All)
            if (!Characters.IsHero(c.Id))
                Check(!Signatures.Is(c.StartingWeapon), $"{c.Id} starts with signature {c.StartingWeapon}");

        foreach (string id in Catalog.SignatureIds)
        {
            WeaponDef? def = Catalog.WeaponById(id);
            Check(def is not null, $"signature {id} not in the catalogue");
            if (def is null) continue;
            Check(Signatures.Owner.ContainsKey(id), $"signature {id} has no owner");
            Check(WeaponUpgrades.EligibleLines(def) >= 3, $"signature {id} rolls fewer than 3 upgrade lines");
            Check(Icons.WeaponGlyph(def) is { } g && g != Icons.ShapeGlyph(def.Shape) && Icons.Exists(g), $"signature {id} has no icon of its own");
            for (int max = 0; max < 2; max++)
            {
                var (name, text) = Signatures.Milestone(id, max == 1);
                Check(name.Length > 0 && text.Length > 0, $"signature {id} milestone {(max == 1 ? "max" : "4")} unnamed");
            }
            Check(Signatures.MilestoneLine(def, Signatures.MilestoneLevel).Length > 0
                  && Signatures.MilestoneLine(def, def.MaxLevel).Length > 0
                  && Signatures.MilestoneLine(def, 2).Length == 0, $"signature {id} milestone card lines wrong");
            Check(!Toggler.Entries().Any(e => e.Id == id), $"signature {id} is listed in the Toggler");
        }

        OfferPool? runPool = OfferPool.Current;
        try { AuditSignatureOffers(system); }
        finally
        {
            OfferPool.Current = runPool;
            if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
        }
    }

    private static void AuditSignatureOffers(WeaponSystem system)
    {
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0x5161A7UL));
        string? held = Catalog.SignatureIds.FirstOrDefault(system.Has);
        int newSig = 0, heldUps = 0, ledgerSig = 0;
        const int rolls = 3000;
        for (int r = 0; r < rolls; r++)
        {
            foreach (Offer o in pool.Roll(3, system, null, null))
            {
                if (o.Weapon is not { } w || !Signatures.Is(w.Id)) continue;
                if (o.Upgrade is null || w.Id != held) newSig++;
                else heldUps++;
            }
            // The ledger path (no live system: a between-runs screen, a test).
            foreach (Offer o in pool.Roll(3, null, null, null))
                if (o.Weapon is { } w && Signatures.Is(w.Id)) ledgerSig++;
        }
        Check(newSig == 0, $"signature offered as a new weapon {newSig} times in {rolls} rolls");
        Check(ledgerSig == 0, $"signature offered by the ledger path {ledgerSig} times");
        if (held is not null)
        {
            Check(heldUps > 0, $"held signature {held} never offered an upgrade in {rolls} rolls");
            if (system.Get(held) is { } rt && rt.CanLevel)
            {
                Offer card = Offer.ForWeapon(rt.Def, Signatures.MilestoneLevel - 1, WeaponUpgrades.Reference(rt.Def));
                Check(card.Effect.Contains("★"), $"signature {held} milestone card does not name the milestone");
            }
        }
        Line($"heroes offers: new-signature={newSig} ledger={ledgerSig} held={held ?? "none"} held-upgrades={heldUps} rolls={rolls}" +
             (held is null ? " (run with --character=<hero> to check held upgrades)" : ""));
    }
}
