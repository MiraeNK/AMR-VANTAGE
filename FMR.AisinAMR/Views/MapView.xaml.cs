using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using FMR.AisinAMR.ViewModels;
using FMR.AisinAMR.Models;
using FMR.AisinAMR.Helpers;

namespace FMR.AisinAMR.Views
{
    public enum MapMode { Normal, SetInitialPose, SetNavGoal }

    public partial class MapView : UserControl
    {
        private Point _panStartPoint;
        private bool _isPanning;
        
        // Interactive Mode
        private MapMode _currentMode = MapMode.Normal;
        private bool _isDraggingMode = false;
        private Point _dragStartCanvas;
        
        // Render state
        private DateTime _lastPoseUpdate = DateTime.MinValue;
        private DateTime _lastScanUpdate = DateTime.MinValue;
        private RobotStatus? _trackedRobot;

        public MapView()
        {
            InitializeComponent();
            this.DataContextChanged += MapView_DataContextChanged;
        }

        private void MapView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            }
            if (e.NewValue is MainViewModel newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
                TrackRobot(newVm.RobotSelected);
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.RobotSelected))
            {
                if (DataContext is MainViewModel vm)
                {
                    TrackRobot(vm.RobotSelected);
                }
            }
        }

        private void TrackRobot(RobotStatus? robot)
        {
            if (_trackedRobot != null)
            {
                _trackedRobot.PropertyChanged -= Robot_PropertyChanged;
            }
            
            _trackedRobot = robot;
            
            if (_trackedRobot != null)
            {
                _trackedRobot.PropertyChanged += Robot_PropertyChanged;
                // Force initial render
                UpdateRobotPoseThrottled();
                UpdateScanPointsThrottled();
            }
        }

        private void Robot_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RobotStatus.PoseX) || 
                e.PropertyName == nameof(RobotStatus.PoseY) || 
                e.PropertyName == nameof(RobotStatus.PoseYaw) ||
                e.PropertyName == nameof(RobotStatus.HasActiveGoal) ||
                e.PropertyName == nameof(RobotStatus.GoalX) ||
                e.PropertyName == nameof(RobotStatus.GoalY))
            {
                UpdateRobotPoseThrottled();
            }
            else if (e.PropertyName == nameof(RobotStatus.ScanRanges))
            {
                UpdateScanPointsThrottled();
            }
        }

        private void UpdateRobotPoseThrottled()
        {
            if ((DateTime.Now - _lastPoseUpdate).TotalMilliseconds < 100) return; // 10Hz throttle
            _lastPoseUpdate = DateTime.Now;

            if (_trackedRobot == null) return;

            Dispatcher.InvokeAsync(() =>
            {
                RobotRenderer.UpdateRobotPose(MapCanvas, _trackedRobot.PoseX, _trackedRobot.PoseY, _trackedRobot.PoseYaw * (Math.PI / 180.0), MapCanvas.Width, MapCanvas.Height);
                
                // Draw Nav Goal Marker
                for (int i = MapCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (MapCanvas.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "goal")
                    {
                        MapCanvas.Children.RemoveAt(i);
                    }
                }

                if (_trackedRobot.HasActiveGoal)
                {
                    Point goalCanvas = MapCoordinateHelper.WorldToCanvas(_trackedRobot.GoalX, _trackedRobot.GoalY, MapCanvas.Width, MapCanvas.Height);
                    
                    Ellipse goalCircle = new Ellipse
                    {
                        Width = 12, Height = 12,
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")),
                        StrokeThickness = 2,
                        Tag = "goal",
                        StrokeDashArray = new DoubleCollection(new double[] { 2, 2 })
                    };
                    Canvas.SetLeft(goalCircle, goalCanvas.X - 6);
                    Canvas.SetTop(goalCircle, goalCanvas.Y - 6);
                    Panel.SetZIndex(goalCircle, 50);
                    MapCanvas.Children.Add(goalCircle);

                    Line cross1 = new Line { X1 = goalCanvas.X - 4, Y1 = goalCanvas.Y - 4, X2 = goalCanvas.X + 4, Y2 = goalCanvas.Y + 4, Stroke = goalCircle.Stroke, StrokeThickness = 2, Tag = "goal" };
                    Line cross2 = new Line { X1 = goalCanvas.X - 4, Y1 = goalCanvas.Y + 4, X2 = goalCanvas.X + 4, Y2 = goalCanvas.Y - 4, Stroke = goalCircle.Stroke, StrokeThickness = 2, Tag = "goal" };
                    Panel.SetZIndex(cross1, 50);
                    Panel.SetZIndex(cross2, 50);
                    MapCanvas.Children.Add(cross1);
                    MapCanvas.Children.Add(cross2);
                }
            });
        }

        private void UpdateScanPointsThrottled()
        {
            if ((DateTime.Now - _lastScanUpdate).TotalMilliseconds < 200) return; // 5Hz throttle
            _lastScanUpdate = DateTime.Now;

            if (_trackedRobot == null) return;
            
            double[] ranges = _trackedRobot.ScanRanges;
            double angleMin = _trackedRobot.ScanAngleMin;
            double angleInc = _trackedRobot.ScanAngleInc;
            double rx = _trackedRobot.PoseX;
            double ry = _trackedRobot.PoseY;
            double ryaw = _trackedRobot.PoseYaw * (Math.PI / 180.0);

            if (ranges == null || ranges.Length == 0) return;

            Dispatcher.InvokeAsync(() =>
            {
                // Remove existing scan points
                for (int i = MapCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (MapCanvas.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "scan")
                    {
                        MapCanvas.Children.RemoveAt(i);
                    }
                }

                SolidColorBrush scanBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B2FF4444")); // Opacity 0.7

                for (int i = 0; i < ranges.Length; i++)
                {
                    double r = ranges[i];
                    if (double.IsNaN(r) || double.IsInfinity(r) || r < 0.05 || r > 10.0) continue;

                    double angle = angleMin + (i * angleInc) + ryaw;
                    double wx = rx + r * Math.Cos(angle);
                    double wy = ry + r * Math.Sin(angle);

                    Point cp = MapCoordinateHelper.WorldToCanvas(wx, wy, MapCanvas.Width, MapCanvas.Height);

                    Ellipse dot = new Ellipse
                    {
                        Width = 3,
                        Height = 3,
                        Fill = scanBrush,
                        Tag = "scan"
                    };
                    Canvas.SetLeft(dot, cp.X - 1.5);
                    Canvas.SetTop(dot, cp.Y - 1.5);
                    Panel.SetZIndex(dot, 5);
                    MapCanvas.Children.Add(dot);
                }
            });
        }

        // ──────────────────────────────────────────────────────────
        // UI INTERACTIONS
        // ──────────────────────────────────────────────────────────

        private void ToggleInitialPose_Checked(object sender, RoutedEventArgs e)
        {
            SetMode(MapMode.SetInitialPose);
            ToggleNavGoal.IsChecked = false;
        }

        private void ToggleNavGoal_Checked(object sender, RoutedEventArgs e)
        {
            SetMode(MapMode.SetNavGoal);
            ToggleInitialPose.IsChecked = false;
        }

        private void ToggleMode_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ToggleInitialPose.IsChecked == false && ToggleNavGoal.IsChecked == false)
            {
                SetMode(MapMode.Normal);
            }
        }

        private void SetMode(MapMode mode)
        {
            _currentMode = mode;
            if (mode == MapMode.Normal)
            {
                ModeIndicatorBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ModeIndicatorBorder.Visibility = Visibility.Visible;
                TxtModeIndicator.Text = mode == MapMode.SetInitialPose ? "MODE: Set Initial Pose" : "MODE: Set Nav Goal";
                TxtModeIndicator.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mode == MapMode.SetInitialPose ? "#00E676" : "#00E5FF"));
            }
            ClearDragVisuals();
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == MapMode.Normal) return;

            _isDraggingMode = true;
            _dragStartCanvas = e.GetPosition(MapCanvas);
            MapCanvas.CaptureMouse();
            
            DrawDragVisuals(_dragStartCanvas);
        }

        private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingMode)
            {
                DrawDragVisuals(e.GetPosition(MapCanvas));
            }
        }

        private async void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingMode) return;
            
            _isDraggingMode = false;
            MapCanvas.ReleaseMouseCapture();

            Point endCanvas = e.GetPosition(MapCanvas);
            var (wx, wy) = MapCoordinateHelper.CanvasToWorld(_dragStartCanvas.X, _dragStartCanvas.Y, MapCanvas.Width, MapCanvas.Height);
            
            // Calculate yaw from drag direction in Canvas space. 
            // WPF Canvas: Y goes down. So atan2(endY - startY, endX - startX) gives angle clockwise.
            // ROS Yaw: angle counter-clockwise.
            // But wait, if drag up on screen, endY < startY, dy is negative. atan2(neg, dx) is negative.
            // In ROS, UP is +Y, so we want positive yaw.
            // So we need to compute dy based on world coordinates.
            var (ewx, ewy) = MapCoordinateHelper.CanvasToWorld(endCanvas.X, endCanvas.Y, MapCanvas.Width, MapCanvas.Height);
            
            double dx = ewx - wx;
            double dy = ewy - wy;
            double yaw = Math.Atan2(dy, dx); // World space dy/dx gives correct ROS yaw

            ClearDragVisuals();

            if (DataContext is MainViewModel vm && _trackedRobot != null)
            {
                if (_currentMode == MapMode.SetInitialPose)
                {
                    var payload = new { x = wx, y = wy, yaw = yaw, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    // We need to access mqtt client, but it's private in VM. 
                    // Let's call a command or just trigger SetInitialPose if we pass params.
                    // Wait, we can just send it via reflection or add a method.
                    // Or we just update PoseX, PoseY, PoseYaw and call existing SetInitialPoseCommand?
                    // Better to just add a public method to MainViewModel.
                    _trackedRobot.PoseX = wx;
                    _trackedRobot.PoseY = wy;
                    _trackedRobot.PoseYaw = yaw * (180.0 / Math.PI);
                    if (vm.SetInitialPoseCommand.CanExecute(null))
                    {
                        vm.SetInitialPoseCommand.Execute(null);
                    }
                }
                else if (_currentMode == MapMode.SetNavGoal)
                {
                    _ = vm.SendNavGoalAtAsync(wx, wy, yaw);
                }
            }

            ToggleInitialPose.IsChecked = false;
            ToggleNavGoal.IsChecked = false;
            SetMode(MapMode.Normal);
        }

        private void DrawDragVisuals(Point currentPos)
        {
            ClearDragVisuals();

            string colorHex = _currentMode == MapMode.SetInitialPose ? "#00E676" : "#00E5FF";
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

            // Start circle
            Ellipse startCircle = new Ellipse { Width = 10, Height = 10, Stroke = brush, StrokeThickness = 2, Tag = "drag" };
            Canvas.SetLeft(startCircle, _dragStartCanvas.X - 5);
            Canvas.SetTop(startCircle, _dragStartCanvas.Y - 5);
            Panel.SetZIndex(startCircle, 100);
            MapCanvas.Children.Add(startCircle);

            // Line
            Line line = new Line
            {
                X1 = _dragStartCanvas.X, Y1 = _dragStartCanvas.Y,
                X2 = currentPos.X, Y2 = currentPos.Y,
                Stroke = brush, StrokeThickness = 2, Tag = "drag"
            };
            Panel.SetZIndex(line, 100);
            MapCanvas.Children.Add(line);

            // ArrowHead
            double dx = currentPos.X - _dragStartCanvas.X;
            double dy = currentPos.Y - _dragStartCanvas.Y;
            double angle = Math.Atan2(dy, dx);
            double arrowLen = 10;
            
            Polygon arrowhead = new Polygon
            {
                Points = new PointCollection(new[]
                {
                    currentPos,
                    new Point(currentPos.X - arrowLen * Math.Cos(angle - Math.PI / 6), currentPos.Y - arrowLen * Math.Sin(angle - Math.PI / 6)),
                    new Point(currentPos.X - arrowLen * Math.Cos(angle + Math.PI / 6), currentPos.Y - arrowLen * Math.Sin(angle + Math.PI / 6))
                }),
                Fill = brush, Tag = "drag"
            };
            Panel.SetZIndex(arrowhead, 100);
            MapCanvas.Children.Add(arrowhead);

            // Floating Label
            var (wx, wy) = MapCoordinateHelper.CanvasToWorld(_dragStartCanvas.X, _dragStartCanvas.Y, MapCanvas.Width, MapCanvas.Height);
            var (ewx, ewy) = MapCoordinateHelper.CanvasToWorld(currentPos.X, currentPos.Y, MapCanvas.Width, MapCanvas.Height);
            double yawRad = Math.Atan2(ewy - wy, ewx - wx);
            double yawDeg = yawRad * (180.0 / Math.PI);

            Border labelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Tag = "drag"
            };
            TextBlock labelTxt = new TextBlock
            {
                Text = $"X: {wx:F2}  Y: {wy:F2}  Yaw: {yawDeg:F0}°",
                Foreground = Brushes.White,
                FontSize = 10
            };
            labelBorder.Child = labelTxt;
            Canvas.SetLeft(labelBorder, currentPos.X + 10);
            Canvas.SetTop(labelBorder, currentPos.Y + 10);
            Panel.SetZIndex(labelBorder, 100);
            MapCanvas.Children.Add(labelBorder);
        }

        private void ClearDragVisuals()
        {
            for (int i = MapCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (MapCanvas.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "drag")
                {
                    MapCanvas.Children.RemoveAt(i);
                }
            }
        }

        private void LoadMap_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Map files|*.pgm;*.png;*.jpg;*.bmp|All files|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.MapFilePath = ofd.FileName;
                }
                
                try 
                {
                    BitmapSource? bitmap = null;
                    if (ofd.FileName.EndsWith(".pgm", StringComparison.OrdinalIgnoreCase))
                    {
                        bitmap = FMR.AisinAMR.Helpers.PgmLoader.LoadPgm(ofd.FileName);
                    }
                    else
                    {
                        bitmap = new BitmapImage(new Uri(ofd.FileName));
                    }

                    if (bitmap != null)
                    {
                        MapImage.Source = bitmap;
                        MapImage.Width  = MapCanvas.Width;
                        MapImage.Height = MapCanvas.Height;
                        TxtMapFileInfo.Text = $"{System.IO.Path.GetFileName(ofd.FileName)}\n{bitmap.PixelWidth}x{bitmap.PixelHeight} px";
                        
                        // Update Helper
                        MapCoordinateHelper.MapWidth = bitmap.PixelWidth;
                        MapCoordinateHelper.MapHeight = bitmap.PixelHeight;
                    }
                    else
                    {
                        TxtMapFileInfo.Text = "Failed to load map";
                    }
                }
                catch (Exception ex)
                {
                    TxtMapFileInfo.Text = $"Error: {ex.Message}";
                }
            }
        }

        private void FitMap_Click(object sender, RoutedEventArgs e)
        {
            if (MapCanvas.Width == 0 || MapCanvas.Height == 0) return;
            
            double scaleX = MapContainer.ActualWidth / MapCanvas.Width;
            double scaleY = MapContainer.ActualHeight / MapCanvas.Height;
            double scale = Math.Min(scaleX, scaleY);
            
            MapScaleTransform.ScaleX = scale;
            MapScaleTransform.ScaleY = scale;
            
            MapTranslateTransform.X = (MapContainer.ActualWidth - (MapCanvas.Width * scale)) / 2;
            MapTranslateTransform.Y = (MapContainer.ActualHeight - (MapCanvas.Height * scale)) / 2;
        }

        private void MapContainer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : (1.0 / 1.1);
            var position = e.GetPosition(MapCanvas);
            MapScaleTransform.ScaleX *= zoomFactor;
            MapScaleTransform.ScaleY *= zoomFactor;
            MapTranslateTransform.X = (MapTranslateTransform.X - position.X) * zoomFactor + position.X;
            MapTranslateTransform.Y = (MapTranslateTransform.Y - position.Y) * zoomFactor + position.Y;
        }

        private void MapContainer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(MapContainer);
            MapContainer.CaptureMouse();
        }

        private void MapContainer_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                MapContainer.ReleaseMouseCapture();
            }
        }

        private void MapContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var currentPoint = e.GetPosition(MapContainer);
                var diff = currentPoint - _panStartPoint;
                _panStartPoint = currentPoint;

                MapTranslateTransform.X += diff.X;
                MapTranslateTransform.Y += diff.Y;
            }
        }
    }
}
