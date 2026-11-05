using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A circle with a filled wedge that shows how much of a download has arrived. The wedge starts at the top of the
/// circle and grows clockwise. Automation reads the meter as a progress bar, with the percentage that has arrived as
/// its value.
/// </summary>
public sealed partial class DownloadMeter : UserControl
{
    // The space between the inside of the circle's stroke and the wedge.
    private const double WedgeGap = 1.5;

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction),
        typeof(double),
        typeof(DownloadMeter),
        new PropertyMetadata(0.0, OnFractionChanged));

    public DownloadMeter()
    {
        InitializeComponent();

        SizeChanged += (sender, e) => UpdateWedge();
    }

    /// <summary>
    /// How much of the download has arrived, from 0 to 1.
    /// </summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new DownloadMeterAutomationPeer(this);
    }

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DownloadMeter)d).UpdateWedge();
    }

    private void UpdateWedge()
    {
        var diameter = Math.Min(ActualWidth, ActualHeight);
        var radius = diameter / 2 - MeterCircle.StrokeThickness - WedgeGap;
        var fraction = Math.Clamp(Fraction, 0, 1);
        if (radius <= 0 ||
            fraction <= 0)
        {
            MeterWedge.Data = null;
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (fraction >= 1)
        {
            MeterWedge.Data = new EllipseGeometry
            {
                Center = center,
                RadiusX = radius,
                RadiusY = radius
            };
            return;
        }

        // The angle runs clockwise from the top of the circle.
        var angle = fraction * 2 * Math.PI;
        var top = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

        var figure = new PathFigure
        {
            StartPoint = center,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment
        {
            Point = top
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        MeterWedge.Data = geometry;
    }
}

/// <summary>
/// Describes the download meter as a read-only progress bar. The value is the percentage of the download that has
/// arrived.
/// </summary>
internal sealed class DownloadMeterAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public DownloadMeterAutomationPeer(DownloadMeter owner)
        : base(owner)
    {
    }

    public double Value => Math.Clamp(((DownloadMeter)Owner).Fraction, 0, 1) * 100;

    public double Minimum => 0;

    public double Maximum => 100;

    public double SmallChange => 0;

    public double LargeChange => 0;

    public bool IsReadOnly => true;

    public void SetValue(double value)
    {
        throw new InvalidOperationException("The download meter is read-only");
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.ProgressBar;
    }

    protected override string GetClassNameCore()
    {
        return nameof(DownloadMeter);
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.RangeValue)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }
}
