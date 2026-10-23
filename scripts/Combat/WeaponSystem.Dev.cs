namespace Wavebreak.Combat;

/// <summary>Developer-panel build editing (UI/DevPanel).</summary>
public sealed partial class WeaponSystem
{
    /// <summary>Drop one held weapon and everything it put in the world.</summary>
    public bool DevRemove(string id)
    {
        WeaponRuntime? w = Find(id);
        if (w is null) return false;
        w.Dispose();
        _weapons.Remove(w);
        return true;
    }

    /// <summary>Level a held weapon (or take it first) up to its maximum.
    /// Returns the level it ends on, or 0 when there was no slot.</summary>
    public int DevMax(Data.WeaponDef def)
    {
        for (int guard = 0; guard < 64 && CanAdd(def); guard++) Add(def);
        return LevelOf(def.Id);
    }
}
