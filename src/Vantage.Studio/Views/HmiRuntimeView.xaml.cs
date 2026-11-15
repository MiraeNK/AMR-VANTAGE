using System.Windows.Controls;
using System.Windows.Media;
using Vantage.Fleet.Hmi;
using Vantage.Studio.Controls;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class HmiRuntimeView : UserControl
{
    private HmiRuntimeViewModel? _vm;
    private readonly List<HmiWidgetView> _views = new();

    public HmiRuntimeView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) { _vm.ScreenSwitched -= Build; _vm.Scanned -= Refresh; }
            _vm = e.NewValue as HmiRuntimeViewModel;
            if (_vm is null) return;
            _vm.ScreenSwitched += Build;
            _vm.Scanned += Refresh;
            Build();
        };
    }

    private void Build()
    {
        Surface.Children.Clear();
        _views.Clear();
        var s = _vm?.Screen;
        if (s is null) return;
        Surface.Width = s.Width;
        Surface.Height = s.Height;
        try { Surface.Background = (Brush)new BrushConverter().ConvertFromString(s.Background)!; } catch { Surface.Background = Brushes.WhiteSmoke; }

        foreach (var w in s.Widgets.OrderBy(w => w.Kind == WidgetKind.Panel ? 0 : 1))
        {
            var v = new HmiWidgetView(w, design: false) { Width = w.Width, Height = w.Height };
            v.Pressed += id => _vm?.Engine.Press(id);
            Canvas.SetLeft(v, w.X);
            Canvas.SetTop(v, w.Y);
            Surface.Children.Add(v);
            _views.Add(v);
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_vm is null || !IsVisible) return;
        var data = _vm.Data;
        var scene = _vm.MapScene;
        foreach (var v in _views) v.Refresh(_vm.Values, data, scene);
    }
}
