namespace RemoteHub.Controls;

public sealed class Wallpaper : FrameworkElement
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(string), typeof(Wallpaper),
        new FrameworkPropertyMetadata("Blue", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    private static readonly Geometry Back = Geometry.Parse("M145,377 C75,242 231,168 286,56 S356,-59 483,-4 C349,32 387,170 296,226 S157,335 210,379Z");
    private static readonly Geometry Middle = Geometry.Parse("M274,371 C161,269 314,201 358,90 S423,-65 526,-32 C412,33 464,100 395,207 S225,300 283,378Z");
    private static readonly Geometry Front = Geometry.Parse("M364,367 C223,306 289,199 390,179 S531,111 553,35 C548,197 430,209 396,254 S390,335 447,367Z");
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        string[] colors = Tone switch
        {
            "Sand" => ["#EAE5DC", "#CFB797", "#EEE3CD", "#9CAAB1", "#F6F2E9"],
            "Mint" => ["#BDDAD2", "#328B78", "#D7EDE3", "#075B58", "#D3E4DC"],
            _ => ["#C3DCFA", "#3279D9", "#A7D7FC", "#123898", "#E6F4FF"]
        };
        Color C(int index) => (Color)ColorConverter.ConvertFromString(colors[index]);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        double scale = Math.Max(ActualWidth / 600, ActualHeight / 330);
        dc.PushTransform(new TranslateTransform((ActualWidth - 600 * scale) / 2, (ActualHeight - 330 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRectangle(new LinearGradientBrush(C(0), C(4), 45), null, new Rect(0, 0, 600, 330));
        var fold = new LinearGradientBrush { StartPoint = new Point(.2, 0), EndPoint = new Point(.8, 1),
            GradientStops = [new(C(2), 0), new(C(1), .46), new(C(3), 1)] };
        dc.DrawGeometry(fold, null, Back);
        dc.DrawGeometry(new LinearGradientBrush(C(2), C(3), 65), null, Middle);
        dc.DrawGeometry(fold, null, Front);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
