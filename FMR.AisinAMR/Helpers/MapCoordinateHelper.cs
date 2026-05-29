using System;
using System.Windows;

namespace FMR.AisinAMR.Helpers
{
    public static class MapCoordinateHelper
    {
        // Map metadata (updated dynamically from MainViewModel)
        public static double Resolution = 0.05;   // m/pixel
        public static double OriginX    = -0.691; // meter
        public static double OriginY    = -3.07;  // meter
        public static int    MapWidth   = 70;     // pixel
        public static int    MapHeight  = 79;     // pixel

        // World (meter, ROS frame) -> Canvas pixel
        public static Point WorldToCanvas(double wx, double wy, double canvasW, double canvasH)
        {
            if (MapWidth == 0 || MapHeight == 0 || Resolution <= 0) 
                return new Point(0, 0);

            double scale = Math.Min(canvasW / (MapWidth * Resolution),
                                    canvasH / (MapHeight * Resolution));
            
            // X matches
            double px = (wx - OriginX) * scale;
            
            // Y is flipped (WPF Y goes down, ROS Y goes up)
            double py = canvasH - (wy - OriginY) * scale;
            
            return new Point(px, py);
        }

        // Canvas pixel -> World (meter, ROS frame)
        public static (double wx, double wy) CanvasToWorld(double px, double py, double canvasW, double canvasH)
        {
            if (MapWidth == 0 || MapHeight == 0 || Resolution <= 0) 
                return (0, 0);

            double scale = Math.Min(canvasW / (MapWidth * Resolution),
                                    canvasH / (MapHeight * Resolution));
            
            double wx = (px / scale) + OriginX;
            double wy = ((canvasH - py) / scale) + OriginY;
            
            return (wx, wy);
        }
    }
}
