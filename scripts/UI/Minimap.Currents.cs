using Godot;
using Wavebreak.Game;

namespace Wavebreak.UI;

public sealed partial class Minimap
{
    private static readonly Color CurrentInk = new(0.74f, 1f, 0.9f);

    /// <summary>Currents drawn so far (the currents self-check reads it).</summary>
    public static int CurrentsDrawn;

    /// <summary>
    /// Water currents, kept subtle: a faint band along the flow with a small
    /// arrowhead; an up- or downdraft is a ring with a chevron. Telegraphing
    /// currents are fainter still. Clipped to the face, never pinned.
    /// </summary>
    private void DrawCurrents()
    {
        if (ReefCurrentField.Instance is not { } field) return;
        float clock = field.Clock;
        foreach (ReefCurrentFlow flow in field.Live)
        {
            float alpha = 0.18f + 0.3f * flow.Visibility(clock);
            Color ink = CurrentInk with { A = alpha };
            float width = Mathf.Clamp(flow.Radius * 2f * _scale, 2f, 9f);
            if (new Vector2(flow.Direction.X, flow.Direction.Z).Length() < 0.35f)
            {
                if (Inside(flow.Centre, 6f) is not Vector2 at) continue;
                float r = Mathf.Max(3.5f, flow.Radius * _scale);
                DrawArc(at, r, 0f, Mathf.Tau, 20, ink, 1.5f, true);
                float up = flow.Direction.Y > 0 ? -1f : 1f;   // screen y grows downward
                DrawPolyline(new[] { at + new Vector2(-r * 0.5f, -up * r * 0.25f), at + new Vector2(0f, up * r * 0.35f), at + new Vector2(r * 0.5f, -up * r * 0.25f) }, ink, 1.5f, true);
                CurrentsDrawn++;
                continue;
            }
            Vector2? last = null, tip = null, before = null;
            bool any = false;
            for (int i = 0; i <= 12; i++)
            {
                Vector2? p = Inside(flow.From.Lerp(flow.To, i / 12f), 4f);
                if (p is Vector2 now && last is Vector2 prev) { DrawLine(prev, now, ink with { A = alpha * 0.55f }, width, true); any = true; before = prev; tip = now; }
                last = p;
            }
            if (!any || tip is not Vector2 head || before is not Vector2 back) continue;
            Vector2 dir = (head - back).Normalized(), normal = new(-dir.Y, dir.X);
            DrawPolyline(new[] { head - dir * 5f + normal * 3.5f, head, head - dir * 5f - normal * 3.5f }, ink, 1.6f, true);
            CurrentsDrawn++;
        }
    }
}
