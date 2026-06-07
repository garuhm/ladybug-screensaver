namespace LadybugScreensaver.Models;

public class Ladybug
{
    private PointF[] _controlPoints;
    private float _distanceTraveled  = 0f;
    private float _pixelsPerFrame    = 2.5f;

    public PointF Position      { get; private set; }
    public float  RotationAngle { get; private set; }
    public List<TrailDot> Trail { get; } = new();

    private readonly int   _screenW, _screenH;
    private readonly Image _sprite;
    private readonly int   _spriteW = 144, _spriteH = 144;

    public bool IsOffscreen => Position.X > _screenW + _spriteW;

    private float[] _arcLengthTable;
    private float   _totalLength;
    private const int ArcTableSamples = 200;

    private float _distanceSinceLastDot = 0f;
    private const float DotSpacing = 110f;

    public Ladybug(int screenW, int screenH, Image sprite)
    {
        _screenW = screenW;
        _screenH = screenH;
        _sprite  = sprite;
        GeneratePath();
    }

    private void GeneratePath()
    {
        var rng = new Random();
        _distanceTraveled    = 0f;
        _distanceSinceLastDot = 0f;

        float startY = rng.Next(50, _screenH - 50);
        float endY   = rng.Next(50, _screenH - 50);

        float cp1Y = rng.Next(0, _screenH);
        float cp2Y = rng.Next(0, _screenH);

        if (Math.Abs(cp1Y - cp2Y) < _screenH * 0.4f)
            cp2Y = _screenH - cp2Y;

        _controlPoints = new[]
        {
            new PointF(-_spriteW, startY),
            new PointF(_screenW * 0.25f, cp1Y),
            new PointF(_screenW * 0.75f, cp2Y),
            new PointF(_screenW + _spriteW, endY)
        };

        BuildArcLengthTable();
    }

    private PointF Bezier(float progress)
    {
        float inverse         = 1 - progress;
        float progressSquared = progress * progress;
        float inverseSquared  = inverse  * inverse;
        float inverseCubed    = inverseSquared * inverse;
        float progressCubed   = progressSquared * progress;

        var pts = _controlPoints;
        float x = inverseCubed * pts[0].X + 3 * inverseSquared * progress * pts[1].X
                                           + 3 * inverse * progressSquared * pts[2].X
                                           + progressCubed * pts[3].X;
        float y = inverseCubed * pts[0].Y + 3 * inverseSquared * progress * pts[1].Y
                                           + 3 * inverse * progressSquared * pts[2].Y
                                           + progressCubed * pts[3].Y;
        return new PointF(x, y);
    }

    private void BuildArcLengthTable()
    {
        _arcLengthTable    = new float[ArcTableSamples + 1];
        _arcLengthTable[0] = 0f;

        PointF previousPoint    = Bezier(0f);
        float  cumulativeLength = 0f;

        for (int i = 1; i <= ArcTableSamples; i++)
        {
            float  progress      = i / (float)ArcTableSamples;
            PointF currentPoint  = Bezier(progress);
            float  dx            = currentPoint.X - previousPoint.X;
            float  dy            = currentPoint.Y - previousPoint.Y;
            cumulativeLength    += (float)Math.Sqrt(dx * dx + dy * dy);
            _arcLengthTable[i]   = cumulativeLength;
            previousPoint        = currentPoint;
        }

        _totalLength = cumulativeLength;
    }

    private float ArcLengthToT(float distance)
    {
        if (distance <= 0)            return 0f;
        if (distance >= _totalLength) return 1f;

        int low = 0, high = ArcTableSamples;
        while (low < high - 1)
        {
            int mid = (low + high) / 2;
            if (_arcLengthTable[mid] < distance) low = mid;
            else high = mid;
        }

        float segmentStart = _arcLengthTable[low];
        float segmentEnd   = _arcLengthTable[high];
        float blend        = (distance - segmentStart) / (segmentEnd - segmentStart);
        return (low + blend) / ArcTableSamples;
    }

    public void Update()
    {
        _distanceTraveled += _pixelsPerFrame;
        if (_distanceTraveled >= _totalLength)
        {
            GeneratePath();
            return;
        }

        float  progress = ArcLengthToT(_distanceTraveled);
        Position = Bezier(progress);

        float  progressAhead = ArcLengthToT(Math.Min(_distanceTraveled + 5f, _totalLength));
        PointF ahead         = Bezier(progressAhead);
        float  dx            = ahead.X - Position.X;
        float  dy            = ahead.Y - Position.Y;
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

        var graphicsState = g.Save();
        g.TranslateTransform(Position.X, Position.Y);
        g.RotateTransform(RotationAngle);
        g.DrawImage(_sprite, -_spriteW / 2f, -_spriteH / 2f, _spriteW, _spriteH);
        g.Restore(graphicsState);
    }
}