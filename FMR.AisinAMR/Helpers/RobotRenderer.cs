using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FMR.AisinAMR.Helpers
{
    public static class RobotRenderer
    {
        // Colors
        private static readonly SolidColorBrush BodyFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A3A5C"));
        private static readonly SolidColorBrush BodyStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
        private static readonly SolidColorBrush WheelFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
        private static readonly SolidColorBrush DirectionFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
        private static readonly SolidColorBrush LidarFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9100"));
        private static readonly SolidColorBrush CenterDotFill = new SolidColorBrush(Colors.White);

        // Robot URDF Specs (meters)
        private const double BodyLengthMeters = 0.30;
        private const double BodyWidthMeters = 0.20;
        private const double WheelDiameterMeters = 0.11;
        private const double WheelWidthMeters = 0.04;
        private const double WheelSeparationMeters = 0.24;

        public static void UpdateRobotPose(Canvas mapCanvas, double worldX, double worldY, double yawRad, double canvasW, double canvasH)
        {
            // Remove existing robot elements
            for (int i = mapCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (mapCanvas.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "robot")
                    mapCanvas.Children.RemoveAt(i);
            }

            if (MapCoordinateHelper.MapWidth == 0 || MapCoordinateHelper.Resolution <= 0) return;

            double scale = Math.Min(
                canvasW / (MapCoordinateHelper.MapWidth  * MapCoordinateHelper.Resolution),
                canvasH / (MapCoordinateHelper.MapHeight * MapCoordinateHelper.Resolution));

            Point center = MapCoordinateHelper.WorldToCanvas(worldX, worldY, canvasW, canvasH);

            // Robot dimensions in canvas pixels
            double bodyL  = 0.30 * scale;
            double bodyW  = 0.20 * scale;
            double wheelD = 0.11 * scale;
            double wheelW = 0.04 * scale;
            double wheelS = 0.24 * scale;
            double lidarD = Math.Max(8, 0.08 * scale);

            // Create a single Canvas container for all robot parts
            // Everything drawn relative to local (0,0) = robot center
            Canvas robotCanvas = new Canvas
            {
                Width  = bodyL * 3,  // generous size
                Height = bodyW * 3,
                Tag    = "robot",
                IsHitTestVisible = false
            };

            // Local center offset within robotCanvas
            double lx = robotCanvas.Width  / 2;
            double ly = robotCanvas.Height / 2;

            // 1. Body
            Rectangle body = new Rectangle
            {
                Width  = bodyL, Height = bodyW,
                Fill   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A3A5C")),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")),
                StrokeThickness = 2, Opacity = 0.9
            };
            Canvas.SetLeft(body, lx - bodyL / 2);
            Canvas.SetTop (body, ly - bodyW / 2);
            Panel.SetZIndex(body, 1);
            robotCanvas.Children.Add(body);

            // 2. Left wheel
            Rectangle lWheel = new Rectangle
            {
                Width = wheelD, Height = wheelW,
                Fill  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"))
            };
            Canvas.SetLeft(lWheel, lx - wheelD / 2);
            Canvas.SetTop (lWheel, ly - wheelS / 2 - wheelW / 2);
            Panel.SetZIndex(lWheel, 2);
            robotCanvas.Children.Add(lWheel);

            // 3. Right wheel
            Rectangle rWheel = new Rectangle
            {
                Width = wheelD, Height = wheelW,
                Fill  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"))
            };
            Canvas.SetLeft(rWheel, lx - wheelD / 2);
            Canvas.SetTop (rWheel, ly + wheelS / 2 - wheelW / 2);
            Panel.SetZIndex(rWheel, 2);
            robotCanvas.Children.Add(rWheel);

            // 4. Direction arrow (front of body)
            Polygon arrow = new Polygon
            {
                Points = new PointCollection(new[]
                {
                    new Point(lx + bodyL / 2,                ly - bodyW / 5),
                    new Point(lx + bodyL / 2 + bodyL * 0.2,  ly),
                    new Point(lx + bodyL / 2,                ly + bodyW / 5)
                }),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"))
            };
            Panel.SetZIndex(arrow, 3);
            robotCanvas.Children.Add(arrow);

            // 5. LiDAR (orange circle, slightly forward of center)
            Ellipse lidar = new Ellipse
            {
                Width = lidarD, Height = lidarD,
                Fill  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9100"))
            };
            Canvas.SetLeft(lidar, lx + bodyL * 0.1 - lidarD / 2);
            Canvas.SetTop (lidar, ly - lidarD / 2);
            Panel.SetZIndex(lidar, 4);
            robotCanvas.Children.Add(lidar);

            // 6. Center dot
            Ellipse dot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
            Canvas.SetLeft(dot, lx - 2);
            Canvas.SetTop (dot, ly - 2);
            Panel.SetZIndex(dot, 5);
            robotCanvas.Children.Add(dot);

            // Rotate entire robotCanvas around its local center
            // WPF angle clockwise, ROS yaw counter-clockwise → negate
            double angleDeg = -yawRad * (180.0 / Math.PI);
            robotCanvas.RenderTransform = new RotateTransform(angleDeg, lx, ly);

            // Position robotCanvas so its local center aligns with map center
            Canvas.SetLeft(robotCanvas, center.X - lx);
            Canvas.SetTop (robotCanvas, center.Y - ly);
            Panel.SetZIndex(robotCanvas, 10);

            mapCanvas.Children.Add(robotCanvas);
        }
    }
}
