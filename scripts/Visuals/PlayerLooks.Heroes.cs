namespace Wavebreak.Visuals;

/// <summary>
/// Looks for the heroes trio (Meta/Characters.Heroes.cs): hand-built bodies
/// of their own (<see cref="HeroModels"/>), not shared with any other
/// character or enemy. They used to wear Kenney cube pets, which read as
/// repeats of the cute crew; the look ids changed with the bodies
/// ("pet-parrot" became "hero-sunny" and so on), and since saves keep skin
/// ids, not look ids, nothing stored needs migrating.
/// </summary>
public static partial class PlayerLooks
{
    private static ModelSpec? ResolveHero(string id) => HeroModels.Spec(id);
}
