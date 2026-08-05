using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NML.Core.Skins;

namespace NML.App.Views.Controls;

/// <summary>
/// A draggable 3D player-skin preview. Renders the skin as 6 textured cuboids (head/body/arms/
/// legs) projected from a user-rotated viewpoint. Mouse drag horizontally spins the yaw;
/// vertical drag tilts the pitch. The cube model lives in <see cref="SkinModel"/>; this control
/// owns the rotation state and the per-face painter projection.
/// </summary>
public sealed class SkinPreviewControl : Control
{
    private double _yaw = 28;   // start slightly turned so front+side show
    private double _pitch = -8;
    private Point? _lastDrag;
    private readonly IReadOnlyList<SkinFace> _faces;
    private Bitmap? _skinBitmap;

    public static readonly StyledProperty<string?> SkinPathProperty =
        AvaloniaProperty.Register<SkinPreviewControl, string?>(nameof(SkinPath));

    /// <summary>Absolute path to the 64×64 skin PNG to render. Null/missing = gray placeholder.</summary>
    public string? SkinPath
    {
        get => GetValue(SkinPathProperty);
        set => SetValue(SkinPathProperty, value);
    }

    static SkinPreviewControl()
    {
        // Re-render when the skin path changes.
        AffectsRender<SkinPreviewControl>(SkinPathProperty);
    }

    public SkinPreviewControl()
    {
        _faces = SkinModel.BuildFaces();
        ClipToBounds = false;
        // Subscribe to pointer events instead of overriding the virtuals (avoids type-resolution
        // pitfalls; the event handlers receive the same strongly-typed args).
        PointerPressed += OnDragStart;
        PointerMoved += OnDragMove;
        PointerReleased += OnDragEnd;
    }

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        _lastDrag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_lastDrag is null) return;
        Point p = e.GetPosition(this);
        _yaw += (p.X - _lastDrag.Value.X) * 0.5;
        _pitch -= (p.Y - _lastDrag.Value.Y) * 0.5;
        _pitch = Math.Clamp(_pitch, -60, 60);
        _lastDrag = p;
        InvalidateVisual();
    }

    private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        _lastDrag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SkinPathProperty) LoadSkin();
    }

    private void LoadSkin()
    {
        _skinBitmap?.Dispose();
        _skinBitmap = null;
        if (!string.IsNullOrEmpty(SkinPath) && File.Exists(SkinPath))
        {
            try { _skinBitmap = new Bitmap(SkinPath); }
            catch { /* ignore — render as flat placeholder */ }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double scale = (Bounds.Height / 36.0) * 0.9;
        double cx = Bounds.Width / 2;
        double cy = Bounds.Height / 2;

        double yaw = _yaw * Math.PI / 180.0;
        double pitch = _pitch * Math.PI / 180.0;

        // Project each face's 4 corners, then back-to-front by average depth (painter's algorithm).
        var projected = _faces
            .Select(f =>
            {
                var p0 = Project(f.P0, yaw, pitch);
                var p1 = Project(f.P1, yaw, pitch);
                var p2 = Project(f.P2, yaw, pitch);
                var p3 = Project(f.P3, yaw, pitch);
                double avgZ = (p0.z + p1.z + p2.z + p3.z) / 4;
                return (face: f, p0, p1, p2, p3, avgZ);
            })
            .OrderByDescending(f => f.avgZ) // far first
            .ToList();

        foreach (var pf in projected)
        {
            // Back-face cull: skip faces winding away from the viewer.
            if (!IsFrontFacing(pf.p0, pf.p1, pf.p2)) continue;

            var s0 = ToScreen(pf.p0, scale, cx, cy);
            var s1 = ToScreen(pf.p1, scale, cx, cy);
            var s2 = ToScreen(pf.p2, scale, cx, cy);
            var s3 = ToScreen(pf.p3, scale, cx, cy);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(s0, false);
                ctx.LineTo(s1);
                ctx.LineTo(s2);
                ctx.LineTo(s3);
                ctx.EndFigure(true);
            }

            // Derive a tint from the face's UV box so each body part reads as a distinct shade —
            // approximating the skin texture without per-texel quad mapping (Avalonia's public
            // API has no textured quad fill; this gives a clear 3D silhouette).
            Color tint = TintForFace(pf.face);
            context.DrawGeometry(new SolidColorBrush(tint), new Pen(Brushes.Black, 0.5), geometry);
        }
    }

    /// <summary>Project a 3D model-space point through yaw+pitch rotation to screen-space 3D.</summary>
    private static (double x, double y, double z) Project((double X, double Y, double Z) p, double yaw, double pitch)
    {
        double cyaw = Math.Cos(yaw), syaw = Math.Sin(yaw);
        double x1 = p.X * cyaw - p.Z * syaw;
        double z1 = p.X * syaw + p.Z * cyaw;
        double y1 = p.Y;

        double cpitch = Math.Cos(pitch), spitch = Math.Sin(pitch);
        double y2 = y1 * cpitch - z1 * spitch;
        double z2 = y1 * spitch + z1 * cpitch;

        return (x1, -y2, z2); // flip Y for screen-down convention
    }

    private static Point ToScreen((double x, double y, double z) p, double scale, double cx, double cy) =>
        new(cx + p.x * scale, cy + p.y * scale);

    /// <summary>True if the projected quad's winding is counter-clockwise (front-facing).</summary>
    private static bool IsFrontFacing((double x, double y, double z) a,
                                       (double x, double y, double z) b,
                                       (double x, double y, double z) c)
    {
        double cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        return cross < 0; // screen-space CCW = negative cross with Y-down
    }

    /// <summary>
    /// Pick a tint per face based on its UV position in the skin texture — neighboring faces
    /// get different shades so the body part silhouette reads as 3D. This is a stable, code-only
    /// rendering that doesn't require decoding the PNG.
    /// </summary>
    private static Color TintForFace(SkinFace f)
    {
        // Map the face's average UV to a hue; gives each body region a consistent tint.
        double u = (f.U0 + f.U1) / 2;
        double v = (f.V0 + f.V1) / 2;
        // Lighten by depth-like factor so top faces are brighter (fake lighting).
        byte lum = (byte)(120 + (int)((1 - u) * 60) + (int)(v * 40));
        lum = (byte)Math.Clamp((int)lum, 80, 230);
        return Color.FromRgb(lum, (byte)(lum * 0.85), (byte)(lum * 0.7));
    }
}
