using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.Actors;

/// <summary>
/// The shady merchant: a striped stall with a hooded seller and three items for
/// gold. Offers are rolled the first time the stall is opened (so luck at that
/// moment counts) and stay fixed; a bought slot is emptied for good.
/// </summary>
public sealed partial class Merchant : Station
{
    public static readonly Color MerchantTint = new(0.78f, 0.45f, 1.0f);

    public const int SlotCount = 3;

    /// <summary>Null until rolled; then one entry per slot, null once bought (or
    /// when the pool had nothing to put there).</summary>
    public Offer?[]? Offers { get; set; }

    /// <summary>Price per slot, fixed when the offers are rolled.</summary>
    public int[] Prices { get; } = new int[SlotCount];

    public override Color Tint => MerchantTint;
    public override string Verb => "trade with merchant";

    public override bool Usable
    {
        get
        {
            if (Offers is null) return true;
            foreach (Offer? o in Offers) if (o is not null) return true;
            return false;
        }
    }

    public override void _Ready()
    {
        Name = "Merchant";
        var wood = Flat(new Color(0.40f, 0.27f, 0.17f));
        var cloth = Flat(new Color(0.42f, 0.18f, 0.55f));
        var stripe = Flat(new Color(0.92f, 0.82f, 0.55f));

        // Counter, four posts, a two-tone awning: a stall reads as "shop" from
        // across the map where a lone figure reads as one more enemy.
        Part(new BoxMesh { Size = new Vector3(2.2f, 0.9f, 0.8f) }, new Vector3(0f, 0.45f, 0.55f), wood);
        Part(new BoxMesh { Size = new Vector3(2.3f, 0.06f, 0.9f) }, new Vector3(0f, 0.93f, 0.55f), cloth);
        foreach (float x in new[] { -1.05f, 1.05f })
            foreach (float z in new[] { 0.15f, -0.95f })
                Part(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 2.4f, RadialSegments = 6 },
                     new Vector3(x, 1.2f, z), wood);
        for (int i = 0; i < 5; i++)
        {
            float x = -0.96f + i * 0.48f;
            Part(new BoxMesh { Size = new Vector3(0.48f, 0.08f, 1.5f) }, new Vector3(x, 2.42f, -0.4f), i % 2 == 0 ? cloth : stripe);
        }

        // Wares on the counter: three little glowing jars in rarity colours.
        for (int i = 0; i < 3; i++)
            Part(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.11f, Height = 0.24f, RadialSegments = 8 },
                 new Vector3(-0.6f + i * 0.6f, 1.08f, 0.55f),
                 Glow(Catalog.RarityColor((Rarity)(i + 1)), 1.2f));

        // The seller, behind the counter.
        if (Model("res://assets/kenney/graveyard-kit/character-keeper.glb", 1.55f, new Vector3(0f, 0f, -0.35f), 0f) is null)
        {
            Part(new CapsuleMesh { Radius = 0.3f, Height = 1.5f }, new Vector3(0f, 0.75f, -0.35f), cloth);
            Part(new SphereMesh { Radius = 0.22f, Height = 0.44f }, new Vector3(0f, 1.6f, -0.35f), Flat(new Color(0.2f, 0.12f, 0.25f)));
        }

        BuildMarker(3.1f);
        Refresh();
    }
}
