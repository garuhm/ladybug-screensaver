using LadybugScreensaver.Models;

namespace LadybugScreensaver;

public class ScreensaverForm : Form
{
    private readonly List<Ladybug> _ladybugs = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Bitmap _backBuffer;
    private readonly Graphics _backBufferGraphics; // cached — not recreated every frame
    private readonly Image _sprite;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private static readonly Color BgColor = Color.FromArgb(198, 230, 188);

    private int _spawnCooldown = 60;
    private const int MinSpawnInterval = 180;
    private const int MaxSpawnInterval = 360;
    private readonly Random _rng = new();

    private readonly List<TrailDot> _orphanedDots = new();

    public ScreensaverForm()
    {
        try
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState     = FormWindowState.Maximized;
            DoubleBuffered  = true;
            ShowCursor(false);
            BackColor       = BgColor;
            TopMost         = true;

            _sprite = Image.FromFile("Assets/ladybug.png");

            var screen = Screen.PrimaryScreen!.Bounds;
            _backBuffer         = new Bitmap(screen.Width, screen.Height);
            _backBufferGraphics = Graphics.FromImage(_backBuffer);
            _backBufferGraphics.SmoothingMode =
                System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            _backBufferGraphics.CompositingQuality =
                System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            _backBufferGraphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.Low;

            _ladybugs.Add(new Ladybug(screen.Width, screen.Height, _sprite));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += OnTick;
            _timer.Start();

            KeyDown    += (_, _) => Application.Exit();
            MouseMove  += (_, _) => Application.Exit();
            MouseClick += (_, _) => Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show("CRASH: " + ex.Message + "\n\n" + ex.StackTrace);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _spawnCooldown--;
        if (_spawnCooldown <= 0)
        {
            _ladybugs.Add(new Ladybug(ClientSize.Width, ClientSize.Height, _sprite));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);
        }

        foreach (var bug in _ladybugs) bug.Update();

        var dead = _ladybugs.Where(b => b.IsFinished).ToList();
        foreach (var bug in dead)
        {
            _orphanedDots.AddRange(bug.Trail);
            bug.Trail.Clear();
            _ladybugs.Remove(bug);
        }

        foreach (var dot in _orphanedDots) dot.Update();
        _orphanedDots.RemoveAll(d => d.IsDead);

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_backBuffer == null) return;

        _backBufferGraphics.Clear(BgColor);

        foreach (var dot in _orphanedDots) dot.Draw(_backBufferGraphics);
        foreach (var bug in _ladybugs) bug.Draw(_backBufferGraphics);

        e.Graphics.DrawImage(_backBuffer, 0, 0);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _backBufferGraphics.Dispose();
        ShowCursor(true);
        base.OnFormClosed(e);
    }
}