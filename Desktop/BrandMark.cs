using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace SC.ScriptForge;

/// <summary>The Shmonov Studio SC tile from Assets/Brand/sc-scriptforge-mark.svg, rendered as native geometry on its navy plate.</summary>
public sealed class BrandMark : Border
{
    private const double Units = 1254;

    public BrandMark()
    {
        ClipToBounds = true;
        CornerRadius = new(9);
        // A faint inner edge keeps the navy plate distinct on the dark surfaces without showing on light ones.
        BorderThickness = new(1);
        BorderBrush = Brush.Parse("#1CFFFFFF");
        Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.48, 0.45, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.48, 0.45, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.82, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.82, RelativeUnit.Relative),
            GradientStops = { new(Color.Parse("#09121F"), 0), new(Color.Parse("#07101C"), 0.58), new(Color.Parse("#070D17"), 1) }
        };
        AutomationProperties.SetName(this, "SC — Shmonov Studio");

        var turquoise = Gradient("#23F4F3", "#25F5F0", "#20F1EB");
        var white = Gradient("#F8FAFA", "#F5F7F8", "#F1F4F5");
        var canvas = new Avalonia.Controls.Canvas { Width = Units, Height = Units };
        foreach (var (fill, data) in new[]
        {
            (turquoise, "M320 355H691C665 400 640 463 540 463H352A16 16 0 0 0 336 479V550A16 16 0 0 0 352 566H573A90 90 0 0 1 589 567.43H680V837.97A200 200 0 0 1 573 869H232.89A16 16 0 0 1 218.58 845.84L253.58 775.85A16 16 0 0 1 267.89 767H551A16 16 0 0 0 567 751V684A16 16 0 0 0 551 668H320A96 96 0 0 1 224 572V451A96 96 0 0 1 320 355Z"),
            (white, "M691 355H943L871 463H720A22 22 0 0 0 698 485V745A22 22 0 0 0 720 767H1021A16 16 0 0 1 1037 783V853A16 16 0 0 1 1021 869H573A90 90 0 0 0 663 779V656A90 90 0 0 0 589 567.43V479A16 16 0 0 0 573 463H540C593 463 609 355 691 355Z"),
            (turquoise, "M969 355H1025A12 12 0 0 1 1037 367V451A12 12 0 0 1 1025 463H897Z")
        })
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Fill = fill, Data = StreamGeometry.Parse(data) });
        Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
    }

    private static LinearGradientBrush Gradient(string start, string middle, string end) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new(Color.Parse(start), 0), new(Color.Parse(middle), 0.55), new(Color.Parse(end), 1) }
    };
}
