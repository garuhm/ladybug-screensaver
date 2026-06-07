namespace LadybugScreensaver.Models;

public class Ladybug
{
    // Each path element is a cubic Bézier: [p0, cp1, cp2, p1]
    // p1 of segment N == p0 of segment N+1 (guaranteed)
    // tangent at join is continuous because cp2 of N, p1 of N, cp1 of N+1 are collinear
    private List<(PointF p0, PointF cp1, PointF cp2, PointF p1)> _beziers = new();

    private float[] _segmentEndDistances = Array.Empty<float>();
    private List<float[]> _segmentLocalArcTables = new();
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

    // --- Arc length ---

    private void BuildArcTables()
    {
        _segmentEndDistances  = new float[_beziers.Count];
        _segmentLocalArcTables = new List<float[]>();
        float cumulative = 0f;
        const int localSamples = 40;

        for (int bezierIndex = 0; bezierIndex < _beziers.Count; bezierIndex++)
        {
            var (p0, cp1, cp2, p1) = _beziers[bezierIndex];
            var localArc = new float[localSamples + 1];
            localArc[0] = 0f;
            PointF previousPoint = p0;

            for (int step = 1; step <= localSamples; step++)
            {
                float   progress     = step / (float)localSamples;
                PointF  currentPoint = EvaluateBezier(p0, cp1, cp2, p1, progress);
                float   dx           = currentPoint.X - previousPoint.X;
                float   dy           = currentPoint.Y - previousPoint.Y;
                float   stepLength   = (float)Math.Sqrt(dx * dx + dy * dy);
                localArc[step]       = localArc[step - 1] + stepLength;
                cumulative          += stepLength;
                previousPoint        = currentPoint;
            }

            _segmentLocalArcTables.Add(localArc);
            _segmentEndDistances[bezierIndex] = cumulative;
        }

        _totalLength = cumulative;
    }


    private PointF SampleAtDistance(float targetDistance)
    {
        targetDistance = Math.Clamp(targetDistance, 0f, _totalLength);

        float segmentStartDistance = 0f;
        for (int bezierIndex = 0; bezierIndex < _beziers.Count; bezierIndex++)
        {
            float segmentEndDistance = _segmentEndDistances[bezierIndex];

            if (targetDistance <= segmentEndDistance || bezierIndex == _beziers.Count - 1)
            {
                var (p0, cp1, cp2, p1) = _beziers[bezierIndex];
                float segmentLength    = segmentEndDistance - segmentStartDistance;
                if (segmentLength <= 0f) return p0;

                float   localTarget = targetDistance - segmentStartDistance;
                float[] localArc    = _segmentLocalArcTables[bezierIndex];
                int     localSamples = localArc.Length - 1;

                // Binary search the pre-built local arc table — no rebuilding
                int lo = 0, hi = localSamples;
                while (lo < hi - 1)
                {
                    int mid = (lo + hi) / 2;
                    if (localArc[mid] < localTarget) lo = mid;
                    else hi = mid;
                }

                float blend = (localArc[hi] - localArc[lo]) > 0f
                    ? (localTarget - localArc[lo]) / (localArc[hi] - localArc[lo])
                    : 0f;
                float localProgress = Math.Clamp(
                    (lo + blend) / localSamples, 0f, 1f);

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

        int loopCount          = random.Next(0, 3);
        int minRadius          = (int)(Math.Min(_screenW, _screenH) * 0.08f);
        int maxRadius          = (int)(Math.Min(_screenW, _screenH) * 0.17f);
        int curveSegmentCount  = random.Next(3, 6);
        int loopsRemaining     = loopCount;
        float baseStepDist     = Math.Min(_screenW, _screenH) * 0.35f;
        float headingAngle     = (float)Math.Atan2(exitTangent.Y, exitTangent.X);

        for (int segmentIndex = 0; segmentIndex < curveSegmentCount; segmentIndex++)
        {
            bool isLastSegment = segmentIndex == curveSegmentCount - 1;

            float maxTurnRadians = 1.1f;
            headingAngle += (float)(random.NextDouble() * maxTurnRadians * 2 - maxTurnRadians);

            PointF targetTangent = new PointF(
                (float)Math.Cos(headingAngle),
                (float)Math.Sin(headingAngle));

            PointF segmentEnd = isLastSegment
                ? endPoint
                : new PointF(
                    currentPoint.X + targetTangent.X * baseStepDist,
                    currentPoint.Y + targetTangent.Y * baseStepDist);

            float segmentDist = (float)Math.Sqrt(
                (segmentEnd.X - currentPoint.X) * (segmentEnd.X - currentPoint.X) +
                (segmentEnd.Y - currentPoint.Y) * (segmentEnd.Y - currentPoint.Y));
            if (segmentDist < 1f) segmentDist = 1f;

            float controlDist = segmentDist * 0.45f;

            PointF cp1 = new PointF(
                currentPoint.X + exitTangent.X * controlDist,
                currentPoint.Y + exitTangent.Y * controlDist);

            PointF cp2Perp = new PointF(-targetTangent.Y, targetTangent.X);
            float  cp2Bow  = segmentDist * (float)(random.NextDouble() * 0.5 + 0.25)
                             * (random.Next(0, 2) == 0 ? 1f : -1f);
            PointF cp2 = new PointF(
                segmentEnd.X - targetTangent.X * controlDist + cp2Perp.X * cp2Bow,
                segmentEnd.Y - targetTangent.Y * controlDist + cp2Perp.Y * cp2Bow);

            _beziers.Add((currentPoint, cp1, cp2, segmentEnd));

            exitTangent  = BezierExitTangent(cp2, segmentEnd);
            currentPoint = segmentEnd;

            bool insertLoop = loopsRemaining > 0
                              && !isLastSegment
                              && (random.Next(0, 2) == 0 || loopsRemaining >= curveSegmentCount - segmentIndex - 1);
            if (insertLoop)
            {
                loopsRemaining--;

                float loopRadius    = random.Next(minRadius, maxRadius);
                bool  loopAbove     = random.Next(0, 2) == 0;
                float loopDirection = loopAbove ? -1f : 1f;

                PointF loopPerpendicular = new PointF(
                    -loopDirection * exitTangent.Y,
                     loopDirection * exitTangent.X);

                // Oval: center is closer than loopRadius so loop is squashed perpendicularly
                float ovalHeight = loopRadius * 0.8f;
                PointF loopCenter = new PointF(
                    currentPoint.X + loopPerpendicular.X * ovalHeight,
                    currentPoint.Y + loopPerpendicular.Y * ovalHeight);

                // Reduced circleConstant for oval shape
                float circleConstant = loopRadius * 0.5522848f * 0.8f;

                float entryAngle = (float)Math.Atan2(
                    currentPoint.Y - loopCenter.Y,
                    currentPoint.X - loopCenter.X);

                PointF[] circlePoints = new PointF[5];
                circlePoints[0] = currentPoint;
                circlePoints[4] = currentPoint;
                for (int quarterIndex = 1; quarterIndex <= 3; quarterIndex++)
                {
                    float pointAngle = entryAngle
                                       + loopDirection * quarterIndex * (float)Math.PI / 2f;

                    float rawX = loopRadius * (float)Math.Cos(pointAngle);
                    float rawY = loopRadius * (float)Math.Sin(pointAngle);

                    // Project onto travel and perpendicular axes, scale differently
                    // full loopRadius along travel = wide, 0.6x along perp = squashed
                    float alongTravel = rawX * exitTangent.X       + rawY * exitTangent.Y;
                    float alongPerp   = rawX * loopPerpendicular.X + rawY * loopPerpendicular.Y;

                    circlePoints[quarterIndex] = new PointF(
                        loopCenter.X + alongTravel * exitTangent.X       * 0.6f + alongPerp * loopPerpendicular.X,
                        loopCenter.Y + alongTravel * exitTangent.Y       * 0.6f + alongPerp * loopPerpendicular.Y);
                }

                for (int quarterIndex = 0; quarterIndex < 4; quarterIndex++)
                {
                    float angleAtStart = entryAngle
                                         + loopDirection * quarterIndex       * (float)Math.PI / 2f;
                    float angleAtEnd   = entryAngle
                                         + loopDirection * (quarterIndex + 1) * (float)Math.PI / 2f;

                    PointF tangentAtStart = new PointF(
                        loopDirection * -(float)Math.Sin(angleAtStart),
                        loopDirection *  (float)Math.Cos(angleAtStart));
                    PointF tangentAtEnd = new PointF(
                        loopDirection * -(float)Math.Sin(angleAtEnd),
                        loopDirection *  (float)Math.Cos(angleAtEnd));

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

                exitTangent  = BezierExitTangent(_beziers[^1].cp2, _beziers[^1].p1);
                headingAngle = (float)Math.Atan2(exitTangent.Y, exitTangent.X);
                currentPoint = circlePoints[4];
            }
        }

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