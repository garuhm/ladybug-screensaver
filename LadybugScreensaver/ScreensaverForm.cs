using LadybugScreensaver.Models;

namespace LadybugScreensaver;

// Main screensaver form
// Handles rendering, spawning, and input dismissal.
// Supports two modes:
//   Fullscreen — activated by Windows on AFK or via /s argument
//   Preview — renders inside the small monitor in Screen Saver Settings via /p argument
public class ScreensaverForm : Form
{
    private readonly List<Ladybug> _ladybugs    = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Bitmap   _backBuffer;
    private readonly Graphics _backBufferGraphics; // cached to avoid per-frame allocation
    private readonly Image    _sprite;

    private float _renderScale  = 1f; // 1f for fullscreen, fraction for preview

    // Mouse tracking
    // Used to distinguish real movement from Windows cursor warps
    private Point _lastMousePosition = Point.Empty;
    private bool  _mousePositionSet  = false;

    // Input events are ignored for the first second after activation to prevent
    // Windows' own cursor movement and key events from immediately dismissing the screensaver
    private bool _startupComplete = false;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr parentHandle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out Rectangle rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    private static readonly Color StripeColorA = Color.FromArgb(0xC6, 0xE6, 0xBC);
    private static readonly Color StripeColorB = Color.FromArgb(0xCF, 0xE8, 0xCA);

    private int _spawnCooldown         = 60;
    private const int MinSpawnInterval = 180; // frames (~3 seconds at 60fps)
    private const int MaxSpawnInterval = 360; // frames (~6 seconds at 60fps)
    private readonly Random _rng = new();

    // Dots from bugs that have finished their path (kept alive until they expire naturally)
    private readonly List<TrailDot> _orphanedDots = new();

    // Fullscreen constructor for normal screensaver activation
    public ScreensaverForm()
    {
        try
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState     = FormWindowState.Maximized;
            DoubleBuffered  = true;
            ShowCursor(false);
            BackColor       = StripeColorA;
            TopMost         = true;
            _renderScale    = 1f;

            _sprite = Image.FromFile("Assets/ladybug.png");

            var screen = Screen.PrimaryScreen!.Bounds;
            _backBuffer         = new Bitmap(screen.Width, screen.Height);
            _backBufferGraphics = Graphics.FromImage(_backBuffer);
            _backBufferGraphics.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            _backBufferGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            _backBufferGraphics.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.Low;

            _ladybugs.Add(new Ladybug(screen.Width, screen.Height, _sprite));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);

            _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
            _timer.Tick += OnTick;
            _timer.Start();

            // Wait 1 second before monitoring input
            // Windows sends spurious mouse and key events during screensaver activation
            // that would otherwise dismiss it immediately
            var startupTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            startupTimer.Tick += (_, _) =>
            {
                _startupComplete = true;
                startupTimer.Stop();
                startupTimer.Dispose();
            };
            startupTimer.Start();

            KeyDown    += (_, _) => { if (_startupComplete) Application.Exit(); };
            MouseMove  += OnMouseMove;
            MouseClick += (_, _) => { if (_startupComplete) Application.Exit(); };
        }
        catch (Exception ex)
        {
            MessageBox.Show("CRASH: " + ex.Message + "\n\n" + ex.StackTrace);
        }
    }

    // Preview constructor
    // Renders inside the small monitor in Screen Saver Settings.
    // The form is parented to the preview window handle Windows provides via /p.
    public ScreensaverForm(IntPtr previewHandle)
    {
        try
        {
            GetClientRect(previewHandle, out Rectangle previewRect);

            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered  = true;
            BackColor       = StripeColorA;
            _renderScale    = (float)previewRect.Width / 1920f;

            Size = new Size(previewRect.Width, previewRect.Height);

            // Parent our form into the preview window so it renders inside it
            SetParent(Handle, previewHandle);

            // Mark as a child window so Windows composites it correctly inside the preview
            const int GWL_STYLE = -16;
            const int WS_CHILD  = 0x40000000;
            SetWindowLong(Handle, GWL_STYLE, GetWindowLong(Handle, GWL_STYLE) | WS_CHILD);

            // Set location after parenting so coordinates are relative to the preview window
            Location = new Point(0, 0);
            Size     = new Size(previewRect.Width, previewRect.Height);

            _sprite = Image.FromFile("Assets/ladybug.png");

            _backBuffer         = new Bitmap(previewRect.Width, previewRect.Height);
            _backBufferGraphics = Graphics.FromImage(_backBuffer);
            _backBufferGraphics.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            _backBufferGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            _backBufferGraphics.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.Low;

            _ladybugs.Add(new Ladybug(previewRect.Width, previewRect.Height, _sprite, _renderScale));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Preview CRASH: " + ex.Message + "\n\n" + ex.StackTrace);
        }
    }

    // Dismisses the screensaver on real mouse movement, ignoring:
    //   - Movement before startup delay completes
    //   - First movement event (used to establish baseline position)
    //   - Large position jumps caused by Windows warping the cursor on activation
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_startupComplete) return;

        if (!_mousePositionSet)
        {
            _lastMousePosition = e.Location;
            _mousePositionSet  = true;
            return;
        }

        int dx = Math.Abs(e.Location.X - _lastMousePosition.X);
        int dy = Math.Abs(e.Location.Y - _lastMousePosition.Y);

        int screenW = Screen.PrimaryScreen?.Bounds.Width  ?? 1920;
        int screenH = Screen.PrimaryScreen?.Bounds.Height ?? 1080;

        if (dx > screenW / 4 || dy > screenH / 4)
        {
            // Cursor warp detected
            // Updates position but doesn't dismiss
            _lastMousePosition = e.Location;
            return;
        }

        _lastMousePosition = e.Location;

        if (dx > 10 || dy > 10)
            Application.Exit();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _spawnCooldown--;
        if (_spawnCooldown <= 0)
        {
            _ladybugs.Add(new Ladybug(ClientSize.Width, ClientSize.Height, _sprite, _renderScale));
            _spawnCooldown = _rng.Next(MinSpawnInterval, MaxSpawnInterval);
        }

        foreach (var bug in _ladybugs) bug.Update();

        // Removed finished bugs by moving their remaining trail dots to the orphan list
        // so dots don't all vanish at once when the bug is removed
        var finished = _ladybugs.Where(b => b.IsFinished).ToList();
        foreach (var bug in finished)
        {
            _orphanedDots.AddRange(bug.Trail);
            bug.Trail.Clear();
            _ladybugs.Remove(bug);
        }

        foreach (var dot in _orphanedDots) dot.Update();
        _orphanedDots.RemoveAll(d => d.IsDead);

        Invalidate();
    }

    // Draws alternating vertical stripes as the background.
    // Stripe width scaled for preview mode
    private void DrawBackground(int width, int height, float scale = 1f)
    {
        int stripeWidth = Math.Max(1, (int)(40 * scale));
        using var brushA = new SolidBrush(StripeColorA);
        using var brushB = new SolidBrush(StripeColorB);

        for (int x = 0; x < width; x += stripeWidth)
        {
            bool isEvenStripe = (x / stripeWidth) % 2 == 0;
            _backBufferGraphics.FillRectangle(
                isEvenStripe ? brushA : brushB,
                x, 0, stripeWidth, height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_backBuffer == null) return;

        DrawBackground(_backBuffer.Width, _backBuffer.Height, _renderScale);

        // Draw all trail dots first, then all sprites on top
        // to ensure ladybugs are never obscured by another bug's trail
        foreach (var dot in _orphanedDots) dot.Draw(_backBufferGraphics);
        foreach (var bug in _ladybugs) bug.DrawTrail(_backBufferGraphics);
        foreach (var bug in _ladybugs) bug.DrawSprite(_backBufferGraphics);

        e.Graphics.DrawImage(_backBuffer, 0, 0);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _backBufferGraphics?.Dispose();
        ShowCursor(true);
        base.OnFormClosed(e);
    }
}
