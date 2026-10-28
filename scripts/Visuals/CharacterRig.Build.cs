using System;
using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    // ------------------------------------------------------------ building

    /// <summary>
    /// Instance a model. <paramref name="faceZ"/>: the parent turns to face +Z
    /// (enemies do) rather than Godot's -Z, so the rig is turned around inside it.
    /// <paramref name="outline"/>: near parts get the ink outline pass.
    /// </summary>
    public static CharacterRig Create(ModelSpec spec, bool faceZ = false, bool outline = true)
    {
        var rig = new CharacterRig { Name = "Rig" };
        rig.Build(spec, faceZ, outline);
        return rig;
    }

    private void Build(ModelSpec spec, bool faceZ, bool outline)
    {
        Spec = spec;
        if (faceZ) Rotation = new Vector3(0f, Mathf.Pi, 0f);
        _seed = (GetInstanceId() % 1000) * 0.0137f;

        _hips = new Node3D { Name = "Hips" };
        AddChild(_hips);

        if (spec.IsImported)
        {
            BuildImported(spec);
            return;
        }

        _model = ModelBuilder.Get(spec);
        _hips.Position = _model.HipsOffset;
        Material mat = outline ? ToonMaterials.Character : ToonMaterials.CharacterNoOutline;
        float lod = spec.LodDistance;

        var geoms = new System.Collections.Generic.List<GeometryInstance3D>();
        MeshInstance3D Mesh(Node parent, Mesh mesh, string name)
        {
            var mi = new MeshInstance3D
            {
                Name = name,
                Mesh = mesh,
                MaterialOverride = mat,
                // Characters do not cast sun shadows: two hundred extra shadow
                // casters doubles the draw count for a detail nobody sees in a
                // crowd. The hero opts back in (see EnableShadow).
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            if (lod > 0f)
            {
                mi.VisibilityRangeEnd = lod;
                mi.VisibilityRangeEndMargin = 1.5f;
            }
            parent.AddChild(mi);
            geoms.Add(mi);
            return mi;
        }

        _bodyMesh = Mesh(_hips, _model.Body, "Body");
        _parts = new Part[_model.Parts.Count];
        for (int i = 0; i < _parts.Length; i++)
        {
            PartDef d = _model.Parts[i];
            MeshInstance3D up = Mesh(_hips, d.Upper, d.Role.ToString());
            up.Position = d.Pivot;
            if (d.Scale != 1f) up.Scale = Vector3.One * d.Scale;
            MeshInstance3D? low = null;
            if (d.Lower is not null)
            {
                low = Mesh(up, d.Lower, d.Role + "Lower");
                low.Position = d.LowerPivot;
            }
            _parts[i] = new Part { Def = d, Upper = up, Lower = low };
        }

        if (lod > 0f)
        {
            _impostor = new MeshInstance3D
            {
                Name = "Far",
                Mesh = _model.Impostor,
                MaterialOverride = ToonMaterials.CharacterNoOutline,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeBegin = lod,
                VisibilityRangeBeginMargin = 1.5f,
                Position = -_model.HipsOffset,
            };
            _hips.AddChild(_impostor);
            geoms.Add(_impostor);
        }

        if (_model.AuraRadius > 0f && spec.Aura)
        {
            _aura = new MeshInstance3D
            {
                Name = "Aura",
                Mesh = AuraMesh(_model.AuraRadius),
                MaterialOverride = ToonMaterials.Aura,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, 0.04f, 0f),
            };
            AddChild(_aura);
            _aura.SetInstanceShaderParameter("glow", spec.AuraColor);
        }

        _geoms = geoms.ToArray();
    }

    private static readonly System.Collections.Generic.Dictionary<int, Mesh> AuraMeshes = new();

    private static Mesh AuraMesh(float radius)
    {
        int key = Mathf.RoundToInt(radius * 10f);
        if (AuraMeshes.TryGetValue(key, out Mesh? m) && IsInstanceValid(m)) return m;
        m = new PlaneMesh { Size = new Vector2(radius * 2f, radius * 2f) };
        AuraMeshes[key] = m;
        return m;
    }

    /// <summary>Hero only: let the near parts cast sun shadows.</summary>
    public void EnableShadow()
    {
        foreach (GeometryInstance3D g in _geoms)
            if (g != _impostor) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
    }

    /// <summary>The merged rest-pose mesh: afterimages and ghosts use it.</summary>
    public Mesh? SilhouetteMesh => _model?.Impostor;

    /// <summary>Global transform of the posed body, for effects that should
    /// start where the body visibly is.</summary>
    public Transform3D BodyTransform => _hips.GlobalTransform;

    // ------------------------------------------------------------- imported

    private void BuildImported(ModelSpec spec)
    {
        _model = new BuiltModel { Height = spec.Height };
        Node3D inst = spec.Scene!.Instantiate<Node3D>();
        Node3D visual = inst;
        if (spec.PreserveSceneRootTransform)
        {
            visual = new Node3D { Name = "ImportedNormalization" };
            visual.AddChild(inst);
        }
        _hips.AddChild(visual);
        visual.Rotation = new Vector3(0f, spec.SceneYaw, 0f);

        float scale = spec.SceneScale;
        if (scale <= 0f)
        {
            Aabb box = MeasureScene(visual);
            spec.SceneSourceBounds = box;
            scale = box.Size.Y > 0.001f ? spec.Height / box.Size.Y : 1f;
            if (spec.PreserveSceneRootTransform)
                spec.SceneOffset = new Basis(Vector3.Up, spec.SceneYaw)
                    * new Vector3(-box.GetCenter().X * scale, -box.Position.Y * scale, -box.GetCenter().Z * scale);
            spec.SceneScale = scale; // measured once per spec
        }
        visual.Scale = Vector3.One * scale;
        if (spec.PreserveSceneRootTransform) visual.Position = spec.SceneOffset;
        _player = FindPlayer(inst);
        if (_player is not null)
        {
            // Advanced by hand: lets far crowds animate at a fraction of the rate
            // (see AnimateImported), which is most of an imported crowd's cost.
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            LoopLocomotion(_player, spec);
            PlayClip(AnimSlot.Idle, 0f);
            // Desynchronise identical enemies so a wave does not march in step.
            _player.Advance((GetInstanceId() % 97) * 0.013f);
        }
        var geoms = new System.Collections.Generic.List<GeometryInstance3D>();
        CollectGeoms(inst, geoms);
        _geoms = geoms.ToArray();
        if (spec.SceneAlbedoFloor > 0) ImportedMaterialVisibility.Apply(_geoms, spec.SceneAlbedoFloor);

        if (spec.Crown)
        {
            // Imported models have no head anchor we can trust, so the elite crown
            // floats above them, which reads better at distance anyway.
            _crown = new MeshInstance3D
            {
                Name = "Crown",
                Mesh = CrownMesh(),
                MaterialOverride = ToonMaterials.CharacterNoOutline,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, spec.Height * 1.08f, 0f),
                Scale = Vector3.One * Mathf.Clamp(spec.Height * 0.3f, 0.4f, 1.6f),
            };
            _hips.AddChild(_crown);
        }
        if (spec.Aura) AddAura(Mathf.Max(0.8f, spec.Height * 0.45f), spec.AuraColor);
        _rimMaterial = spec.Aura ? ToonMaterials.RimOverlay(spec.AuraColor) : null;
        ApplyOverlay(_rimMaterial);
    }

    private MeshInstance3D? _crown;
    private Material? _rimMaterial;
    private Material? _overlay;
    private float _animAccum;
    private int _animStep = 1, _animCounter;

    private static Mesh? _crownMesh;

    private static Mesh CrownMesh()
    {
        if (_crownMesh is not null && IsInstanceValid(_crownMesh)) return _crownMesh;
        var k = new MeshKit();
        Color gold = new(1f, 0.82f, 0.3f);
        k.Cylinder(Transform3D.Identity, 0.5f, 0.55f, 0.18f, gold, 0.9f, 10);
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Tau;
            Vector3 b0 = new(Mathf.Cos(a) * 0.5f, 0.15f, Mathf.Sin(a) * 0.5f);
            k.Cone(b0, b0 + new Vector3(0f, 0.38f, 0f), 0.12f, gold, 1.2f, 4);
            k.Sphere(b0 + new Vector3(0f, 0.42f, 0f), 0.06f, new Color(1f, 0.4f, 0.35f), 1.6f, 5, 3);
        }
        _crownMesh = k.ToMesh();
        return _crownMesh;
    }

    private void AddAura(float radius, Color color)
    {
        _aura = new MeshInstance3D
        {
            Name = "Aura",
            Mesh = AuraMesh(radius),
            MaterialOverride = ToonMaterials.Aura,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 0.04f, 0f),
        };
        AddChild(_aura);
        _aura.SetInstanceShaderParameter("glow", color);
    }

    /// <summary>Imported materials are the model's own, so the hit flash and the
    /// elite rim go on as an overlay pass, swapped in only while needed: an
    /// ordinary enemy that is not being hit draws exactly what the artist made.</summary>
    private void ApplyOverlay(Material? m)
    {
        if (m == _overlay) return;
        _overlay = m;
        foreach (GeometryInstance3D g in _geoms) g.MaterialOverlay = m;
    }

    private static Aabb MeasureScene(Node3D root)
    {
        Aabb box = new();
        bool any = false;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb b = here * mi.Mesh.GetAabb();
                box = any ? box.Merge(b) : b;
                any = true;
            }
            foreach (Node child in n.GetChildren()) Walk(child, here);
        }
        foreach (Node child in root.GetChildren()) Walk(child, Transform3D.Identity);
        return box;
    }

    /// <summary>
    /// Godot imports glTF clips as play-once unless the file says otherwise, and
    /// neither the Kenney nor the Quaternius exports do. A one-shot walk plays a
    /// single stride and then holds its last frame -- the body glides with its
    /// legs frozen. Locomotion clips must loop; attacks, hits and deaths must not.
    /// The Animation resources are shared by every instance of a model, so this
    /// is idempotent and effectively runs once per model.
    /// </summary>
    private static void LoopLocomotion(AnimationPlayer player, ModelSpec spec)
    {
        foreach (AnimSlot slot in new[] { AnimSlot.Idle, AnimSlot.Walk, AnimSlot.Run, AnimSlot.Dash,
                                          AnimSlot.Slide, AnimSlot.Fall })
        {
            if (!spec.Clips.TryGetValue(slot, out string? name) || !player.HasAnimation(name)) continue;
            Animation anim = player.GetAnimation(name);
            if (anim.LoopMode == Animation.LoopModeEnum.None) anim.LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    private static AnimationPlayer? FindPlayer(Node n)
    {
        if (n is AnimationPlayer p) return p;
        foreach (Node c in n.GetChildren())
            if (FindPlayer(c) is { } found) return found;
        return null;
    }

    private static void CollectGeoms(Node n, System.Collections.Generic.List<GeometryInstance3D> into)
    {
        if (n is GeometryInstance3D g)
        {
            g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            into.Add(g);
        }
        foreach (Node c in n.GetChildren()) CollectGeoms(c, into);
    }

}
