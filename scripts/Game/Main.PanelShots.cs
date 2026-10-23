using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// <c>--panel-shot=&lt;prefix&gt;</c>: photographs the in-run panels that no other
/// capture reaches, each opened the way a player opens it, then quits:
///
///   &lt;prefix&gt;-merchant-prompt / -merchant     a merchant placed in front, E pressed
///   &lt;prefix&gt;-microwave-prompt / -microwave   the same for a microwave
///   &lt;prefix&gt;-statue-prompt / -greed-prompt   the map shrines' prompts
///   &lt;prefix&gt;-shrine                         a charge shrine's boon panel
///   &lt;prefix&gt;-results                        the results screen after dying
///
/// Combine with <c>--lang=ar</c> to check the Arabic layout. The player is
/// handed 2000 gold and the first fifteen items first.
/// </summary>
public sealed partial class Main
{
    private async Task PanelShots(string prefix)
    {
        SceneTree tree = GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async Task Shot(string name) => await Screenshot.Capture(tree, this, $"{prefix}-{name}.png");
        void Key(Key k)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        }
        Vector3 Ahead(float metres)
        {
            Vector3 fwd = PlayerCamera.Instance is { } cam ? cam.FlatForward : Vector3.Forward;
            return _player.GlobalPosition + fwd.Normalized() * metres;
        }
        void CloseModals()
        {
            foreach (Node n in FindChildren("*", "", true, false))
                if (n is UI.ModalPanel { IsOpen: true } m) m.Close();
        }

        Screenshot.Invulnerable = true;
        await Wait(4.5);   // past the map's title card
        _inventory.DevSetGold(2000);
        // Something for the microwave to cook and the results to list.
        foreach (ItemDef item in Catalog.Items.Take(15)) _inventory.Add(item);
        for (int i = 0; i < 40 && _levelUp.IsOpen; i++) { Key(Godot.Key.Key1); await Wait(0.3); }

        foreach ((string name, Station station) in new (string, Station)[] { ("merchant", new Merchant()), ("microwave", new Microwave()) })
        {
            if (Interactables.Instance?.SpawnStationAt(station, Ahead(2.0f)) is not { } placed) continue;
            await Wait(0.6);
            await Shot($"{name}-prompt");
            Key(Godot.Key.E);
            await Wait(0.8);
            await Shot(name);
            CloseModals();
            await Wait(0.3);
            placed.QueueFree();
            await Wait(0.3);
        }

        if (ShrineSpawner.Instance is { } shrines && _stages.DevArena is { } arena)
        {
            foreach (MapShrineKind kind in new[] { MapShrineKind.Statue, MapShrineKind.Greed })
            {
                MapShrine s = shrines.SpawnAt(kind, arena, Ahead(1.6f));
                await Wait(0.6);
                await Shot($"{kind.ToString().ToLowerInvariant()}-prompt");
                s.QueueFree();
                await Wait(0.2);
            }
            // A charge shrine fills while the player stands in its ring.
            shrines.SpawnAt(MapShrineKind.Charge, arena, _player.GlobalPosition);
            var panel = FindChildren("*", "", true, false).OfType<UI.ShrinePanel>().FirstOrDefault();
            for (int i = 0; i < 60 && panel is { IsOpen: false }; i++) await Wait(0.25);
            await Wait(0.6);
            await Shot("shrine");
            if (panel is { IsOpen: true }) { Key(Godot.Key.Key1); await Wait(0.5); }
        }

        Screenshot.Invulnerable = false;
        _player.Health.Kill();
        for (int i = 0; i < 40 && !_results.IsShown; i++) await Wait(0.25);
        await Wait(1.5);
        await Shot("results");
        GD.Print("PANELSHOT done");
        tree.Quit();
    }
}
