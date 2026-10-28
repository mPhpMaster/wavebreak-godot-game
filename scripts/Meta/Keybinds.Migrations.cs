using Godot;

namespace Wavebreak.Meta;

public static partial class Keybinds
{
    private static void MigrateScoreboardKey(ConfigFile saved)
    {
        if (saved.GetValue("keys", "binding_schema", 1).AsInt32() >= 2) return;
        // Only migrate the untouched old pair. Any customized primary key remains theirs.
        if (Get("stats", 0) is InputEventKey { PhysicalKeycode: Key.Tab }
            && Get("scoreboard", 0) is InputEventKey { PhysicalKeycode: Key.Y })
        {
            var stats = new InputEvent?[Slots]; var board = new InputEvent?[Slots];
            for (int i = 0; i < Slots; i++) { stats[i] = Get("stats", i); board[i] = Get("scoreboard", i); }
            stats[0] = new InputEventKey { PhysicalKeycode = Key.Y };
            board[0] = new InputEventKey { PhysicalKeycode = Key.Tab };
            SetAll("stats", stats); SetAll("scoreboard", board);
        }
        Save();
    }
}
