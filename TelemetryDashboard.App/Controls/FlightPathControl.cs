using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.App.Controls;

public sealed class FlightPathControl : FrameworkElement
{
    private INotifyCollectionChanged? _observedCollection;

    public static readonly DependencyProperty FlightPathProperty =
        DependencyProperty.Register(
            nameof(FlightPath),
            typeof(IEnumerable<FlightPathPoint>),
            typeof(FlightPathControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnFlightPathChanged));

    public static readonly DependencyProperty CurrentLatitudeProperty =
        DependencyProperty.Register(
            nameof(CurrentLatitude),
            typeof(double),
            typeof(FlightPathControl),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentLongitudeProperty =
        DependencyProperty.Register(
            nameof(CurrentLongitude),
            typeof(double),
            typeof(FlightPathControl),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HeadingDegreesProperty =
        DependencyProperty.Register(
            nameof(HeadingDegrees),
            typeof(double),
            typeof(FlightPathControl),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<FlightPathPoint>? FlightPath
    {
        get => (IEnumerable<FlightPathPoint>?)GetValue(FlightPathProperty);
        set => SetValue(FlightPathProperty, value);
    }

    public double CurrentLatitude
    {
        get => (double)GetValue(CurrentLatitudeProperty);
        set => SetValue(CurrentLatitudeProperty, value);
    }

    public double CurrentLongitude
    {
        get => (double)GetValue(CurrentLongitudeProperty);
        set => SetValue(CurrentLongitudeProperty, value);
    }

    public double HeadingDegrees
    {
        get => (double)GetValue(HeadingDegreesProperty);
        set => SetValue(HeadingDegreesProperty, value);
    }

    private static void OnFlightPathChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (FlightPathControl)d;

        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged -=
                control.OnCollectionChanged;
        }

        control._observedCollection =
            e.NewValue as INotifyCollectionChanged;

        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged +=
                control.OnCollectionChanged;
        }

        control.InvalidateVisual();
    }

    private void OnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var primaryGreen =
            (Brush)Application.Current.FindResource("PrimaryGreenBrush");

        var dimGreen =
            (Brush)Application.Current.FindResource("DimGreenBrush");

        var borderGreen =
            (Brush)Application.Current.FindResource("BorderGreenBrush");

        DrawGrid(dc, borderGreen);

        var points = FlightPath?.ToList()
            ?? new List<FlightPathPoint>();

        if (points.Count == 0)
            return;

        var projected =
        ProjectCoordinates(points);

        if (projected.Count == 0)
            return;

        var currentProjected =
            ProjectCoordinate(
                CurrentLatitude,
                CurrentLongitude,
                points[0].Latitude,
                points[0].Longitude);

        const double padding = 35;

        // Include both trail AND current aircraft position
        double minX = Math.Min(
            projected.Min(p => p.X),
            currentProjected.X);

        double maxX = Math.Max(
            projected.Max(p => p.X),
            currentProjected.X);

        double minY = Math.Min(
            projected.Min(p => p.Y),
            currentProjected.Y);

        double maxY = Math.Max(
            projected.Max(p => p.Y),
            currentProjected.Y);

        // Prevent extreme zooming when only a few samples exist.
        const double minimumVisibleRangeMeters = 150;

        double rangeX = Math.Max(
            maxX - minX,
            minimumVisibleRangeMeters);

        double rangeY = Math.Max(
            maxY - minY,
            minimumVisibleRangeMeters);

        double usableWidth =
            Math.Max(ActualWidth - padding * 2, 1);

        double usableHeight =
            Math.Max(ActualHeight - padding * 2, 1);

        double scale =
            Math.Min(
                usableWidth / rangeX,
                usableHeight / rangeY);

        Point ScreenPoint(Point projectedPoint)
        {
            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            return new Point(
                ActualWidth / 2 +
                    (projectedPoint.X - centerX) * scale,

                ActualHeight / 2 -
                    (projectedPoint.Y - centerY) * scale
            );
        }

        var pathPen =
            new Pen(primaryGreen, 1.5);

        for (int i = 1; i < projected.Count; i++)
        {
            dc.DrawLine(
                pathPen,
                ScreenPoint(projected[i - 1]),
                ScreenPoint(projected[i]));
        }

        var homePoint =
            ScreenPoint(projected[0]);

        DrawHomeMarker(
            dc,
            homePoint,
            dimGreen);

        var aircraftPoint =
            ScreenPoint(currentProjected);

        if (projected.Count > 0)
        {
            var lastTrailPoint =
                ScreenPoint(projected[^1]);

            dc.DrawLine(
                pathPen,
                lastTrailPoint,
                aircraftPoint);
        }

        DrawAircraft(
            dc,
            aircraftPoint,
            HeadingDegrees,
            primaryGreen);
    }

    private void DrawGrid(
        DrawingContext dc,
        Brush gridBrush)
    {
        var pen = new Pen(gridBrush, 0.5);

        const double spacing = 40;

        for (double x = 0; x < ActualWidth; x += spacing)
        {
            dc.DrawLine(
                pen,
                new Point(x, 0),
                new Point(x, ActualHeight));
        }

        for (double y = 0; y < ActualHeight; y += spacing)
        {
            dc.DrawLine(
                pen,
                new Point(0, y),
                new Point(ActualWidth, y));
        }
    }

    private static List<Point> ProjectCoordinates(
        IReadOnlyList<FlightPathPoint> points)
    {
        if (points.Count == 0)
            return new();

        double referenceLatitude =
            points[0].Latitude;

        double referenceLongitude =
            points[0].Longitude;

        return points
            .Select(p =>
                ProjectCoordinate(
                    p.Latitude,
                    p.Longitude,
                    referenceLatitude,
                    referenceLongitude))
            .ToList();
    }

    private static Point ProjectCoordinate(
        double latitude,
        double longitude,
        double referenceLatitude,
        double referenceLongitude)
    {
        const double metersPerDegreeLatitude = 110540;

        double metersPerDegreeLongitude =
            111320 *
            Math.Cos(
                referenceLatitude *
                Math.PI / 180.0);

        double x =
            (longitude - referenceLongitude) *
            metersPerDegreeLongitude;

        double y =
            (latitude - referenceLatitude) *
            metersPerDegreeLatitude;

        return new Point(x, y);
    }

    private static void DrawHomeMarker(
        DrawingContext dc,
        Point position,
        Brush brush)
    {
        dc.DrawEllipse(
            null,
            new Pen(brush, 1.5),
            position,
            7,
            7);

        dc.DrawLine(
            new Pen(brush, 1),
            new Point(position.X - 10, position.Y),
            new Point(position.X + 10, position.Y));

        dc.DrawLine(
            new Pen(brush, 1),
            new Point(position.X, position.Y - 10),
            new Point(position.X, position.Y + 10));
    }

    private static void DrawAircraft(
        DrawingContext dc,
        Point position,
        double heading,
        Brush brush)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(0, -12),
                true,
                true);

            context.LineTo(
                new Point(8, 9),
                true,
                false);

            context.LineTo(
                new Point(0, 5),
                true,
                false);

            context.LineTo(
                new Point(-8, 9),
                true,
                false);
        }

        geometry.Freeze();

        dc.PushTransform(
            new TranslateTransform(
                position.X,
                position.Y));

        dc.PushTransform(
            new RotateTransform(heading));

        dc.DrawGeometry(
            brush,
            new Pen(brush, 1),
            geometry);

        dc.Pop();
        dc.Pop();
    }
}