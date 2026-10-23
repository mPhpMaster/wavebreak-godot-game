using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// One cosmetic hat. Owned for free, by a quest (<see cref="ExpansionQuests"/>
/// names it as a <see cref="RewardKind.Hat"/> reward), or bought with Pearls.
/// The mesh is built by <c>Visuals.HatModels</c> from <see cref="Id"/>.
/// </summary>
public sealed class HatDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Pearl price; 0 for free or quest-only hats.</summary>
    public int PearlCost { get; init; }

    /// <summary>The quest that grants it, or null.</summary>
    public string? UnlockedBy => Unlocks.SourceOf(Id);

    public bool IsOwned
    {
        get
        {
            if (Id == Hats.NoneId) return true;
            if (UnlockedBy is { } q) return Profile.IsUnlocked(q);
            if (PearlCost <= 0) return true;
            return Profile.GetMeta(Hats.BoughtKey(Id)) > 0;
        }
    }

    /// <summary>"150 Pearls", the quest condition, or "".</summary>
    public string HowToGet
    {
        get
        {
            if (IsOwned) return "";
            if (Unlocks.ById(UnlockedBy) is { } u) return u.Description;
            return $"{PearlCost} {Pearls.Name}";
        }
    }
}

/// <summary>
/// The hat wardrobe. A hat is chosen per character (stored next to the skin
/// choice, key <c>hat.&lt;characterId&gt;</c>), so the profile file needs no new
/// section; buying one with Pearls sets <c>hat.&lt;id&gt;</c> in the meta table.
/// </summary>
public static partial class Hats
{
    public const string NoneId = "none";

    public static readonly IReadOnlyList<HatDef> All = WithCute(new List<HatDef>   // + Meta/Hats.Cute.cs
    {
        new() { Id = NoneId, Name = "No hat" },
        new() { Id = "sailor_cap", Name = "Sailor's Cap" },
        new() { Id = "tricorne", Name = "Tricorne", PearlCost = 120 },
        new() { Id = "souwester", Name = "Sou'wester", PearlCost = 150 },
        new() { Id = "kelp_wreath", Name = "Kelp Wreath", PearlCost = 200 },
        new() { Id = "starfish", Name = "Starfish", PearlCost = 250 },
        new() { Id = "shell_bonnet", Name = "Shell Bonnet", PearlCost = 300 },
        new() { Id = "pearl_tiara", Name = "Pearl Tiara", PearlCost = 600 },
        new() { Id = "captains_cap", Name = "Captain's Cap" },
        new() { Id = "lantern_hat", Name = "Lantern Hat" },
        new() { Id = "anchor_crest", Name = "Anchor Crest" },
        new() { Id = "coral_crown", Name = "Coral Crown" },
        new() { Id = "diving_helmet", Name = "Diving Helmet" },
        new() { Id = "gull_perch", Name = "Gull Perch" },
        new() { Id = "jellyfish_cap", Name = "Jellyfish Cap" },
        new() { Id = "storm_cloud", Name = "Storm Cloud" },
    });

    public static HatDef? ById(string? id)
    {
        foreach (HatDef h in All) if (h.Id == id) return h;
        return null;
    }

    public static string BoughtKey(string hatId) => "hat." + hatId;
    private static string ChoiceKey(string characterId) => "hat." + characterId;

    /// <summary>The hat this character wears: the saved choice if still owned,
    /// else none.</summary>
    public static HatDef SelectedFor(string characterId)
    {
        Profile.EnsureLoaded();
        HatDef? h = ById(Profile.SelectedSkin(ChoiceKey(characterId)));
        return h is { IsOwned: true } ? h : All[0];
    }

    /// <summary>Remember the choice. Does not save.</summary>
    public static void Select(string characterId, HatDef hat)
    {
        if (hat.IsOwned) Profile.SelectSkin(ChoiceKey(characterId), hat.Id);
    }

    /// <summary>Spend Pearls on a hat. Saves on success.</summary>
    public static bool TryBuy(HatDef hat)
    {
        if (hat.IsOwned || hat.PearlCost <= 0 || hat.UnlockedBy is not null) return false;
        if (!Pearls.TrySpend(hat.PearlCost)) return false;
        Profile.SetMeta(BoughtKey(hat.Id), 1);
        Profile.Save();
        GD.Print($"META hat bought {hat.Id} for {hat.PearlCost}");
        return true;
    }
}
