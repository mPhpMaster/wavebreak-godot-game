using System;
using Godot;

namespace Wavebreak.Data;

internal ref struct SpanCandidates
{
    public readonly Span<Vector3> Points;
    private readonly Span<float> _scores;
    public int Count;
    public SpanCandidates() { Points = new Vector3[Game.CityRooftops.VisibilityBudget]; _scores = new float[Game.CityRooftops.VisibilityBudget]; Count = 0; }
    public void Insert(Vector3 point, float score)
    {
        int index = Count;
        if (index == Points.Length) { if (score >= _scores[index - 1]) return; index--; }
        else Count++;
        while (index > 0 && score < _scores[index - 1])
        { _scores[index] = _scores[index - 1]; Points[index] = Points[index - 1]; index--; }
        _scores[index] = score; Points[index] = point;
    }
}
