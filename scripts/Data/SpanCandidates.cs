using System;
using Godot;

namespace Wavebreak.Data;

internal ref struct SpanCandidates
{
    public readonly Span<Vector3> Points;
    private readonly Span<float> _scores;
    public int Count;
    public SpanCandidates(Span<Vector3> points, Span<float> scores) { Points = points; _scores = scores; Count = 0; }
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
