using RemoteHub.Controls;

namespace RemoteHub.Focus;

public sealed class QuickControlsWindow : Window
{
    public event Action<string>? ActionRequested;
    public QuickControlsWindow(Window owner)
    {
        Owner = owner; Title = "RemoteMachine quick controls"; Icon = owner.Icon;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        ShowInTaskbar = false; ShowActivated = false; Background = Brushes.Transparent; Width = 454; Height = 82;
        var border = new Border { Margin = new(10), Padding = new(6), BorderThickness = new(1), CornerRadius = new(11),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .16 } };
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "FieldBorder");
        var row = new Grid();
        var actions = new[] { ("Minimize", "Minimize"), ("Windowed", "Window"), ("Disconnect", "Power"), ("More", "More") };
        for (int index = 0; index < actions.Length; index++)
        {
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var (label, glyph) = actions[index];
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Icon { Kind = glyph, Width = 15, Height = 15, Margin = new(0, 0, 7, 0) });
            stack.Children.Add(new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = stack, Padding = new(7, 10, 7, 10), BorderThickness = new(0), Background = Brushes.Transparent };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => ActionRequested?.Invoke(label);
            Grid.SetColumn(button, index); row.Children.Add(button);
        }
        border.Child = row; Content = border;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; ActionRequested?.Invoke("Close"); } };
    }
}
