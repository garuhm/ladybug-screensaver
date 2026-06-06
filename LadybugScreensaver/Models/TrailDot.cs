namespace LadybugScreensaver.Models;

public class TrailDot
{
    public PointF Position;
    public int Life = 600;          // frames before disappearing
    public const int DotRadius = 10; // was 4
    
    public TrailDot(PointF position)
    {
        Position = position;
    }

    public bool IsDead => Life <= 0;

    public void Update() => Life--;

    public void Draw(Graphics g)
    {
        using var brush = new SolidBrush(Color.Black);
        g.FillEllipse(brush, Position.X - DotRadius, Position.Y - DotRadius, 
            DotRadius * 2, DotRadius * 2);
    }
}