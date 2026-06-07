using LadybugScreensaver.Models;

namespace LadybugScreensaver;

public class ScreensaverForm : Form
{
    private readonly List<Ladybug> _ladybugs = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Bitmap _backBuffer;
    private readonly Image _sprite;
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private static readonly Color BgColor = Color.FromArgb(198, 230, 188); // pale green

    private int _spawnCooldown = 60; // frames until next spawn
    private const int MinSpawnInterval = 90;
    private const int MaxSpawnInterval = 300;
    private readonly Random _rng = new();
    
    private readonly List<TrailDot> _orphanedDots = new();
    
    public ScreensaverForm()
{
    try
    {
        MessageBox.Show("Step 1: form properties");
        FormBorderStyle = FormBorderStyle.None;
        WindowState     = FormWindowState.Maximized;
        DoubleBuffered  = true;
        ShowCursor(false);
        BackColor       = BgColor;

        MessageBox.Show("Step 2: loading sprite");
        _sprite = Image.FromFile("Assets/ladybug.png");

        MessageBox.Show("Step 3: backbuffer");
        var screen = Screen.PrimaryScreen!.Bounds;
        _backBuffer = new Bitmap(screen.Width, screen.Height);

        MessageBox.Show("Step 4: timer");
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
        _timer.Start();

        MessageBox.Show("Step 5: first ladybug");
        _ladybugs.Add(new Ladybug(screen.Width, screen.Height, _sprite));
        _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);

        MessageBox.Show("Step 6: event handlers — done");
        KeyDown    += (_, _) => Application.Exit();
        // MouseMove  += (_, _) => Application.Exit();
        MouseClick += (_, _) => Application.Exit();
    }
    catch (Exception ex)
    {
        MessageBox.Show("CRASH: " + ex.Message + "\n\n" + ex.StackTrace);
    }
}

    private void OnTick(object? sender, EventArgs e)
    {
        // Spawn new ladybug on interval
        _spawnCooldown--;
        if (_spawnCooldown <= 0)
        {
            _ladybugs.Add(new Ladybug(ClientSize.Width, ClientSize.Height, _sprite));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);
        }

        foreach (var bug in _ladybugs) bug.Update();

        // Remove ladybugs that have gone offscreen, but keep their orphaned trail dots alive
        // by moving dots to a separate graveyard list
        var dead = _ladybugs.Where(b => b.IsFinished).ToList();
        foreach (var bug in dead)
        {
            _orphanedDots.AddRange(bug.Trail);
            bug.Trail.Clear();
            _ladybugs.Remove(bug);
        }

        // Update orphaned dots
        foreach (var dot in _orphanedDots) dot.Update();
        _orphanedDots.RemoveAll(d => d.IsDead);

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_backBuffer == null) return;
    
        // Draw to back buffer, then blit — prevents flicker
        using var g = Graphics.FromImage(_backBuffer);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BgColor);

        foreach (var dot in _orphanedDots) dot.Draw(g);
        foreach (var bug in _ladybugs) bug.Draw(g);

        e.Graphics.DrawImage(_backBuffer, 0, 0);
    }
    
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ShowCursor(true);
        base.OnFormClosed(e);
    }
}