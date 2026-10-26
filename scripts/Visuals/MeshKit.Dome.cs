using Godot;

namespace Wavebreak.Visuals;

public sealed partial class MeshKit
{
    /// <summary>The top of an ellipsoid centred at the transform origin, cut
    /// off at <paramref name="fromHeight"/> (-1 is the whole ellipsoid, 0 the
    /// upper half, as a fraction of the Y radius) and open below: caps,
    /// helmets and hoods that sit over a head without covering the face.</summary>
    public void Dome(Transform3D xf, Vector3 radii, float fromHeight, Color col, float emit = 0f,
                     int segs = 16, int rings = 6, Color? topColor = null)
    {
        float ph0 = Mathf.Asin(Mathf.Clamp(fromHeight, -1f, 1f));
        Lathe(xf, segs, rings, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            float ph = Mathf.Lerp(ph0, Mathf.Pi * 0.5f, v);
            Vector3 unit = new(Mathf.Cos(ph) * Mathf.Cos(th), Mathf.Sin(ph), Mathf.Cos(ph) * Mathf.Sin(th));
            Vector3 n = new Vector3(unit.X / radii.X, unit.Y / radii.Y, unit.Z / radii.Z).Normalized();
            return (unit * radii, n);
        }, topColor);
    }
}
