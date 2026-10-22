using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>
/// The meta layer's whole footprint on a run, behind one call from
/// <see cref="Main"/>:
///
///     Meta.RunMeta.Attach(this);   // after BuildWorld
///
/// It applies the chosen character, the mouse-sensitivity setting, adds the
/// unlock tracker and toast, and places the hidden shrines. Everything else it
/// does, it does by listening to the bus — the run never calls back into it.
///
/// Keeping it to one entry point is what lets the run scene be owned by someone
/// else: the merge surface is three lines in Main (this call, the starting
/// weapon, the offer pool's content lists).
/// </summary>
public static class RunMeta
{
    /// <summary>Who this run is being played as. Resolved once per run, before
    /// the starting weapon is handed out, so both agree.</summary>
    public static CharacterDef Character { get; private set; } = Characters.Default;

    /// <summary>The starting weapon for this run. Called from Main.BuildWorld in
    /// place of <c>Catalog.Weapons[0]</c>; the default character returns exactly
    /// that, so unflagged and automated runs are unchanged.</summary>
    public static WeaponDef StartingWeapon()
    {
        Character = Characters.ForThisRun();
        return Character.StartingWeaponDef;
    }

    public static void Attach(Main main)
    {
        Profile.EnsureLoaded();
        GameSettings.Load();

        Character.ApplyTo(main.Player.Stats);
        SkinDef skin = Launch.Automated ? Character.Skins[0] : Character.SelectedSkin;
        main.Player.ApplySkin(skin.Tint, skin.LookId);

        // Mouse sensitivity is read live by PlayerCamera from GameSettings, so a
        // change made in the pause menu applies immediately; nothing to do here.

        var tracker = new UnlockTracker();
        tracker.Bind(main);
        main.AddChild(tracker);

        var layer = new CanvasLayer { Name = "MetaUI", Layer = 20 };
        main.AddChild(layer);
        layer.AddChild(new UI.UnlockToast());

        // Until maps call this themselves: once, on the run root. A separate Rng
        // derived from the seed keeps the run's own stream untouched.
        SecretSpawner.Populate(main, new Rng(main.Run.Seed ^ 0x5EC2E75UL), Arena.Radius);

        int earnedItems = MetaContent.Items.Count(i => Unlocks.IsContentUnlocked(i.Id));
        int earnedWeapons = MetaContent.Weapons.Count(w => Unlocks.IsContentUnlocked(w.Id));
        GD.Print($"META run character={Character.Id} look={Character.LookId} start={Character.StartingWeaponDef.Id} " +
                 $"maxhp={main.Player.Stats.Get(Stats.Stat.MaxHealth):0} speed={main.Player.Stats.Get(Stats.Stat.MoveSpeed):0.##} " +
                 $"earned_items={earnedItems}/{MetaContent.Items.Count} earned_weapons={earnedWeapons}/{MetaContent.Weapons.Count} " +
                 $"pool={MetaContent.OfferItems(Catalog.Items).Count}i+{MetaContent.OfferWeapons(Catalog.Weapons).Count}w " +
                 $"earned=[{string.Join(",", MetaContent.Items.Select(i => i.Id).Concat(MetaContent.Weapons.Select(w => w.Id)).Where(Unlocks.IsContentUnlocked))}] " +
                 $"unlocks={Profile.UnlockedIds.Count}/{Unlocks.All.Count} profile={Profile.Path ?? "(memory)"}");

        if (Launch.SecretTest) _ = SecretTest(main);
        if (Launch.OfferCheck) OfferCheck(main);
    }

    /// <summary>
    /// --offer-check: prove earned content is really in the level-up pool, not
    /// just listed. Rolls a pool built exactly as Main builds its own (but on a
    /// separate generator, so the run's stream is untouched), counts how often
    /// each earned id is offered, then takes the first earned item offered so
    /// its stat effect lands on the live player.
    /// </summary>
    private static void OfferCheck(Main main)
    {
        var pool = new Progression.OfferPool(MetaContent.OfferItems(Catalog.Items),
                                             MetaContent.OfferWeapons(Catalog.Weapons), new Rng(0xC0DEUL));
        // Building a pool hands its Rng to the drop table; give the run's back.
        Actors.PickupField.Ensure(main.Run.Rng);
        var seen = new Dictionary<string, int>();
        Progression.Offer? firstEarned = null;
        for (int i = 0; i < 400; i++)
            foreach (Progression.Offer o in pool.Roll(3, null, null, null))
            {
                string? id = o.Item?.Id ?? o.Weapon?.Id;
                if (id is null || MetaContent.ItemById(id) is null && MetaContent.WeaponById(id) is null) continue;
                seen[id] = seen.GetValueOrDefault(id) + 1;
                if (firstEarned is null && o.Item is not null) firstEarned = o;
            }
        GD.Print($"META offer-check rolls=400 earned_offered=[{string.Join(",", seen.Select(kv => $"{kv.Key}:{kv.Value}"))}]");

        if (firstEarned?.Item is { } item)
        {
            float before = main.Player.Stats.Get(Stats.Stat.MaxHealth);
            bool added = main.Inventory.Add(item);
            GD.Print($"META offer-check took {item.Id} added={added} stacks={main.Inventory.StacksOf(item)} " +
                     $"maxhp {before:0}->{main.Player.Stats.Get(Stats.Stat.MaxHealth):0} effects=\"{Progression.OfferPool.DescribeEffects(item)}\"");
        }
    }

    /// <summary>
    /// --secret-test: walk to each shrine and press the real interact key. The
    /// key goes through Input like a player's would, so the prompt range check,
    /// the input handler and the reward are all exercised, not just Activate().
    /// </summary>
    private static async Task SecretTest(Main main)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

        await Wait(2.0);
        foreach (Shrine shrine in SecretSpawner.Placed.ToList())
        {
            // A few tries: the autopilot keeps steering (and dashing) while this
            // runs, so the player can be carried out of range before the press.
            for (int attempt = 0; attempt < 6 && !shrine.Spent; attempt++)
            {
                if (!GodotObject.IsInstanceValid(main)) return;
                main.Player.GlobalPosition = shrine.GlobalPosition + new Vector3(1.2f, 0.5f, 0f);
                main.Player.Velocity = Vector3.Zero;
                await Wait(0.1);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
                await Wait(0.2);
            }
            GD.Print($"META secret-test {shrine.Kind.Id} spent={shrine.Spent}");
        }
    }
}
