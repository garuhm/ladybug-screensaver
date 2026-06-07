namespace LadybugScreensaver.Models;

// Represents a single dot in a ladybug's trail.
// Dots are spawned at fixed pixel intervals along the path and disappear
// instantly (no fading) after a fixed number of frames.
public class TrailDot
{
    public PointF Position;

    // Counts down each frame
    // dot is removed when it reaches zero
    public int Life = 600;

    // Radius in pixels
    // set at spawn time so preview mode can scale it down
    public int DotRadius = 10;

    public TrailDot(PointF position, int dotRadius = 10)
    {
        Position  = position;
        DotRadius = dotRadius;
    }

    public bool IsDead => Life <= 0;

    public void Update() => Life--;

    public void Draw(Graphics g)
    {
        using var brush = new SolidBrush(Color.Black);
        g.FillEllipse(brush,
            Position.X - DotRadius, Position.Y - DotRadius,
            DotRadius * 2, DotRadius * 2);
    }
}