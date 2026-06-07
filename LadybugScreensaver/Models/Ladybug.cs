namespace LadybugScreensaver.Models;

// Represents a single ladybug that travels across the screen along a 
// generated curved path made of chained cubic Bézier segments. The path is tangent-continuous
// at every join (to prevent sharp corners) and may include oval loops.

// Arc-length parameterization ensures the bug moves at a constant pixel speed regardless of
// how curved the path is.
public class Ladybug
{
    // Path stored as a list of cubic Bézier segments.
    // Each segment is (p0, cp1, cp2, p1) where p1 of segment N == p0 of segment N+1.
    // Tangent continuity is guaranteed because cp2[N], p1[N], cp1[N+1] are collinear.
    private List<(PointF p0, PointF cp1, PointF cp2, PointF p1)> _beziers = new();

    // Cumulative arc distance at the end of each Bézier segment.
    // Used to quickly find which segment contains a given travel distance.
    private float[] _segmentEndDistances = Array.Empty<float>();

    // Per-segment arc length lookup tables. Pre-built so SampleAtDistance
    // never has to recompute them at runtime, which would cause lag.
    private List<float[]> _segmentLocalArcTables = new();

    private float _totalLength;
    private const int SamplesPerBezier = 80; // samples used when building each arc table

    private float _distanceTraveled;      // total pixels traveled along the path so far
    private float _distanceSinceLastDot;  // pixels since the last trail dot was placed
    private float _pixelsPerFrame;        // movement speed (scaled for preview mode)
    private float _dotSpacing;            // pixel gap between trail dots (scaled for preview mode)
    private int   _dotRadius;             // trail dot size (scaled for preview mode)

    public PointF Position      { get; private set; }
    public float  RotationAngle { get; private set; } // degrees, used for sprite rotation
    public bool   IsFinished    { get; private set; } = false;
    public List<TrailDot> Trail { get; } = new();

    private readonly int   _screenW, _screenH;
    private readonly Image _sprite;
    private int _spriteW; // sprite draw width (scaled for preview mode)
    private int _spriteH; // sprite draw height (scaled for preview mode)

    // scale = 1f for fullscreen, < 1f for the small preview window
    public Ladybug(int screenW, int screenH, Image sprite, float scale = 1f)
    {
        _screenW        = screenW;
        _screenH        = screenH;
        _sprite         = sprite;
        _pixelsPerFrame = 6f    * scale;
        _spriteW        = (int)(144f * scale);
        _spriteH        = (int)(144f * scale);
        _dotSpacing     = 110f  * scale;
        _dotRadius      = Math.Max(1, (int)(7f * scale));
        GeneratePath();
    }

    // --- Bézier math ---

    // Evaluates a cubic Bézier at a given progress value (0–1).
    private static PointF EvaluateBezier(
        PointF p0, PointF cp1, PointF cp2, PointF p1, float progress)
    {
        float inverse         = 1f - progress;
        float inverseSquared  = inverse   * inverse;
        float inverseCubed    = inverseSquared * inverse;
        float progressSquared = progress  * progress;
        float progressCubed   = progressSquared * progress;

        return new PointF(
            inverseCubed  * p0.X + 3f * inverseSquared * progress * cp1.X
          + 3f * inverse  * progressSquared * cp2.X + progressCubed * p1.X,
            inverseCubed  * p0.Y + 3f * inverseSquared * progress * cp1.Y
          + 3f * inverse  * progressSquared * cp2.Y + progressCubed * p1.Y);
    }

    // Returns the normalized tangent direction at the end of a Bézier segment.
    // The exit tangent is the direction from cp2 to p1.
    // This ensures smooth joins when cp1 of the next segment mirrors this direction.
    private static PointF BezierExitTangent(PointF cp2, PointF p1)
    {
        float dx  = p1.X - cp2.X;
        float dy  = p1.Y - cp2.Y;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        return len > 0 ? new PointF(dx / len, dy / len) : new PointF(1f, 0f);
    }

    // --- Arc length tables ---

    // Pre-builds a local arc length table for each Bézier segment.
    // Each table maps sample indices to cumulative pixel distances within that segment,
    // allowing SampleAtDistance to binary search for the exact position without
    // recomputing anything at runtime.
    private void BuildArcTables()
    {
        _segmentEndDistances   = new float[_beziers.Count];
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
                float  progress     = step / (float)localSamples;
                PointF currentPoint = EvaluateBezier(p0, cp1, cp2, p1, progress);
                float  dx           = currentPoint.X - previousPoint.X;
                float  dy           = currentPoint.Y - previousPoint.Y;
                float  stepLength   = (float)Math.Sqrt(dx * dx + dy * dy);
                localArc[step]      = localArc[step - 1] + stepLength;
                cumulative         += stepLength;
                previousPoint       = currentPoint;
            }

            _segmentLocalArcTables.Add(localArc);
            _segmentEndDistances[bezierIndex] = cumulative;
        }

        _totalLength = cumulative;
    }

    // Returns the world position at a given pixel distance along the full path.
    // Finds the right segment via _segmentEndDistances, then binary searches
    // that segment's pre-built arc table for the precise local progress value.
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

                float   localTarget  = targetDistance - segmentStartDistance;
                float[] localArc     = _segmentLocalArcTables[bezierIndex];
                int     localSamples = localArc.Length - 1;

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
                float localProgress = Math.Clamp((lo + blend) / localSamples, 0f, 1f);

                return EvaluateBezier(p0, cp1, cp2, p1, localProgress);
            }

            segmentStartDistance = segmentEndDistance;
        }

        return _beziers[^1].p1;
    }

    // --- Path generation ---

    // Generates a complete path.
    // Path composition:
    //   1. Curved Bézier segments with random heading evolution
    //   2. Zero, one, or two oval loops between curve segments (randomly)
    //   3. Continuation segments for the exit (after the loops), added
    //      until the path leaves the screen, at which point the final
    //      segment is clipped at the boundary
    private void GeneratePath()
    {
        var random = new Random();
        _distanceTraveled     = 0f;
        _distanceSinceLastDot = 0f;
        _beziers              = new List<(PointF, PointF, PointF, PointF)>();

        int startEdge = random.Next(0, 4);

        PointF currentPoint = RandomPointOnEdge(startEdge, random);
        PointF exitTangent  = EdgeOutwardTangent(startEdge, random);

        int loopCount         = random.Next(0, 3);
        int minRadius         = (int)(Math.Min(_screenW, _screenH) * 0.08f);
        int maxRadius         = (int)(Math.Min(_screenW, _screenH) * 0.17f);
        int curveSegmentCount = random.Next(3, 6);
        int loopsRemaining    = loopCount;
        float baseStepDist    = Math.Min(_screenW, _screenH) * 0.35f;

        // headingAngle drives the overall direction of travel and evolves gradually
        // to avoid corners
        float headingAngle = (float)Math.Atan2(exitTangent.Y, exitTangent.X);

        for (int segmentIndex = 0; segmentIndex < curveSegmentCount; segmentIndex++)
        {
            // Nudge heading by up to ~63 degrees
            headingAngle += (float)(random.NextDouble() * 1.1f * 2 - 1.1f);

            PointF targetTangent = new PointF(
                (float)Math.Cos(headingAngle),
                (float)Math.Sin(headingAngle));

            PointF segmentEnd = new PointF(
                currentPoint.X + targetTangent.X * baseStepDist,
                currentPoint.Y + targetTangent.Y * baseStepDist);

            float segmentDist = (float)Math.Sqrt(
                (segmentEnd.X - currentPoint.X) * (segmentEnd.X - currentPoint.X) +
                (segmentEnd.Y - currentPoint.Y) * (segmentEnd.Y - currentPoint.Y));
            if (segmentDist < 1f) segmentDist = 1f;

            float controlDist = segmentDist * 0.45f;

            // cp1 starts exactly along exitTangent (for continuity with previous segment)
            PointF cp1 = new PointF(
                currentPoint.X + exitTangent.X * controlDist,
                currentPoint.Y + exitTangent.Y * controlDist);

            // cp2 is bowed perpendicularly to targetTangent by a random amount.
            // Because cp1 and cp2 pull in different directions, each segment
            // has an S-curve shape
            PointF cp2Perp = new PointF(-targetTangent.Y, targetTangent.X);
            float  cp2Bow  = segmentDist * (float)(random.NextDouble() * 0.5 + 0.25)
                             * (random.Next(0, 2) == 0 ? 1f : -1f);
            PointF cp2 = new PointF(
                segmentEnd.X - targetTangent.X * controlDist + cp2Perp.X * cp2Bow,
                segmentEnd.Y - targetTangent.Y * controlDist + cp2Perp.Y * cp2Bow);

            _beziers.Add((currentPoint, cp1, cp2, segmentEnd));

            exitTangent  = BezierExitTangent(cp2, segmentEnd);
            currentPoint = segmentEnd;

            // Decide whether to insert a loop after this segment.
            // The second condition forces remaining loops to be placed if
            // running out of segments to place them in.
            bool insertLoop = loopsRemaining > 0
                              && (random.Next(0, 2) == 0
                                  || loopsRemaining >= curveSegmentCount - segmentIndex - 1);
            if (insertLoop)
            {
                loopsRemaining--;

                float loopRadius    = random.Next(minRadius, maxRadius);
                bool  loopAbove     = random.Next(0, 2) == 0;
                float loopDirection = loopAbove ? -1f : 1f;

                // Determines which side the loop bulges
                PointF loopPerpendicular = new PointF(
                    -loopDirection * exitTangent.Y,
                     loopDirection * exitTangent.X);

                // ovalHeight < loopRadius makes the loop taller than it is wide
                float  ovalHeight = loopRadius * 0.8f;
                PointF loopCenter = new PointF(
                    currentPoint.X + loopPerpendicular.X * ovalHeight,
                    currentPoint.Y + loopPerpendicular.Y * ovalHeight);

                // Standard Bézier circle approximation constant, reduced slightly for oval shape
                float circleConstant = loopRadius * 0.5522848f * 0.8f;

                // Entry angle = direction from loop center back to the entry point on the oval
                float entryAngle = (float)Math.Atan2(
                    currentPoint.Y - loopCenter.Y,
                    currentPoint.X - loopCenter.X);

                // 5 points define the oval
                // [0] and [4] are the entry/exit points respectively
                PointF[] circlePoints = new PointF[5];
                circlePoints[0] = currentPoint;
                circlePoints[4] = currentPoint;

                for (int quarterIndex = 1; quarterIndex <= 3; quarterIndex++)
                {
                    float pointAngle = entryAngle
                                       + loopDirection * quarterIndex * (float)Math.PI / 2f;

                    float rawX = loopRadius * (float)Math.Cos(pointAngle);
                    float rawY = loopRadius * (float)Math.Sin(pointAngle);

                    // Project raw circle point onto travel and perpendicular axes,
                    // then scale the travel axis by 0.6 to squash the loop into an oval
                    float alongTravel = rawX * exitTangent.X       + rawY * exitTangent.Y;
                    float alongPerp   = rawX * loopPerpendicular.X + rawY * loopPerpendicular.Y;

                    circlePoints[quarterIndex] = new PointF(
                        loopCenter.X + alongTravel * exitTangent.X      * 0.6f + alongPerp * loopPerpendicular.X,
                        loopCenter.Y + alongTravel * exitTangent.Y      * 0.6f + alongPerp * loopPerpendicular.Y);
                }

                // Build 4 quarter-arc Bézier segments approximating the oval.
                // Control points are derived from the circle tangent at each point.
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

                    // Force the first quarter's cp1 to use exitTangent rather than the
                    // geometric circle tangent to keep the path continuous
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

                // Update exitTangent and headingAngle from the loop's exit so the next
                // curve segment flows naturally from wherever the loop left off
                exitTangent  = BezierExitTangent(_beziers[^1].cp2, _beziers[^1].p1);
                headingAngle = (float)Math.Atan2(exitTangent.Y, exitTangent.X);
                currentPoint = circlePoints[4];
            }
        }

        // After the main segments and loops, keep adding continuation segments
        // with the same curving logic until the path naturally exits the screen.
        // The final segment is clipped at the exact screen boundary crossing point.
        bool exited = false;
        while (!exited)
        {
            headingAngle += (float)(random.NextDouble() * 0.8f * 2 - 0.8f);

            PointF continuationTangent = new PointF(
                (float)Math.Cos(headingAngle),
                (float)Math.Sin(headingAngle));

            PointF continuationEnd = new PointF(
                currentPoint.X + continuationTangent.X * baseStepDist * 0.6f,
                currentPoint.Y + continuationTangent.Y * baseStepDist * 0.6f);

            float continuationDist = (float)Math.Sqrt(
                (continuationEnd.X - currentPoint.X) * (continuationEnd.X - currentPoint.X) +
                (continuationEnd.Y - currentPoint.Y) * (continuationEnd.Y - currentPoint.Y));
            if (continuationDist < 1f) continuationDist = 1f;

            float continuationControlDist = continuationDist * 0.45f;

            PointF continuationCp1 = new PointF(
                currentPoint.X + exitTangent.X * continuationControlDist,
                currentPoint.Y + exitTangent.Y * continuationControlDist);

            PointF continuationCp2Perp = new PointF(-continuationTangent.Y, continuationTangent.X);
            float  continuationCp2Bow  = continuationDist * (float)(random.NextDouble() * 0.4 + 0.15)
                                         * (random.Next(0, 2) == 0 ? 1f : -1f);
            PointF continuationCp2 = new PointF(
                continuationEnd.X - continuationTangent.X * continuationControlDist
                                  + continuationCp2Perp.X * continuationCp2Bow,
                continuationEnd.Y - continuationTangent.Y * continuationControlDist
                                  + continuationCp2Perp.Y * continuationCp2Bow);

            float exitT = FindScreenExitT(currentPoint, continuationCp1, continuationCp2, continuationEnd);
            if (exitT < 1f)
            {
                // This segment crosses the screen boundary and gets
                // clipped at the crossing point. De Casteljau subdivision:
                // scale cp1 forward and cp2 backward by exit to get control points
                // for the clipped subsegment.
                PointF clippedEnd = EvaluateBezier(
                    currentPoint, continuationCp1, continuationCp2, continuationEnd, exitT);

                PointF clippedCp1 = new PointF(
                    currentPoint.X    + (continuationCp1.X - currentPoint.X)    * exitT,
                    currentPoint.Y    + (continuationCp1.Y - currentPoint.Y)    * exitT);
                PointF clippedCp2 = new PointF(
                    continuationCp2.X + (clippedEnd.X      - continuationCp2.X) * (1f - exitT),
                    continuationCp2.Y + (clippedEnd.Y      - continuationCp2.Y) * (1f - exitT));

                _beziers.Add((currentPoint, clippedCp1, clippedCp2, clippedEnd));
                exited = true;
            }
            else
            {
                _beziers.Add((currentPoint, continuationCp1, continuationCp2, continuationEnd));
                exitTangent  = BezierExitTangent(continuationCp2, continuationEnd);
                currentPoint = continuationEnd;
            }

            // Safety cap to prevent an infinite loop if the path never exits
            if (_beziers.Count > 30) exited = true;
        }

        BuildArcTables();
    }

    // --- Edge helpers ---

    // Returns a random point just outside the given screen edge,
    // so the bug starts off-screen and enters naturally.
    private PointF RandomPointOnEdge(int edge, Random random) => edge switch
    {
        0 => new PointF(random.Next(0, _screenW), -_spriteH),           // top
        1 => new PointF(_screenW + _spriteW,       random.Next(0, _screenH)), // right
        2 => new PointF(random.Next(0, _screenW),  _screenH + _spriteH),  // bottom
        _ => new PointF(-_spriteW,                 random.Next(0, _screenH))  // left
    };

    // Returns the inward-pointing tangent from the given edge,
    // with a small random wobble so paths aren't perfectly perpendicular to the edge.
    private PointF EdgeOutwardTangent(int edge, Random random)
    {
        float wobble = (float)(random.NextDouble() * 0.4 - 0.2);
        PointF rawDirection = edge switch
        {
            0 => new PointF(wobble,  1f),   // top — heading down
            1 => new PointF(-1f,     wobble), // right — heading left
            2 => new PointF(wobble, -1f),   // bottom — heading up
            _ => new PointF(1f,      wobble)  // left — heading right
        };
        float length = (float)Math.Sqrt(
            rawDirection.X * rawDirection.X + rawDirection.Y * rawDirection.Y);
        return new PointF(rawDirection.X / length, rawDirection.Y / length);
    }

    // Returns true if the point is outside the screen boundary
    // (accounting for sprite size so bugs fully exit before being retired)
    private bool IsOutsideScreen(PointF point)
    {
        return point.X < -_spriteW
            || point.X > _screenW + _spriteW
            || point.Y < -_spriteH
            || point.Y > _screenH + _spriteH;
    }

    // Binary searches for the t value (0–1) where this Bézier segment
    // first crosses outside the screen boundary.
    // Returns 1f if the segment stays entirely on screen.
    private float FindScreenExitT(PointF p0, PointF cp1, PointF cp2, PointF p1)
    {
        if (!IsOutsideScreen(p1)) return 1f;
        if (IsOutsideScreen(p0))  return 0f;

        float low = 0f, high = 1f;
        for (int iteration = 0; iteration < 16; iteration++)
        {
            float  mid      = (low + high) / 2f;
            PointF midPoint = EvaluateBezier(p0, cp1, cp2, p1, mid);
            if (IsOutsideScreen(midPoint)) high = mid;
            else                           low  = mid;
        }
        return (low + high) / 2f;
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

        // Look slightly ahead along the path to determine the bug's facing direction
        PointF lookAheadPoint = SampleAtDistance(
            Math.Min(_distanceTraveled + 8f, _totalLength));
        float dx = lookAheadPoint.X - Position.X;
        float dy = lookAheadPoint.Y - Position.Y;
        RotationAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90f;

        _distanceSinceLastDot += _pixelsPerFrame;
        if (_distanceSinceLastDot >= _dotSpacing)
        {
            Trail.Add(new TrailDot(Position, _dotRadius));
            _distanceSinceLastDot = 0f;
        }

        foreach (var dot in Trail) dot.Update();
        Trail.RemoveAll(dot => dot.IsDead);
    }

    // Draws trail dots. called before DrawSprite so dots always appear behind the bug
    public void DrawTrail(Graphics g)
    {
        foreach (var dot in Trail) dot.Draw(g);
    }

    // Draws the ladybug sprite, rotated to face its direction of travel
    public void DrawSprite(Graphics g)
    {
        var graphicsState = g.Save();
        g.TranslateTransform(Position.X, Position.Y);
        g.RotateTransform(RotationAngle);
        g.DrawImage(_sprite, -_spriteW / 2f, -_spriteH / 2f, _spriteW, _spriteH);
        g.Restore(graphicsState);
    }
}
