using System;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Hats on a rig. Imported Kenney heroes carry a skeleton with a "head" bone,
/// and the hat follows that bone (position and rotation relative to its rest
/// pose, so the bone's own axis convention does not matter). Procedural heroes
/// have the head baked into the body mesh, so the hat is parented to the hips
/// at the head's computed top, which is exactly where ModelBuilder puts it.
///
/// Hat meshes are authored in head units (head half-width = 1, origin on the
/// crown, front = -Z); this file measures each rig's head and scales to fit.
/// </summary>
public sealed partial class CharacterRig
{
    private Node3D? _hat;
    private string? _hatId;

    /// <summary>Wear <paramref name="hatId"/> (null or "none" removes the hat).</summary>
    public void SetHat(string? hatId)
    {
        if (hatId == _hatId && (_hat is null || IsInstanceValid(_hat))) return;
        if (_hat is not null && IsInstanceValid(_hat))
        {
            // Out of the tree now, not at frame end: the new hat measures the head
            // and must not count the old one as part of it.
            _hat.GetParent()?.RemoveChild(_hat);
            _hat.QueueFree();
        }
        _hat = null;
        _hatId = hatId;
        if (string.IsNullOrEmpty(hatId) || hatId == Meta.Hats.NoneId) return;
        if (HatModels.Get(hatId) is not { } mesh) return;

        var mi = new MeshInstance3D
        {
            Name = "HatMesh",
            Mesh = mesh,
            MaterialOverride = ToonMaterials.CharacterNoOutline,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        if (Spec.IsImported && FindHeadBone(out Skeleton3D? skel, out int bone, out float halfWidth, out Vector3 offset))
        {
            var follow = new HatFollower(skel!, bone, offset, Spec.SceneYaw, _hips) { Name = "Hat" };
            mi.Scale = Vector3.One * halfWidth;
            follow.AddChild(mi);
            AddChild(follow);
            _hat = follow;
            return;
        }

        // Rigid imported models (the blocky set) have no skeleton but a part
        // named "head": sit on that part, sized to it, so it nods along.
        if (Spec.IsImported && FindHeadNode(out MeshInstance3D? headNode))
        {
            Aabb hb = headNode!.Mesh.GetAabb();
            // Hats are authored for round heads (half-width 1); a cube head's
            // corners stick out past that, so size to the corner, not the face.
            float half = MathF.Max(hb.Size.X, hb.Size.Z) * 0.5f * 1.35f;
            var onHead = new Node3D
            {
                Name = "Hat",
                Position = new Vector3(hb.Position.X + hb.Size.X * 0.5f, hb.Position.Y + hb.Size.Y, hb.Position.Z + hb.Size.Z * 0.5f),
                Rotation = new Vector3(0f, -Spec.SceneYaw, 0f),
            };
            mi.Scale = Vector3.One * half;
            onHead.AddChild(mi);
            headNode.AddChild(onHead);
            _hat = onHead;
            return;
        }

        // Procedural (or an imported model without a head bone): sit on the
        // computed crown under the hips, so lean and bob carry it.
        float h = Spec.Height;
        float hr, top;
        if (!Spec.IsImported && Spec.Body == BodyType.Biped && _model is not null)
        {
            hr = 0.155f * h * Spec.HeadScale;
            float shoulder = 0.70f * h - _model.HipsOffset.Y;
            top = shoulder + hr * 1.85f;
        }
        else
        {
            // HeadScale is unused by imported models otherwise; the cube pets
            // set it, since their whole body is the head (PlayerLooks.Cute).
            hr = h * 0.14f * Spec.HeadScale;
            top = h * Spec.HatTop - (_model?.HipsOffset.Y ?? 0f);
        }
        // Never below the model's real top: the formulas above sank hats into
        // the cube pets, whose whole body is the head.
        Aabb body = MeasureScene(_hips);
        if (body.Size.Y > 0.01f) top = MathF.Max(top, body.Position.Y + body.Size.Y - hr * 0.08f);
        var anchor = new Node3D { Name = "Hat", Position = new Vector3(0f, top, 0f) };
        mi.Scale = Vector3.One * hr;
        anchor.AddChild(mi);
        _hips.AddChild(anchor);
        _hat = anchor;
    }

    /// <summary>Finds a bone named like "head" and measures the head from the
    /// rest pose: half-width in world metres, and the crown's offset from the
    /// bone in skeleton space.</summary>
    private bool FindHeadBone(out Skeleton3D? skel, out int bone, out float halfWidth, out Vector3 offset)
    {
        skel = null; bone = -1; halfWidth = 0f; offset = Vector3.Zero;
        Node3D? inst = _hips.GetChildCount() > 0 ? _hips.GetChild(0) as Node3D : null;
        if (inst is null) return false;
        skel = FindSkeleton(inst);
        if (skel is null) return false;

        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            string n = skel.GetBoneName(i).ToLowerInvariant();
            if (n == "head" || (bone < 0 && n.Contains("head"))) bone = i;
        }
        if (bone < 0) return false;

        // Skeleton -> instance transform (unscaled instance space).
        Transform3D skelToInst = Transform3D.Identity;
        for (Node? n = skel; n is not null && n != inst; n = n.GetParent())
            if (n is Node3D n3) skelToInst = n3.Transform * skelToInst;

        Aabb box = MeasureScene(inst);
        Transform3D rest = skel.GetBoneGlobalRest(bone);
        Vector3 headInst = skelToInst * rest.Origin;
        // The crown is the highest vertex skinned to the head bone. The whole
        // model's box was too high on some heroes (hair, loose skinned-mesh
        // bounds) and floated the hat a hand above the head.
        float topInst = HeadTop(inst, skel, bone) ?? box.Position.Y + box.Size.Y;
        // Some rigs (the blocky set) put the head bone near the crown, which
        // made the head look a few centimetres tall and shrank the hat to a
        // sliver; a head is never less than about a fifth of the body.
        float headHeight = MathF.Max(topInst - headInst.Y, 0.22f * box.Size.Y);
        Vector3 crownSkel = skelToInst.AffineInverse() * new Vector3(headInst.X, topInst, headInst.Z);
        offset = crownSkel - rest.Origin;

        float scale = Spec.SceneScale > 0f ? Spec.SceneScale : 1f;
        halfWidth = headHeight * 0.68f * scale;
        return true;
    }

    /// <summary>A mesh part named "head" in the imported model, if any.</summary>
    private bool FindHeadNode(out MeshInstance3D? head)
    {
        head = null;
        Node? inst = _hips.GetChildCount() > 0 ? _hips.GetChild(0) : null;
        if (inst is null) return false;
        head = Find(inst);
        return head is not null;

        static MeshInstance3D? Find(Node n)
        {
            if (n is MeshInstance3D m && m.Mesh is not null && n.Name.ToString().ToLowerInvariant() == "head") return m;
            foreach (Node c in n.GetChildren())
                if (Find(c) is { } f) return f;
            return null;
        }
    }

    /// <summary>Highest point, in <paramref name="inst"/> space, of the rest-pose
    /// vertices weighted mostly to <paramref name="bone"/>; null if none are.</summary>
    private static float? HeadTop(Node3D inst, Skeleton3D skel, int bone)
    {
        float? best = null;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D { Mesh: ArrayMesh mesh } mi)
            {
                Skin? skin = mi.Skin;
                for (int si = 0; si < mesh.GetSurfaceCount(); si++)
                {
                    var arrays = mesh.SurfaceGetArrays(si);
                    if (arrays[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.Nil) continue;
                    Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    int[] bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    if (verts.Length == 0 || bones.Length < verts.Length) continue;
                    int per = bones.Length / verts.Length;
                    for (int v = 0; v < verts.Length; v++)
                        for (int k = 0; k < per; k++)
                        {
                            int bi = v * per + k;
                            if (weights.Length <= bi || weights[bi] < 0.5f) continue;
                            int b = bones[bi];
                            if (skin is not null && b < skin.GetBindCount())
                            {
                                string name = skin.GetBindName(b);
                                b = name.Length > 0 ? skel.FindBone(name) : skin.GetBindBone(b);
                            }
                            if (b != bone) continue;
                            float y = (here * verts[v]).Y;
                            if (best is null || y > best) best = y;
                        }
                }
            }
            foreach (Node c in n.GetChildren()) Walk(c, here);
        }
        foreach (Node c in inst.GetChildren()) Walk(c, Transform3D.Identity);
        return best;
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (Node c in n.GetChildren())
            if (FindSkeleton(c) is { } found) return found;
        return null;
    }
}

/// <summary>Keeps a hat on a bone: rest-relative rotation, crown offset,
/// and the rig's facing (the model's own import yaw is undone so the hat's
/// front stays the character's front).</summary>
internal sealed partial class HatFollower : Node3D
{
    private readonly Skeleton3D _skel;
    private readonly int _bone;
    private readonly Vector3 _offset;
    private readonly Basis _unyaw;
    private readonly Basis _restInv;

    public HatFollower() { _skel = null!; }

    public HatFollower(Skeleton3D skel, int bone, Vector3 offset, float yaw, Node3D hips)
    {
        _skel = skel;
        _bone = bone;
        _offset = offset;
        _unyaw = new Basis(Vector3.Up, -yaw);
        _restInv = skel.GetBoneGlobalRest(bone).Basis.Inverse();
        TopLevel = true;
    }

    public override void _Process(double delta)
    {
        if (_skel is null || !IsInstanceValid(_skel) || !_skel.IsInsideTree()) return;
        Transform3D sg = _skel.GlobalTransform;
        Transform3D pose = _skel.GetBoneGlobalPose(_bone);
        Basis d = pose.Basis * _restInv;
        Vector3 local = pose.Origin + d * _offset;
        GlobalTransform = new Transform3D(sg.Basis.Orthonormalized() * d * _unyaw, sg * local);
    }
}
