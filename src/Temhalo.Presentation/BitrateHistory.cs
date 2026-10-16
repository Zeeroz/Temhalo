namespace Temhalo.Presentation;

public sealed record BitratePoint(double Seconds, double InputMbps, double OutputMbps);

/// <summary>UI-owned, bounded 60-second history. Counter resets establish a baseline, never a spike.</summary>
public sealed class BitrateHistory
{
    private readonly List<BitratePoint> _points = [];
    private long _input, _output;
    private int _attempt = -1;
    private double _time = -1;
    public IReadOnlyList<BitratePoint> Points => _points;
    public double InputMbps { get; private set; }
    public double OutputMbps { get; private set; }

    public void Sample(double seconds, long input, long output, int attempt, bool running)
    {
        if (!running) { _time = -1; InputMbps = OutputMbps = 0; return; }
        if (_time < 0 || seconds < _time || _attempt != attempt || input < _input || output < _output)
        {
            if (_time < 0 || seconds < _time) _points.Clear();
            _time = seconds; _input = input; _output = output; _attempt = attempt;
            InputMbps = OutputMbps = 0;
            return;
        }
        double interval = seconds - _time;
        if (interval < .75) return; // Button clicks must not manufacture extra chart samples.
        InputMbps = (input - _input) * 8.0 / interval / 1_000_000;
        OutputMbps = (output - _output) * 8.0 / interval / 1_000_000;
        _points.Add(new(seconds, InputMbps, OutputMbps));
        _points.RemoveAll(point => point.Seconds < seconds - 60);
        while (_points.Count > 81) _points.RemoveAt(0);
        _time = seconds; _input = input; _output = output;
    }
}
