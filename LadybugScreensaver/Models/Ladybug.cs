namespace LadybugScreensaver.Models;

public class Ladybug
{
    // Bézier path control points — regenerated each pass
    private PointF[] _controlPoints;
    private float _distanceTraveled = 0f;  // 0.0 → 1.0 progress
    private float _pixelsPerFrame = 2.5f; // linear speed in pixels per frame              
    private float _speed = 0.0015f;

    public PointF Position { get; private set; }
    public float RotationAngle { get; private set; }
    public List<TrailDot> Trail { get; } = new();

    private readonly int _screenW, _screenH;
    private readonly Image _sprite;
    private readonly int _spriteW = 144, _spriteH = 144; // was 48x48
    
    public bool IsOffscreen => Position.X > _screenW + _spriteW;
    
    private float[] _arcLengthTable;
    private float _totalLength;
    private const int ArcTableSamples = 200;
    
    private float _distanceSinceLastDot = 0f;
    private const float DotSpacing = 110f; // pixels between dots

    public Ladybug(int screenW, int screenH, Image sprite)
    {
        _screenW = screenW;
        _screenH = screenH;
        _sprite  = sprite;
        _speed   = 0.0008f + (float)new Random().NextDouble() * 0.001f;
        GeneratePath();
    }

    private void GeneratePath()
    {
        var rng = new Random();
        _distanceTraveled = 0f;

        float startY = rng.Next(50, _screenH - 50);
        float endY   = rng.Next(50, _screenH - 50);

        // Push control points to extremes for dramatic S-curves
        float cp1Y = rng.Next(0, _screenH);   // can go near very top or bottom
        float cp2Y = rng.Next(0, _screenH);

        // Optionally flip them to opposite sides for a strong S
        if (Math.Abs(cp1Y - cp2Y) < _screenH * 0.4f)
            cp2Y = _screenH - cp2Y;           // force them apart

        _controlPoints = new[]
        {
            new PointF(-_spriteW, startY),
            new PointF(_screenW * 0.25f, cp1Y),
            new PointF(_screenW * 0.75f, cp2Y),
            new PointF(_screenW + _spriteW, endY)
        };
        
        BuildArcLengthTable();
    }

    // Cubic Bézier interpolation across the 4 control points
    private PointF Bezier(float progress)
    {
        float inverse = 1 - progress;
        float progressSquared = progress * progress, inverseSquared = inverse * inverse;
        float inverseCubed = inverseSquared * inverse, progressCubed = progressSquared * progress;

        var pts = _controlPoints;
        float x = inverseCubed * pts[0].X + 3 * inverseSquared * progress * pts[1].X
                                          + 3 * inverse * progressSquared * pts[2].X + progressCubed * pts[3].X;
        float y = inverseCubed * pts[0].Y + 3 * inverseSquared * progress * pts[1].Y
                                          + 3 * inverse * progressSquared * pts[2].Y + progressCubed * pts[3].Y;
        return new PointF(x, y);
    }

    public void Update()
    {
        _distanceTraveled += _pixelsPerFrame;
        if (_distanceTraveled >= _totalLength) { GeneratePath(); return; }

        float t = ArcLengthToT(_distanceTraveled);
        Position = Bezier(t);

        float tAhead = ArcLengthToT(Math.Min(_distanceTraveled + 5f, _totalLength));
        PointF ahead = Bezier(tAhead);
        float dx = ahead.X - Position.X;
        float dy = ahead.Y - Position.Y;
        RotationAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90f;

        _distanceSinceLastDot += _pixelsPerFrame;
        if (_distanceSinceLastDot >= DotSpacing)
        {
            Trail.Add(new TrailDot(Position));
            _distanceSinceLastDot = 0f;
        }

        foreach (var dot in Trail) dot.Update();
        Trail.RemoveAll(d => d.IsDead);
    }

    public void Draw(Graphics g)
    {
        foreach (var dot in Trail) dot.Draw(g);

        var state = g.Save();
        g.TranslateTransform(Position.X, Position.Y);
        g.RotateTransform(RotationAngle);
        g.DrawImage(_sprite, -_spriteW / 2f, -_spriteH / 2f, _spriteW, _spriteH);
        g.Restore(state);
    }
    
    private void BuildArcLengthTable()
    {
        _arcLengthTable = new float[ArcTableSamples + 1];
        _arcLengthTable[0] = 0f;

        PointF prev = Bezier(0f);
        float cumulative = 0f;

        for (int i = 1; i <= ArcTableSamples; i++)
        {
            float t = i / (float)ArcTableSamples;
            PointF curr = Bezier(t);
            float dx = curr.X - prev.X;
            float dy = curr.Y - prev.Y;
            cumulative += (float)Math.Sqrt(dx * dx + dy * dy);
            _arcLengthTable[i] = cumulative;
            prev = curr;
        }

        _totalLength = cumulative;
    }
    
    private float ArcLengthToT(float distance)
    {
        if (distance <= 0) return 0f;
        if (distance >= _totalLength) return 1f;

        int lo = 0, hi = ArcTableSamples;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (_arcLengthTable[mid] < distance) lo = mid;
            else hi = mid;
        }

        float segStart = _arcLengthTable[lo];
        float segEnd   = _arcLengthTable[hi];
        float blend    = (distance - segStart) / (segEnd - segStart);
        return (lo + blend) / ArcTableSamples;
    }
}