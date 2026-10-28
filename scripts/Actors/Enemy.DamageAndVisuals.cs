using System;
using System.Reflection;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    // -------------------------------------------------------------- damage

    /// <inheritdoc/>
    public void TakeDamage(DamageInfo info)
    {
        if (info is null) return;

        if (_dead)
        {
            // Report nothing landed, so lifesteal and on-hit effects do not fire
            // off a corpse that a pierce shot happened to pass through.
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        // Co-op client: this body is the host's enemy; the hit is predicted
        // here and sent to the host, which owns the health (Enemy.Coop.cs).
        if (NetId > 0 && Net.Coop.IsClient)
        {
            PuppetHit(info);
            return;
        }

        if (InterceptHit(info)) return;
        float amount = MathF.Max(0f, info.Amount);
        if (amount <= 0f)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        // Overkill is not damage dealt. Clamping here is what makes lifesteal and
        // damage meters read the truth rather than the swing size.
        if (amount > _health) amount = _health;
        info.Amount = amount;

        _health -= amount;
        NoteDamage(amount);   // last hitter + scoreboard (Enemy.Awareness.cs)
        _flashTimer = FlashDuration;
        _rig?.PlayHit();

        if (!IsCityGiant && info.Knockback > 0f && _act != Act.Dash) AddKnockback(info.Direction, info.Knockback);

        if (_health <= 0f) Die();
        else CheckPhase();
    }

    private void AddKnockback(Vector3 direction, float strength)
    {
        Vector3 flat = new(direction.X, 0f, direction.Z);
        float lengthSquared = flat.LengthSquared();
        if (lengthSquared < 1e-6f) return;

        flat /= MathF.Sqrt(lengthSquared);
        _knockback += flat * strength * KnockbackScale * (1f - _knockbackResist);

        float speedSquared = _knockback.LengthSquared();
        if (speedSquared > MaxKnockbackSpeed * MaxKnockbackSpeed)
            _knockback = _knockback / MathF.Sqrt(speedSquared) * MaxKnockbackSpeed;
    }

    private void Die()
    {
        if (_dead) return;
        ResetCityGiant();
        _dead = true;
        _health = 0f;

        int xp = _def?.XpValue ?? 0;
        int gold = _def?.GoldValue ?? 0;
        Data.ContentAudit.NoteKill(_def);
        if (IsBoss) Data.ContentAudit.NoteBossDeath(_def, _aliveSeconds);
        Visuals.EnemyVisuals.SpawnCorpse(_rig);
        Split();

        // Slightly above the feet: drops read better when they are not embedded
        // in the floor, and the pickup layer spawns from this point.
        EventBus.Publish(new EnemyKilled(GlobalPosition + new Vector3(0f, 0.35f, 0f), xp, gold, IsElite || IsBoss));
        if (NetId > 0 && Net.Coop.IsHost) Net.NetSession.Instance?.OnEnemyGone(this, killed: true);
        NoteDeath();   // kill credit and the boss toast (Enemy.Awareness.cs)

        Deactivate();
        Despawned?.Invoke(this);
    }

    /// <summary>Make the body inert immediately. Layer/mask zeroing rather than
    /// disabling shapes: it takes effect the moment it is set, so a corpse cannot
    /// be hit by a projectile later in the same physics step, and it avoids the
    /// deferred-call ordering trap that shape toggling has when a node dies and
    /// respawns inside one frame.</summary>
    private void Deactivate()
    {
        EndBehaviour();
        ClearStatuses();
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        _knockback = Vector3.Zero;
        AliveIndex = -1;
        NetId = 0;
    }

    // ------------------------------------------------------------- visuals

    private void UpdateFlash(float dt)
    {
        RefreshWounds();
        if (_flashTimer <= 0f) return;
        _flashTimer -= dt;
        ApplyFlash(_flashTimer <= 0f ? 0f : _flashTimer / FlashDuration);
    }

    /// <summary>
    /// Writes body colour from two inputs: the hit flash (white, brief) and the
    /// wind-up glow (hot, held for the whole telegraph). Combined in one place
    /// so neither can leave the other's colour behind when it ends.
    /// </summary>
    private void ApplyFlash(float amount)
    {
        if (_bodyMaterial is null) return;

        float t = Mathf.Clamp(amount, 0f, 1f);
        float g = Mathf.Clamp(_glow, 0f, 1f);
        float baseEnergy = EmissionFor(_builtSilhouette) + (_phase >= 2 ? 0.7f : 0f);

        float st = _statusTintAmount;
        Color body = _baseColor.Lerp(_statusTint, st).Lerp(GlowColor, g * 0.6f).Lerp(Colors.White, t);
        _bodyMaterial.AlbedoColor = body;
        _bodyMaterial.Emission = body;
        _bodyMaterial.EmissionEnergyMultiplier = baseEnergy + t * 2.2f + g * 2.6f + st * 1.2f;

        if (_accentMaterial is not null)
        {
            Color accent = _accentColor.Lerp(GlowColor, g * 0.6f).Lerp(Colors.White, t);
            _accentMaterial.AlbedoColor = accent;
            _accentMaterial.Emission = accent;
            _accentMaterial.EmissionEnergyMultiplier = baseEnergy * 1.8f + t * 2.2f + g * 3f;
        }
    }

    /// <summary>Wind-up colour. Warm and bright on every enemy, so "about to
    /// do something" reads the same across the whole roster.</summary>
    private static readonly Color GlowColor = new(1.00f, 0.86f, 0.55f);

    private static float EmissionFor(Silhouette s) => s switch
    {
        Silhouette.Boss => 0.85f,
        Silhouette.Elite => 0.55f,
        Silhouette.Caster => 0.45f,
        Silhouette.Bloat => 0.4f,
        _ => 0.22f,
    };

    /// <summary>
    /// Which primitive body an enemy wears.
    ///
    /// Chosen from stats rather than authored per definition, so silhouette and
    /// behaviour cannot drift apart: if a designer makes something fast, it
    /// becomes a low sphere and the player learns to read speed off the shape.
    /// Behaviour outranks stats for the same reason: what an enemy *does* is the
    /// first thing the player needs to read off it. A tapered tower with an orb
    /// shoots, a ball swells and bursts, a horned block charges, a squat one
    /// jumps.
    /// </summary>
    private enum Silhouette { None, Grunt, Runner, Brute, Elite, Boss, Caster, Bloat, Horned, Hopper }

    private static Silhouette Classify(EnemyDef def)
    {
        if (def.IsBoss) return Silhouette.Boss;
        if (def.IsElite) return Silhouette.Elite;
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter: return Silhouette.Caster;
            case EnemyBehavior.Exploder: return Silhouette.Bloat;
            case EnemyBehavior.Charger: return Silhouette.Horned;
            case EnemyBehavior.Leaper: return Silhouette.Hopper;
        }
        if (def.MoveSpeed >= 5f) return Silhouette.Runner;
        if (def.Health >= 60f || def.KnockbackResist >= 0.5f) return Silhouette.Brute;
        return Silhouette.Grunt;
    }

    private void BuildBody(EnemyDef def, float radius, float height)
    {
        _rig = Visuals.EnemyVisuals.Attach(this, def, _rig);
        Silhouette wanted = Classify(def);
        bool sameShape = wanted == _builtSilhouette
                      && Mathf.IsEqualApprox(radius, _builtRadius)
                      && Mathf.IsEqualApprox(height, _builtHeight);

        _baseColor = def.Tint;
        _accentColor = def.Tint.Lightened(0.45f);

        if (sameShape)
        {
            ApplyFlash(0f);
            return;
        }

        _builtSilhouette = wanted;
        _builtRadius = radius;
        _builtHeight = height;

        EnsureNodes();

        // Capsule collision for every silhouette: the shape the player fights is
        // the shape they see, and a single primitive keeps the physics step flat.
        _capsule!.Radius = radius;
        _capsule.Height = height;
        _collision!.Position = new Vector3(0f, height * 0.5f, 0f);

        _bodyMaterial ??= NewMaterial();
        _bodyMesh!.MaterialOverride = _bodyMaterial;
        _bodyMesh.Mesh = MeshLibrary.Body(wanted, radius, height);
        _bodyMesh.Position = new Vector3(0f, height * 0.5f, 0f);
        _bodyScale = BodyScale(wanted);
        _bodyMesh.Scale = _bodyScale;
        _bodyMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        Mesh? accent = MeshLibrary.Accent(wanted, radius, height);
        if (accent is null)
        {
            if (_accentMesh is not null) _accentMesh.Visible = false;
        }
        else
        {
            EnsureAccent();
            _accentMaterial ??= NewMaterial();
            _accentMesh!.Visible = true;
            _accentMesh.MaterialOverride = _accentMaterial;
            _accentMesh.Mesh = accent;
            // The horn sits on the front face and points along it (+Z is the
            // way the body turns to face); everything else crowns the head.
            bool horn = wanted == Silhouette.Horned;
            _accentMesh.Position = horn
                ? new Vector3(0f, height * 0.62f, radius * 1.15f)
                : new Vector3(0f, height + radius * 0.45f, 0f);
            _accentMesh.RotationDegrees = horn ? new Vector3(90f, 0f, 0f) : Vector3.Zero;
            _accentMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        _bodyMesh.Visible = _rig is null; // the rig draws the body when there is one
        if (_accentMesh is not null) _accentMesh.Visible = _rig is null && accent is not null;

        // Only the things worth noticing get a light. Hundreds of OmniLight3Ds
        // is the fastest way to melt the forward+ renderer.
        bool wantsLight = wanted is Silhouette.Elite or Silhouette.Boss;
        if (wantsLight)
        {
            _light ??= NewLight();
            _light.Visible = true;
            _light.Position = new Vector3(0f, height * 0.6f, 0f);
            _light.OmniRange = wanted == Silhouette.Boss ? radius * 14f : radius * 8f;
            _light.LightEnergy = wanted == Silhouette.Boss ? 3.2f : 1.7f;
            _light.LightColor = _accentColor;
        }
        else if (_light is not null)
        {
            _light.Visible = false;
        }

        ApplyFlash(0f);
    }

    private static Vector3 BodyScale(Silhouette s) => s switch
    {
        // Squashed and stretched along the travel axis — reads as "moving fast"
        // even standing still.
        Silhouette.Runner => new Vector3(1f, 0.78f, 1.3f),
        Silhouette.Boss => new Vector3(1.05f, 1f, 1.05f),
        Silhouette.Hopper => new Vector3(1.18f, 0.74f, 1.18f),
        _ => Vector3.One,
    };

    private void EnsureNodes()
    {
        if (_collision is null)
        {
            _capsule = new CapsuleShape3D();
            _collision = new CollisionShape3D { Shape = _capsule, Name = "Body" };
            AddChild(_collision);
        }
        if (_bodyMesh is null)
        {
            _bodyMesh = new MeshInstance3D { Name = "Visual" };
            AddChild(_bodyMesh);
        }
    }

    private void EnsureAccent()
    {
        if (_accentMesh is not null) return;
        _accentMesh = new MeshInstance3D { Name = "Accent" };
        AddChild(_accentMesh);
    }

    private OmniLight3D NewLight()
    {
        OmniLight3D light = new()
        {
            Name = "Glow",
            ShadowEnabled = false,
            OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid,
        };
        AddChild(light);
        return light;
    }

    /// <summary>One material per instance, because the hit flash writes to it.
    /// Created once per pooled node and then reused for the whole run.</summary>
    private static StandardMaterial3D NewMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        EmissionEnabled = true,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };

    /// <summary>
    /// Shared meshes, keyed by silhouette and size.
    ///
    /// Every enemy of a given type has identical geometry, so building a mesh per
    /// instance would allocate hundreds of copies of the same vertex buffer and
    /// throw away the renderer's chance to batch them.
    /// </summary>
    private static class MeshLibrary
    {
        private static readonly System.Collections.Generic.Dictionary<string, Mesh> Cache = new();

        public static Mesh Body(Silhouette s, float radius, float height)
        {
            string key = $"b{(int)s}:{radius:0.###}:{height:0.###}";
            if (Cache.TryGetValue(key, out Mesh? cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            Mesh mesh = s switch
            {
                Silhouette.Brute => new BoxMesh { Size = new Vector3(radius * 1.9f, height, radius * 1.9f) },
                Silhouette.Boss => new BoxMesh { Size = new Vector3(radius * 2.1f, height * 0.8f, radius * 2.1f) },
                Silhouette.Horned => new BoxMesh { Size = new Vector3(radius * 1.7f, height * 0.8f, radius * 2.2f) },
                Silhouette.Caster => new CylinderMesh
                {
                    TopRadius = radius * 0.4f,
                    BottomRadius = radius,
                    Height = height,
                    RadialSegments = 8,
                    Rings = 1,
                },
                Silhouette.Bloat => new SphereMesh
                {
                    Radius = radius,
                    Height = radius * 2f,
                    RadialSegments = 12,
                    Rings = 6,
                },
                Silhouette.Runner => new SphereMesh
                {
                    Radius = radius,
                    Height = height,
                    RadialSegments = 10,
                    Rings = 5,
                },
                _ => new CapsuleMesh
                {
                    Radius = radius,
                    Height = height,
                    RadialSegments = 10,
                    Rings = 3,
                },
            };

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>The bit stuck on top: a crown of spikes for elites, a head for
        /// bosses, nothing for the rank and file.</summary>
        public static Mesh? Accent(Silhouette s, float radius, float height)
        {
            if (s is not (Silhouette.Elite or Silhouette.Boss or Silhouette.Caster or Silhouette.Horned)) return null;

            string key = $"a{(int)s}:{radius:0.###}:{height:0.###}";
            if (Cache.TryGetValue(key, out Mesh? cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            Mesh mesh = s switch
            {
                Silhouette.Boss => new SphereMesh
                {
                    Radius = radius * 0.85f,
                    Height = radius * 1.7f,
                    RadialSegments = 10,
                    Rings = 5,
                },
                // The orb a caster fires from; it is what glows on the wind-up.
                Silhouette.Caster => new SphereMesh
                {
                    Radius = radius * 0.55f,
                    Height = radius * 1.1f,
                    RadialSegments = 10,
                    Rings = 5,
                },
                Silhouette.Horned => new PrismMesh
                {
                    Size = new Vector3(radius * 0.8f, radius * 1.3f, radius * 0.8f),
                    LeftToRight = 0.5f,
                },
                _ => new PrismMesh
                {
                    Size = new Vector3(radius * 1.6f, radius * 1.6f, radius * 1.6f),
                    LeftToRight = 0.5f,
                },
            };

            Cache[key] = mesh;
            return mesh;
        }
    }
}


