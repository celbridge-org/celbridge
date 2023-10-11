using Celbridge.ViewModels;
using System.Numerics;
using Windows.Foundation;

namespace Celbridge.Views
{
    public sealed partial class CelConnectionLine : UserControl
    {
        public CelConnectionLineViewModel ViewModel { get; private set; }

        public float Thickness { get; set; } = 60;
        public float Curvature { get; set; } = -0.1f;
        public Point Start { get; set; }
        public Point End { get; set; }

        public CelConnectionLine()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<CelConnectionLineViewModel>();
        }

        public void Update()
        {
            Point ToPoint(Vector2 v)
            {
                return new Point(v.X, v.Y);
            }

            // Todo: Persist UI elements and update their positions
            // For now we just recreate all elements on each update.

            float HalfThickness = Thickness * 0.5f;

            Vector2 start = new Vector2((float)Start.X, (float)Start.Y);
            Vector2 end = new Vector2((float)End.X, (float)End.Y);

            Vector2 startToEnd = end - start;
            float length = startToEnd.Length();
            float halfLength = length * 0.5f;
            Vector2 startToEndNormal = Vector2.Normalize(startToEnd);

            Vector2 PerpendicularVector(Vector2 vector2)
            {
                return new Vector2(vector2.Y, -vector2.X);
            }

            Vector2 center = start + (startToEnd * 0.5f);
            Vector2 basisY = startToEndNormal;
            Vector2 basisX = PerpendicularVector(startToEndNormal);

            Vector2 GetPoint(float x, float y)
            {
                return center + (basisX * x) + (basisY * y);
            }

            Vector2 a = GetPoint( HalfThickness, -halfLength);
            Vector2 b = GetPoint(-HalfThickness, -halfLength);
            Vector2 c = GetPoint(-HalfThickness,  halfLength);
            Vector2 d = GetPoint( HalfThickness,  halfLength);

            Vector2 cA = GetPoint( HalfThickness * Curvature, -halfLength);
            Vector2 cB = GetPoint(-HalfThickness * Curvature, -halfLength);
            Vector2 cC = GetPoint(-HalfThickness * Curvature,  halfLength);
            Vector2 cD = GetPoint( HalfThickness * Curvature,  halfLength);

            var segments = CelConnectionFigure.Segments;
            segments.Clear();

            CelConnectionFigure.StartPoint = ToPoint(a);
            
            var s0 = new LineSegment();
            s0.Point = ToPoint(b);

            var s1 = new BezierSegment();
            s1.Point1 = ToPoint(cB);
            s1.Point2 = ToPoint(cC);
            s1.Point3 = ToPoint(c);
                
            var s2 = new LineSegment();
            s2.Point = ToPoint(d);

            var s3 = new BezierSegment();
            s3.Point1 = ToPoint(cD);
            s3.Point2 = ToPoint(cA);
            s3.Point3 = ToPoint(a);

            segments.Add(s0);
            segments.Add(s1);
            segments.Add(s2);
            segments.Add(s3);
        }
    }
}
