namespace Wavebreak.Visuals;

/// <summary>
/// Imported models for the behaviour monsters and the bosses added with them.
///
/// Kept apart from <see cref="EnemyVisuals"/>' own table and fed in through its
/// public <see cref="EnemyVisuals.MapImported"/> hook, so enemy content and the
/// visuals layer can change independently. Chosen for what the enemy *does*:
/// the bloated spiky blob is the one that bursts, the horned demon charges, a
/// floating skull hangs back and casts. Enemies without an entry (Surge Leaper,
/// Deepcaller, Shoal Fry) keep the procedural model the visuals layer
/// classifies from their numbers.
/// </summary>
public static class BehaviourModels
{
    private static bool _registered;

    /// <summary>Install the mappings once per process. Called when the enemy
    /// manager is configured, before any of these enemies is built.</summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        EnemyVisuals.MapImported("brinespitter", "quaternius/blob/PinkBlob.gltf");
        EnemyVisuals.MapImported("bloat_polyp", "quaternius/blob/GreenSpikyBlob.gltf");
        EnemyVisuals.MapImported("reefhorn", "quaternius/big/BlueDemon.gltf");
        EnemyVisuals.MapImported("shoalmother", "quaternius/blob/Mushnub_Evolved.gltf");
        EnemyVisuals.MapImported("undertow_oracle", "quaternius/flying/Ghost_Skull.gltf");
        EnemyVisuals.MapImported("knell_leviathan", "quaternius/flying/Dragon_Evolved.gltf");
        ExpansionModels.EnsureRegistered();
    }
}
