using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>The character sheet's tome section and the level-up charges.</summary>
public sealed partial class StatsPanel
{
    private void TomeSection()
    {
        TomeBook? book = TomeBook.Current;
        int slots = book?.Slots ?? Tomes.MaxSlots;
        Section(_loadout, $"TOMES  {book?.Count ?? 0}/{slots}");
        if (book is null || book.Count == 0)
        {
            _loadout.AddChild(Text("none yet", 14, Dim));
        }
        else
        {
            foreach (TomeDef t in book.Held)
            {
                int level = book.LevelOf(t);
                Row(_loadout, t.DisplayName, $"Lv {level}/{t.MaxLevel}  {t.DescribeTotal(level)}", t.Tint, Icons.For(t));
            }
        }

        if (LevelUpChoices.Current is { } c)
            Row(_loadout, "Reroll · Banish · Skip", $"{c.RerollsLeft} · {c.BanishesLeft} · {c.SkipsLeft}", Dim);
    }
}
