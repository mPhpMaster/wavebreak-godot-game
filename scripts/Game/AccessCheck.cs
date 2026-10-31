using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-access=&lt;dir&gt;</c>: switches the accessibility settings in memory
/// and checks what they reach, printing ACCESS lines and ACCESS PASS/FAIL, with a
/// capture of a striped telegraph in each palette.
/// </summary>
public static class AccessCheck
{
    public static string? Requested()
        => System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--check-access="))?["--check-access=".Length..];

    public static async Task Run(Main main, string dir)
    {
        SceneTree tree = main.GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"ACCESS {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        Screenshot.Invulnerable = true;
        await Wait(3.0);

        var rarities = System.Enum.GetValues<Rarity>();
        Color rare = Catalog.RarityColor(Rarity.Rare), slot = Coop.SlotColor(1), danger = EnemyAttacks.DangerColor, wave = EnemyAttacks.WaveColor;
        Expect("off: the shipped rarity blue", rare.IsEqualApprox(new Color(0.31f, 0.66f, 1.00f)));
        Expect("off: the shipped slot orange", slot.IsEqualApprox(new Color(1.00f, 0.62f, 0.25f)));

        Vector3 feet = main.GetTree().Root.GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        if (Actors.Player.Instance is { } player) feet = player.GlobalPosition + player.AimDirection * 4f;
        foreach (ColorVision vision in new[] { ColorVision.Deuteranopia, ColorVision.Protanopia, ColorVision.Tritanopia })
        {
            Accessibility.SetVision(vision);
            Expect($"{vision}: rarities change", !Catalog.RarityColor(Rarity.Rare).IsEqualApprox(rare));
            Expect($"{vision}: all five stay distinct", rarities.Select(r => Catalog.RarityColor(r).ToHtml()).Distinct().Count() == rarities.Length);
            Expect($"{vision}: team slots change", !Coop.SlotColor(1).IsEqualApprox(slot));
            Expect($"{vision}: four slots stay distinct", Enumerable.Range(0, 4).Select(i => Coop.SlotColor(i).ToHtml()).Distinct().Count() == 4);
            Expect($"{vision}: telegraphs change", !EnemyAttacks.DangerColor.IsEqualApprox(danger) && !EnemyAttacks.WaveColor.IsEqualApprox(wave));
            Expect($"{vision}: blast and wave stay apart", !EnemyAttacks.DangerColor.IsEqualApprox(EnemyAttacks.WaveColor));

            GroundDecal? decal = EnemyAttacks.AcquireDecal();
            Expect($"{vision}: a decal is available", decal is not null);
            if (decal is not null)
            {
                // Any enemy's own tint folds into the palette's colour.
                decal.SetDisc(feet, 3.5f, 0.6f, new Color(0.1f, 0.9f, 0.3f, 0.8f));
                decal.Visible = true;
                Expect($"{vision}: a decal carries stripes", decal.GetInstanceShaderParameter("stripes").AsSingle() > 0.5f);
                Expect($"{vision}: a decal takes the palette", decal.GetInstanceShaderParameter("tint").AsColor().IsEqualApprox(EnemyAttacks.DangerColor with { A = 0.8f }));
                await Wait(0.3);
                await Screenshot.Capture(tree, main, System.IO.Path.Combine(dir, $"telegraph-{vision}.png"));
                EnemyAttacks.ReleaseDecal(decal);
            }
        }
        Accessibility.SetVision(ColorVision.Normal);
        Expect("off again: shipped colours are back", Catalog.RarityColor(Rarity.Rare).IsEqualApprox(rare) && EnemyAttacks.DangerColor.IsEqualApprox(danger));

        Expect("flashes off: a flash is whole", Mathf.IsEqualApprox(Accessibility.Flash(1f), 1f));
        Accessibility.SetReduceFlashes(true);
        Expect("reduced: a flash is a fifth", Mathf.IsEqualApprox(Accessibility.Flash(1f), 0.2f));
        var glint = new SignatureFx.Flash(feet, 0.5f, Colors.White, 0.2f);
        Expect("reduced: weapon glints are dropped", !SignatureFx.Add(glint));
        Accessibility.SetReduceFlashes(false);

        Screenshot.Invulnerable = false;
        GD.Print(ok ? "ACCESS PASS" : "ACCESS FAIL");
        tree.Quit();
    }
}
