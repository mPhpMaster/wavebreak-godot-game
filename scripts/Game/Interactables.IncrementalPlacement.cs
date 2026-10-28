using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed partial class Interactables
{
    private int _placementStep, _stationCursor;
    private Vector3 _placementStart;
    private IEnumerator<int>? _totemPlacement;

    private void CancelPlacement()
    {
        _totemPlacement?.Dispose(); _totemPlacement = null;
        _placementStep = _stationCursor = 0;
    }

    private void StepExpeditionPlacements(PhysicsDirectSpaceState3D space)
    {
        _placeIn = 0;
        switch (_placementStep)
        {
            case 0:
                using (Prof.Scope("Place.obstacles")) CollectObstacles(_map!);
                _placementStart = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;
                _placementStep = 1;
                return;
            case 1:
                if (_stationCursor < _wantMerchants)
                {
                    using (Prof.Scope("Place.stations")) PlaceStation(space, new Merchant(), _placementStart, .2f, .7f);
                    _stationCursor++; return;
                }
                _stationCursor = 0; _placementStep = 2;
                goto case 2;
            case 2:
                if (_stationCursor < _wantMicrowaves)
                {
                    using (Prof.Scope("Place.stations")) PlaceStation(space, new Microwave(), _placementStart, .15f, .85f);
                    _stationCursor++; return;
                }
                _placementStep = 3;
                goto case 3;
            case 3:
                using (Prof.Scope("Place.pots")) PlacePots(space, _placementStart);
                _placementStep = 4;
                return;
            default:
                _totemPlacement ??= TotemPlacementSteps(space, _placementStart).GetEnumerator();
                using (Prof.Scope("Place.totems"))
                    if (_totemPlacement.MoveNext()) return;
                CancelPlacement(); _placeIn = -1;
                return;
        }
    }
}
