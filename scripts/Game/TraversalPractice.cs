using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed class TraversalPractice
{
    private int _step;
    private int _boosts = -1, _strikes = -1, _dashes = -1;
    public string Instruction { get; private set; } = "";
    public void Tick(Player player, bool city)
    {
        if (_boosts < 0) { _boosts = player.HookBoosts; _strikes = player.CoreStrikes; _dashes = player.Dashes; }
        if (city)
        {
            if (_step == 0 && player.LeftHookAttached) _step++;
            if (_step == 1 && player.RightHookAttached) _step++;
            if (_step == 2 && player.HookBoosts > _boosts) _step++;
            if (_step == 3 && player.CoreStrikes > _strikes) _step++;
            Instruction = _step switch
            {
                0 => "Practice 1/4: attach your left hook to a rooftop.",
                1 => "Practice 2/4: attach your right hook independently.",
                2 => "Practice 3/4: jump or dash while attached to boost.",
                3 => "Practice 4/4: dash near a glowing core while moving fast.",
                _ => "Traversal practice complete!",
            };
        }
        else
        {
            if (_step == 0 && player.Velocity.Length() > 2) _step++;
            if (_step == 1 && player.Velocity.Y > 1 && Input.IsActionPressed("jump")) _step++;
            if (_step == 2 && player.Dashes > _dashes) _step++;
            Instruction = _step switch
            {
                0 => "Practice 1/3: look around and swim forward.",
                1 => "Practice 2/3: hold jump to swim upward.",
                2 => "Practice 3/3: dash and steer while the burst slows down.",
                _ => "Swimming practice complete!",
            };
        }
    }
}
