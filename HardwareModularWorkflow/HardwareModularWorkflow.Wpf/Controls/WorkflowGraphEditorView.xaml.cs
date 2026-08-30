using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Controls;

public partial class WorkflowGraphEditorView : UserControl
{
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes), typeof(ObservableCollection<GraphNodeEditModel>),
            typeof(WorkflowGraphEditorView), new PropertyMetadata(null, OnNodesChanged));

    public static readonly DependencyProperty EdgesProperty =
        DependencyProperty.Register(nameof(Edges), typeof(ObservableCollection<GraphEdgeRenderModel>),
            typeof(WorkflowGraphEditorView), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.Register(nameof(SelectedNode), typeof(GraphNodeEditModel),
            typeof(WorkflowGraphEditorView), new PropertyMetadata(null));

    public static readonly DependencyProperty ConnectionModeProperty =
        DependencyProperty.Register(nameof(ConnectionMode), typeof(bool),
            typeof(WorkflowGraphEditorView), new PropertyMetadata(false));

    public ObservableCollection<GraphNodeEditModel> Nodes
    {
        get => (ObservableCollection<GraphNodeEditModel>)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public ObservableCollection<GraphEdgeRenderModel> Edges
    {
        get => (ObservableCollection<GraphEdgeRenderModel>)GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public GraphNodeEditModel? SelectedNode
    {
        get => (GraphNodeEditModel?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public bool ConnectionMode
    {
        get => (bool)GetValue(ConnectionModeProperty);
        set => SetValue(ConnectionModeProperty, value);
    }

    public Visibility ConnectionModeVisibility => ConnectionMode ? Visibility.Visible : Visibility.Collapsed;

    public TransformGroup CanvasTransform { get; } = new();
    public string ZoomLevelText => $"{_zoomLevel * 100:F0}%";

    private double _zoomLevel = 1.0;
    private Point _panOffset;
    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isDraggingNode;
    private GraphNodeEditModel? _dragNode;
    private Point _dragStartOffset;
    private GraphNodeEditModel? _connectionSourceNode;
    private DispatcherTimer? _updateTimer;
    private Line? _dragLine;

    public WorkflowGraphEditorView()
    {
        InitializeComponent();
        var scaleTransform = new ScaleTransform();
        var translateTransform = new TranslateTransform();
        CanvasTransform.Children.Add(scaleTransform);
        CanvasTransform.Children.Add(translateTransform);
        _dragLine = new Line
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800")),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection(new[] { 4.0, 3.0 }),
            Visibility = Visibility.Collapsed
        };
        TransformGrid.Children.Add(_dragLine);
    }

    private static void OnNodesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowGraphEditorView view && e.NewValue is ObservableCollection<GraphNodeEditModel> nodes)
        {
            nodes.CollectionChanged += (_, _) => view.RebuildEdges();
            view.RebuildEdges();
        }
    }

    public void RebuildEdges()
    {
        if (Nodes is null || Edges is null) return;

        var nodePositions = Nodes.ToDictionary(n => n.NodeId);
        for (int i = 0; i < Edges.Count; i++)
        {
            var edge = Edges[i];
            if (nodePositions.TryGetValue(edge.FromNodeId, out var fromNode) &&
                nodePositions.TryGetValue(edge.ToNodeId, out var toNode))
            {
                double fromX = fromNode.X + fromNode.Width / 2;
                double fromY = fromNode.Y + fromNode.Height;
                double toX = toNode.X + toNode.Width / 2;
                double toY = toNode.Y;
                edge.UpdateLine(fromX, fromY, toX, toY);
            }
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
            return;
        }

        if (ConnectionMode && _connectionSourceNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            ConnectionMode = false;
            _connectionSourceNode = null;
            return;
        }

        Keyboard.Focus(this);
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            var dx = pos.X - _lastMousePos.X;
            var dy = pos.Y - _lastMousePos.Y;
            _panOffset.X += dx;
            _panOffset.Y += dy;
            ((TranslateTransform)CanvasTransform.Children[1]).X = _panOffset.X;
            ((TranslateTransform)CanvasTransform.Children[1]).Y = _panOffset.Y;
            _lastMousePos = pos;
            return;
        }

        if (_isDraggingNode && _dragNode is not null)
        {
            _dragNode.X = pos.X - _dragStartOffset.X;
            _dragNode.Y = pos.Y - _dragStartOffset.Y;
            RebuildEdges();
        }

        if (ConnectionMode && _connectionSourceNode is not null && _dragLine is not null)
        {
            double sx = _connectionSourceNode.X + _connectionSourceNode.Width / 2;
            double sy = _connectionSourceNode.Y + _connectionSourceNode.Height;
            _dragLine.X1 = sx;
            _dragLine.Y1 = sy;
            _dragLine.X2 = pos.X;
            _dragLine.Y2 = pos.Y;
            _dragLine.Visibility = Visibility.Visible;
        }

        _lastMousePos = pos;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
        }
        if (_isDraggingNode)
        {
            _isDraggingNode = false;
            _dragNode = null;
            ReleaseMouseCapture();
        }
        if (_dragLine is not null)
            _dragLine.Visibility = Visibility.Collapsed;
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? 0.1 : -0.1;
        _zoomLevel = Math.Clamp(_zoomLevel + delta, 0.2, 3.0);
        ((ScaleTransform)CanvasTransform.Children[0]).ScaleX = _zoomLevel;
        ((ScaleTransform)CanvasTransform.Children[0]).ScaleY = _zoomLevel;
        ZoomLevelLabel.Text = $"{_zoomLevel * 100:F0}%";
    }

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GraphNodeEditModel node)
        {
            if (ConnectionMode && _connectionSourceNode is not null && _connectionSourceNode.NodeId != node.NodeId)
            {
                FindFlowViewModel()?.AddEdgeToTabCommand.Execute((_connectionSourceNode.NodeId, node.NodeId, "Success"));
                ConnectionMode = false;
                _connectionSourceNode = null;
                if (_dragLine is not null) _dragLine.Visibility = Visibility.Collapsed;
                return;
            }

            if (ConnectionMode)
            {
                _connectionSourceNode = node;
                return;
            }

            SelectedNode = node;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingNode = true;
                _dragNode = node;
                var pos = e.GetPosition(this);
                _dragStartOffset = new Point(pos.X - node.X, pos.Y - node.Y);
                element.CaptureMouse();
            }
        }
    }

    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingNode && _dragNode is not null)
        {
            var pos = e.GetPosition(this);
            _dragNode.X = pos.X - _dragStartOffset.X;
            _dragNode.Y = pos.Y - _dragStartOffset.Y;
            RebuildEdges();
        }
    }

    private void OnNodeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingNode)
        {
            _isDraggingNode = false;
            _dragNode = null;
            if (sender is IInputElement element) element.ReleaseMouseCapture();
            RebuildEdges();
        }
    }

    public event Action<(Guid From, Guid To, string RouteKey)>? AddEdgeCommand;

    private ViewModels.FlowViewModel? FindFlowViewModel()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is ViewModels.FlowViewModel vm)
                return vm;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

public class GraphNodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StartTemplate { get; set; }
    public DataTemplate? EndTemplate { get; set; }
    public DataTemplate? SwitchTemplate { get; set; }
    public DataTemplate? ModuleTemplate { get; set; }
    public DataTemplate? SubFlowTemplate { get; set; }
    public DataTemplate? ForkTemplate { get; set; }
    public DataTemplate? JoinTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is GraphNodeEditModel node)
        {
            return node.NodeType switch
            {
                "Start" => StartTemplate,
                "End" => EndTemplate,
                "Switch" => SwitchTemplate,
                "Module" => ModuleTemplate,
                "SubFlow" => SubFlowTemplate,
                "Fork" => ForkTemplate,
                "Join" => JoinTemplate,
                _ => ModuleTemplate
            };
        }
        return base.SelectTemplate(item, container);
    }
}

public class EdgeColorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var isExecuted = values[0] is bool b && b;
        var routeKey = values[1] as string ?? "";

        if (isExecuted) return new SolidColorBrush(Colors.LimeGreen);
        return routeKey switch
        {
            "Success" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            "Failed" or "Timeout" or "Busy" or "Cancelled" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78909C"))
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public class GraphEdgeRenderModel : ViewModelBase
{
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string RouteKey { get; set; } = "Success";
    public bool IsExecuted { get; set; }
    public bool IsDefault { get; set; }

    private double _x1, _y1, _x2, _y2;
    public double X1 { get => _x1; set => SetProperty(ref _x1, value); }
    public double Y1 { get => _y1; set => SetProperty(ref _y1, value); }
    public double X2 { get => _x2; set => SetProperty(ref _x2, value); }
    public double Y2 { get => _y2; set => SetProperty(ref _y2, value); }

    public Brush Stroke => IsExecuted
        ? new SolidColorBrush(Colors.LimeGreen)
        : RouteKey switch
        {
            "Success" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            "Failed" or "Timeout" or "Busy" or "Cancelled" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78909C"))
        };

    public double StrokeThickness => IsExecuted ? 3 : 2;
    public DoubleCollection? StrokeDashArray => RouteKey is "Success" or "" or null ? null : new DoubleCollection(new[] { 4.0, 3.0 });

    public void UpdateLine(double fromX, double fromY, double toX, double toY)
    {
        X1 = fromX; Y1 = fromY; X2 = toX; Y2 = toY;
    }
}

public class ViewModelBase : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        return true;
    }
}
