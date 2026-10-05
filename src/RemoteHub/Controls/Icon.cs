namespace RemoteHub.Controls;

public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("Monitor", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["Monitor"] = Shape("M3,4 L21,4 21,17 3,17Z M8,21 L16,21 M12,17 L12,21"),
        ["Terminal"] = Shape("M4,6 L10,12 4,18 M13,18 L21,18"),
        ["Settings"] = Shape("M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21"),
        ["Home"] = Shape("M3,10 L12,3 21,10 21,21 15,21 15,14 9,14 9,21 3,21Z"),
        ["Grid"] = Shape("M3,3 L10,3 10,10 3,10Z M14,3 L21,3 21,10 14,10Z M3,14 L10,14 10,21 3,21Z M14,14 L21,14 21,21 14,21Z"),
        ["Folder"] = Shape("M3,7 L3,3 10,3 12,6 21,6 21,21 3,21Z"),
        ["Star"] = Shape("M12,3 L14.8,8.8 21.2,9.7 16.6,14.2 17.7,20.5 12,17.5 6.3,20.5 7.4,14.2 2.8,9.7 9.2,8.8Z"),
        ["People"] = Shape("M9,4 A3,3 0 1 0 9,10 A3,3 0 1 0 9,4 M3,21 L3,18 A6,6 0 0 1 15,18 L15,21 M16,4 A3,3 0 0 1 16,10 M18,14 A5,5 0 0 1 21,18 L21,21"),
        ["Search"] = Shape("M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 M16,16 L21,21"),
        ["Plus"] = Shape("M12,5 L12,19 M5,12 L19,12"),
        ["Arrow"] = Shape("M4,12 L20,12 M14,6 L20,12 14,18"),
        ["Chevron"] = Shape("M9,5 L16,12 9,19"),
        ["Down"] = Shape("M6,9 L12,15 18,9"),
        ["Close"] = Shape("M6,6 L18,18 M18,6 L6,18"),
        ["Minimize"] = Shape("M5,12 L19,12"),
        ["Window"] = Shape("M3,4 L21,4 21,20 3,20Z M3,9 L21,9"),
        ["Expand"] = Shape("M8,3 L3,3 3,8 M16,3 L21,3 21,8 M3,16 L3,21 8,21 M16,21 L21,21 21,16"),
        ["Lock"] = Shape("M5,10 L19,10 19,21 5,21Z M8,10 L8,7 A4,4 0 0 1 16,7 L16,10 M12,14 L12,17"),
        ["Shield"] = Shape("M12,3 L20,6 20,12 Q20,17 12,21 Q4,17 4,12 L4,6Z M8,12 L11,15 16,9"),
        ["Clock"] = Shape("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,6 L12,12 16,14"),
        ["Help"] = Shape("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M9,8 A3,3 0 0 1 15,8 Q15,10 12,12 L12,13 M12,17 L12.1,17"),
        ["Power"] = Shape("M12,3 L12,12 M6,6 A9,9 0 1 0 18,6"),
        ["More"] = Shape("M5,12 L5.1,12 M12,12 L12.1,12 M19,12 L19.1,12"),
        ["Check"] = Shape("M5,12 L9,16 19,6"),
        ["EyeOff"] = Shape("M3,3 L21,21 M9,5 Q16,1 22,12 Q20,15 18,17 M6,6 Q3,9 2,12 Q7,23 17,18 M10,10 A3,3 0 0 0 14,14"),
        ["Server"] = Shape("M3,3 L21,3 21,10 3,10Z M3,14 L21,14 21,21 3,21Z M7,6 L8,6 M7,17 L8,17 M12,6 L17,6 M12,17 L17,17")
    };
    public Icon() { Width = 18; Height = 18; IsHitTestVisible = false; }
    private static Geometry Shape(string path) { var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!Shapes.TryGetValue(Kind, out var shape)) throw new InvalidOperationException($"Unknown icon: {Kind}");
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var pen = new Pen(Stroke, Kind == "More" ? 3 : 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, shape);
        dc.Pop();
    }
}
