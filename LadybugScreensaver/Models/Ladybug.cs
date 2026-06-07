namespace LadybugScreensaver.Models;

public class Ladybug
{
    // Each path element is a cubic Bézier: [p0, cp1, cp2, p1]
    // p1 of segment N == p0 of segment N+1 (guaranteed)
    // tangent at join is continuous because cp2 of N, p1 of N, cp1 of N+1 are collinear
    private List<(PointF p0, PointF cp1, PointF cp2, PointF p1)> _beziers = new();

    private float[] _segmentEndDistances = Array.Empty<float>();
    private float   _totalLength;
    private const int SamplesPerBezier = 80;

    private float _distanceTraveled     = 0f;
    private float _distanceSinceLastDot = 0f;
    private readonly float _pixelsPerFrame = 6f;
    private const float DotSpacing = 110f;

    public PointF Position      { get; private set; }
    public float  RotationAngle { get; private set; }
    public bool   IsFinished    { get; private set; } = false;
    public List<TrailDot> Trail { get; } = new();

    private readonly int   _screenW, _screenH;
    private readonly Image _sprite;
    private readonly int   _spriteW = 144, _spriteH = 144;

    public Ladybug(int screenW, int screenH, Image sprite)
    {
        _screenW = screenW;
        _screenH = screenH;
        _sprite  = sprite;
        GeneratePath();
    }

    // --- Bézier math ---

    private static PointF EvaluateBezier(
        PointF p0, PointF cp1, PointF cp2, PointF p1, float progress)
    {
        float inverse        = 1f - progress;
        float inverseSquared = inverse  * inverse;
        float inverseCubed   = inverseSquared * inverse;
        float progressSquared = progress * progress;
        float progressCubed   = progressSquared * progress;

        return new PointF(
            inverseCubed   * p0.X  + 3f * inverseSquared * progress  * cp1.X
          + 3f * inverse   * progressSquared * cp2.X + progressCubed * p1.X,
            inverseCubed   * p0.Y  + 3f * inverseSquared * progress  * cp1.Y
          + 3f * inverse   * progressSquared * cp2.Y + progressCubed * p1.Y);
    }

    // Tangent direction at the end of a Bézier (p1 end)
    // = direction from cp2 to p1, normalized
    private static PointF BezierExitTangent(PointF cp2, PointF p1)
    {
        float dx  = p1.X - cp2.X;
        float dy  = p1.Y - cp2.Y;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        return len > 0 ? new PointF(dx / len, dy / len) : new PointF(1f, 0f);
    }

    // Given exit tangent and desired control point distance,
    // returns cp1 for the NEXT segment that is tangent-continuous
    private static PointF ContinuousEntryControlPoint(
        PointF segmentStart, PointF exitTangent, float controlDistance)
    {
        return new PointF(
            segmentStart.X + exitTangent.X * controlDistance,
            segmentStart.Y + exitTangent.Y * controlDistance);
    }

    // --- Arc length ---

    private void BuildArcTables()
    {
        _segmentEndDistances = new float[_beziers.Count];
        float cumulative = 0f;

        for (int bezierIndex = 0; bezierIndex < _beziers.Count; bezierIndex++)
        {
            var (p0, cp1, cp2, p1) = _beziers[bezierIndex];
            PointF previousPoint   = p0;

            for (int step = 1; step <= SamplesPerBezier; step++)
            {
                float   progress     = step / (float)SamplesPerBezier;
                PointF  currentPoint = EvaluateBezier(p0, cp1, cp2, p1, progress);
                float   dx           = currentPoint.X - previousPoint.X;
                float   dy           = currentPoint.Y - previousPoint.Y;
                cumulative          += (float)Math.Sqrt(dx * dx + dy * dy);
                previousPoint        = currentPoint;
            }

            _segmentEndDistances[bezierIndex] = cumulative;
        }

        _totalLength = cumulative;
    }

    private PointF SampleAtDistance(float targetDistance)
    {
        targetDistance = Math.Clamp(targetDistance, 0f, _totalLength);

        // Find which Bézier segment contains this distance
        float segmentStartDistance = 0f;
        for (int bezierIndex = 0; bezierIndex < _beziers.Count; bezierIndex++)
        {
            float segmentEndDistance = _segmentEndDistances[bezierIndex];

            if (targetDistance <= segmentEndDistance || bezierIndex == _beziers.Count - 1)
            {
                var (p0, cp1, cp2, p1) = _beziers[bezierIndex];
                float segmentLength    = segmentEndDistance - segmentStartDistance;
                if (segmentLength <= 0f) return p0;

                // Build a fine local arc table for this segment on the fly
                // so we can binary search with accurate distances
                float localTarget = targetDistance - segmentStartDistance;
                const int localSamples = 40;
                float[] localArc = new float[localSamples + 1];
                localArc[0] = 0f;
                PointF prev = p0;

                for (int i = 1; i <= localSamples; i++)
                {
                    float   sampleProgress = i / (float)localSamples;
                    PointF  curr           = EvaluateBezier(p0, cp1, cp2, p1, sampleProgress);
                    float   dx             = curr.X - prev.X;
                    float   dy             = curr.Y - prev.Y;
                    localArc[i]            = localArc[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy);
                    prev                   = curr;
                }

                // Binary search the local arc table for the exact progress value
                int lo = 0, hi = localSamples;
                while (lo < hi - 1)
                {
                    int mid = (lo + hi) / 2;
                    if (localArc[mid] < localTarget) lo = mid;
                    else hi = mid;
                }

                float blend         = (localArc[hi] - localArc[lo]) > 0f
                    ? (localTarget - localArc[lo]) / (localArc[hi] - localArc[lo])
                    : 0f;
                float localProgress = Math.Clamp((lo + blend) / localSamples, 0f, 1f);

                return EvaluateBezier(p0, cp1, cp2, p1, localProgress);
            }

            segmentStartDistance = segmentEndDistance;
        }

        return _beziers[^1].p1;
    }

    // --- Path generation ---

    private void GeneratePath()
    {
        var random = new Random();
        _distanceTraveled     = 0f;
        _distanceSinceLastDot = 0f;
        _beziers              = new List<(PointF, PointF, PointF, PointF)>();

        int startEdge = random.Next(0, 4);
        int endEdge;
        do { endEdge = random.Next(0, 4); } while (endEdge == startEdge);

        PointF currentPoint = RandomPointOnEdge(startEdge, random);
        PointF endPoint     = RandomPointOnEdge(endEdge, random);
        PointF exitTangent  = EdgeOutwardTangent(startEdge, random);

        int loopCount = random.Next(0, 3);
        int minRadius = (int)(Math.Min(_screenW, _screenH) * 0.08f);
        int maxRadius = (int)(Math.Min(_screenW, _screenH) * 0.17f);

        for (int loop = 0; loop < loopCount; loop++)
        {
            float loopRadius    = random.Next(minRadius, maxRadius);
            bool  loopAbove     = random.Next(0, 2) == 0;
            // +1 = loop curves above travel direction, -1 = below
            float loopDirection = loopAbove ? -1f : 1f;

            float straightDistance = random.Next(
                (int)(Math.Min(_screenW, _screenH) * 0.25f),
                (int)(Math.Min(_screenW, _screenH) * 0.45f));

            PointF loopEntryPoint = new PointF(
                currentPoint.X + exitTangent.X * straightDistance,
                currentPoint.Y + exitTangent.Y * straightDistance);

            // Approach segment — arrives at loopEntryPoint exactly along exitTangent
            float  approachControlDist = straightDistance * 0.4f;
            PointF approachCp1 = new PointF(
                currentPoint.X  + exitTangent.X * approachControlDist,
                currentPoint.Y  + exitTangent.Y * approachControlDist);
            PointF approachCp2 = new PointF(
                loopEntryPoint.X - exitTangent.X * approachControlDist,
                loopEntryPoint.Y - exitTangent.Y * approachControlDist);

            _beziers.Add((currentPoint, approachCp1, approachCp2, loopEntryPoint));

            // Loop center is offset perpendicular to exitTangent
            // perpendicular is rotated 90 degrees in the loop direction
            PointF perpendicular = new PointF(
                -loopDirection * exitTangent.Y,
                 loopDirection * exitTangent.X);

            PointF loopCenter = new PointF(
                loopEntryPoint.X + perpendicular.X * loopRadius,
                loopEntryPoint.Y + perpendicular.Y * loopRadius);

            // The circle tangent at the entry point must equal exitTangent.
            // On a circle, tangent = loopDirection * perpendicular rotated 90 = exitTangent.
            // We verify: entry is at angle = atan2(-perp.Y, -perp.X) from center.
            // Tangent there (CCW) = (-sin(angle), cos(angle)) * loopDirection.
            // With our perpendicular definition this works out to exitTangent exactly.
            float circleConstant = loopRadius * 0.5522848f;

            // 4 quarter points around the circle
            // entry angle = direction from loopCenter to loopEntryPoint
            float entryAngle = (float)Math.Atan2(
                loopEntryPoint.Y - loopCenter.Y,
                loopEntryPoint.X - loopCenter.X);

            PointF[] circlePoints = new PointF[5];
            circlePoints[0] = loopEntryPoint;
            circlePoints[4] = loopEntryPoint;
            for (int quarterIndex = 1; quarterIndex <= 3; quarterIndex++)
            {
                float pointAngle = entryAngle + loopDirection * quarterIndex * (float)Math.PI / 2f;
                circlePoints[quarterIndex] = new PointF(
                    loopCenter.X + loopRadius * (float)Math.Cos(pointAngle),
                    loopCenter.Y + loopRadius * (float)Math.Sin(pointAngle));
            }

            // Build 4 quarter-circle Bézier segments
            // At each circle point, the tangent = loopDirection * (-sin(angle), cos(angle))
            for (int quarterIndex = 0; quarterIndex < 4; quarterIndex++)
            {
                float angleAtStart = entryAngle
                                     + loopDirection * quarterIndex       * (float)Math.PI / 2f;
                float angleAtEnd   = entryAngle
                                     + loopDirection * (quarterIndex + 1) * (float)Math.PI / 2f;

                // Circle tangent at each point, in the direction of travel around the loop
                PointF tangentAtStart = new PointF(
                    loopDirection * -(float)Math.Sin(angleAtStart),
                    loopDirection *  (float)Math.Cos(angleAtStart));
                PointF tangentAtEnd = new PointF(
                    loopDirection * -(float)Math.Sin(angleAtEnd),
                    loopDirection *  (float)Math.Cos(angleAtEnd));

                // For quarterIndex == 0: tangentAtStart must equal exitTangent
                // If it doesn't, we override it to guarantee the smooth join
                PointF cp1Tangent = quarterIndex == 0 ? exitTangent : tangentAtStart;

                PointF quarterCp1 = new PointF(
                    circlePoints[quarterIndex].X     + cp1Tangent.X * circleConstant,
                    circlePoints[quarterIndex].Y     + cp1Tangent.Y * circleConstant);
                PointF quarterCp2 = new PointF(
                    circlePoints[quarterIndex + 1].X - tangentAtEnd.X * circleConstant,
                    circlePoints[quarterIndex + 1].Y - tangentAtEnd.Y * circleConstant);

                _beziers.Add((
                    circlePoints[quarterIndex],
                    quarterCp1,
                    quarterCp2,
                    circlePoints[quarterIndex + 1]));
            }

            currentPoint = loopEntryPoint;
            exitTangent  = BezierExitTangent(_beziers[^1].cp2, _beziers[^1].p1);
        }

        // Final segment to exit edge
        float  finalControlDist = 200f;
        PointF finalCp1 = new PointF(
            currentPoint.X + exitTangent.X * finalControlDist,
            currentPoint.Y + exitTangent.Y * finalControlDist);
        PointF exitInwardNormal = EdgeInwardNormal(endEdge);
        PointF finalCp2 = new PointF(
            endPoint.X + exitInwardNormal.X * finalControlDist,
            endPoint.Y + exitInwardNormal.Y * finalControlDist);

        _beziers.Add((currentPoint, finalCp1, finalCp2, endPoint));

        BuildArcTables();
    }

    // --- Edge helpers ---

    private PointF RandomPointOnEdge(int edge, Random random) => edge switch
    {
        0 => new PointF(random.Next(0, _screenW), -_spriteH),
        1 => new PointF(_screenW + _spriteW,       random.Next(0, _screenH)),
        2 => new PointF(random.Next(0, _screenW),  _screenH + _spriteH),
        _ => new PointF(-_spriteW,                 random.Next(0, _screenH))
    };

    private PointF EdgeOutwardTangent(int edge, Random random)
    {
        float wobble = (float)(random.NextDouble() * 0.4 - 0.2);
        PointF rawDirection = edge switch
        {
            0 => new PointF(wobble,  1f),
            1 => new PointF(-1f,     wobble),
            2 => new PointF(wobble, -1f),
            _ => new PointF(1f,      wobble)
        };
        float length = (float)Math.Sqrt(
            rawDirection.X * rawDirection.X + rawDirection.Y * rawDirection.Y);
        return new PointF(rawDirection.X / length, rawDirection.Y / length);
    }

    private PointF EdgeInwardNormal(int edge) => edge switch
    {
        0 => new PointF(0f,  1f),
        1 => new PointF(-1f, 0f),
        2 => new PointF(0f, -1f),
        _ => new PointF(1f,  0f)
    };

    // --- Update / Draw ---

    public void Update()
    {
        if (IsFinished) return;

        _distanceTraveled += _pixelsPerFrame;
        if (_distanceTraveled >= _totalLength)
        {
            IsFinished = true;
            return;
        }

        Position = SampleAtDistance(_distanceTraveled);

        PointF lookAheadPoint = SampleAtDistance(
            Math.Min(_distanceTraveled + 8f, _totalLength));
        float dx = lookAheadPoint.X - Position.X;
        float dy = lookAheadPoint.Y - Position.Y;
        RotationAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90f;

        _distanceSinceLastDot += _pixelsPerFrame;
        if (_distanceSinceLastDot >= DotSpacing)
        {
            Trail.Add(new TrailDot(Position));
            _distanceSinceLastDot = 0f;
        }

        foreach (var dot in Trail) dot.Update();
        Trail.RemoveAll(dot => dot.IsDead);
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