using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LaserCuttingDetector.Commons;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Polygon = System.Windows.Shapes.Polygon;

namespace LaserCuttingDetector.UserControls
{
    public class PictureViewer : UserControl
    {
        private Canvas _canvas;
        private System.Windows.Controls.Image _image;
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private StackPanel _toolBar;
        private Grid _mainGrid;

        // 十字辅助线相关 - 修改为固定在图片上
        private Line _horizontalLine;
        private Line _verticalLine;
        private bool _crosshairVisible;
        private System.Windows.Point _crosshairImagePosition = new System.Windows.Point(0, 0); // 十字线在图片上的位置
        private bool _crosshairPositionInitialized = false; // 标记十字线位置是否已初始化

        private System.Windows.Point _lastPanPoint;
        private bool _isDragging;
        public readonly double ZoomFactorConstant = 1.2; // 重命名以避免与属性混淆
        public readonly double MinZoom = 0.1;
        private const double MAX_ZOOM = 10.0;

        // 双击检测相关
        private DateTime _lastClickTime = DateTime.MinValue;
        private const double DOUBLE_CLICK_INTERVAL = 500; // 毫秒
        private System.Windows.Point _lastClickPosition;

        // 等待点击添加Point相关
        private bool _waitingForPointClick = false;
        private Cursor _originalCursor;

        // 新增：AddPointRequested事件
        public event EventHandler AddPointRequested;

        #region 依赖属性

        // Picture 依赖属性
        public static readonly DependencyProperty PictureProperty =
            DependencyProperty.Register(nameof(Picture), typeof(Bitmap), typeof(PictureViewer),
                new PropertyMetadata(null, OnPictureChanged));

        public Bitmap Picture
        {
            get => (Bitmap)GetValue(PictureProperty);
            set => SetValue(PictureProperty, value);
        }


        public static readonly DependencyProperty ResetViewOnPictureChangeProperty =
            DependencyProperty.Register(
                nameof(ResetViewOnPictureChange),
                typeof(bool),
                typeof(PictureViewer),
                new PropertyMetadata(true)); // 默认行为是重置
        /// <summary>
        /// 用于控制更换图片时是否重置视图
        /// </summary>
        public bool ResetViewOnPictureChange
        {
            get => (bool)GetValue(ResetViewOnPictureChangeProperty);
            set => SetValue(ResetViewOnPictureChangeProperty, value);
        }

        // PointsInteractive 依赖属性
        public static readonly DependencyProperty PointsInteractiveProperty =
            DependencyProperty.Register(nameof(PointsInteractive), typeof(bool), typeof(PictureViewer),
                new PropertyMetadata(true));

        public bool PointsInteractive
        {
            get => (bool)GetValue(PointsInteractiveProperty);
            set => SetValue(PointsInteractiveProperty, value);
        }

        // Points 依赖属性
        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<Point2>), typeof(PictureViewer),
                new PropertyMetadata(null, OnPointsChanged));

        public ObservableCollection<Point2> Points
        {
            get => (ObservableCollection<Point2>)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        // SelectedPoint 依赖属性
        public static readonly DependencyProperty SelectedPointProperty =
            DependencyProperty.Register(nameof(SelectedPoint), typeof(Point2), typeof(PictureViewer),
                new PropertyMetadata(null, OnSelectedPointChanged));

        public Point2 SelectedPoint
        {
            get => (Point2)GetValue(SelectedPointProperty);
            set => SetValue(SelectedPointProperty, value);
        }

        public static readonly DependencyProperty ShapesProperty =
            DependencyProperty.Register(nameof(Shapes), typeof(ObservableCollection<DrawableShape>), typeof(PictureViewer),
                new PropertyMetadata(null, OnShapesChanged));

        private readonly Dictionary<DrawableShape, ShapeVisual> _shapeVisuals;

        public ObservableCollection<DrawableShape> Shapes
        {
            get => (ObservableCollection<DrawableShape>)GetValue(ShapesProperty);
            set => SetValue(ShapesProperty, value);
        }



        #endregion

        #region 新增：用于ViewModel控制的依赖属性

        /// <summary>
        /// 依赖属性：用于从ViewModel接收要居中的目标点（图像坐标系）
        /// </summary>
        public static readonly DependencyProperty CenterPointProperty =
            DependencyProperty.Register(nameof(CenterPoint), typeof(System.Windows.Point), typeof(PictureViewer),
                new PropertyMetadata(new System.Windows.Point(-1, -1), OnCenterPointOrZoomFactorChanged));

        public System.Windows.Point CenterPoint
        {
            get => (System.Windows.Point)GetValue(CenterPointProperty);
            set => SetValue(CenterPointProperty, value);
        }

        /// <summary>
        /// 依赖属性：用于从ViewModel接收目标缩放级别
        /// </summary>
        public static readonly DependencyProperty ZoomFactorProperty =
            DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(PictureViewer),
                new PropertyMetadata(1.0, OnCenterPointOrZoomFactorChanged));

        public double ZoomFactor
        {
            get => (double)GetValue(ZoomFactorProperty);
            set => SetValue(ZoomFactorProperty, value);
        }

        /// <summary>
        /// 当 CenterPoint 或 ZoomFactor 属性从 ViewModel 更新时，调用此回调函数
        /// </summary>
        private static void OnCenterPointOrZoomFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PictureViewer viewer)
            {
                // 使用 Dispatcher.BeginInvoke 确保在UI布局更新后执行，避免获取到旧的控件尺寸
                viewer.Dispatcher.BeginInvoke(new Action(() =>
                {
                    viewer.PerformCenterAndZoom();
                }), DispatcherPriority.Loaded);
            }
        }

        #endregion

        // 新增：Point2相关字段
        private readonly Dictionary<Point2, PointVisual> _pointVisuals;
        private PointVisual _selectedPointVisual;

        private readonly Dictionary<Rectangle2, RectangleVisual> _rectangleVisuals;
        private RectangleVisual _selectedRectangleVisual;

        private bool _isDraggingPoint;
        private Point2 _draggingPoint;
        private TextBlock _zoomLabel;

        public PictureViewer()
        {
            _pointVisuals = new Dictionary<Point2, PointVisual>();
            _shapeVisuals = new Dictionary<DrawableShape, ShapeVisual>();
            InitializeComponent();
            SetupEventHandlers();

            // 优化渲染性能
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetCachingHint(this, CachingHint.Cache);

            // 启用硬件加速
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        private void InitializeComponent()
        {
            // 设置控件背景
            Background = new SolidColorBrush(Colors.LightGray);
            ClipToBounds = true;

            // 创建主Grid容器
            _mainGrid = new Grid();
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 工具栏行
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 图片显示区域

            // 创建工具栏（独立处理，不参与图片区域的事件）
            CreateToolBar();

            // 创建图片显示区域的容器
            CreateImageDisplayArea();

            Content = _mainGrid;

            // 创建右键菜单
            CreateContextMenu();
        }

        private void CreateImageDisplayArea()
        {
            // 创建图片显示区域的边框容器
            var imageDisplayBorder = new Border
            {
                Background = new SolidColorBrush(Colors.DarkGray),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2)
            };

            // 创建Canvas容器 - 只处理图片显示相关的事件
            _canvas = new Canvas
            {
                Background = Brushes.Transparent,
                UseLayoutRounding = true,
                ClipToBounds = true  // 确保内容不会超出边界
            };

            // 将Canvas放入边框容器
            imageDisplayBorder.Child = _canvas;

            // 创建Image控件
            _image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.None,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };


            // 设置变换
            _translateTransform = new TranslateTransform(0, 0);
            _scaleTransform = new ScaleTransform(1.0, 1.0);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);
            _image.RenderTransform = transformGroup;
            _image.RenderTransformOrigin = new System.Windows.Point(0, 0);

            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            RenderOptions.SetCachingHint(_image, CachingHint.Cache);

            // 创建十字辅助线
            CreateCrosshair();

            // 设置图片的Z-Index为最低
            Panel.SetZIndex(_image, 0);
            _canvas.Children.Add(_image);

            // 只在Canvas上绑定图片相关的鼠标事件
            _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
            _canvas.MouseLeftButtonUp += OnCanvasMouseUp;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseWheel += OnCanvasMouseWheel;
            _canvas.SizeChanged += OnCanvasSizeChanged;
            _canvas.MouseLeave += OnCanvasMouseLeave;

            // 双击事件
            _canvas.MouseLeftButtonDown += OnCanvasDoubleClickDetection;

            // 让Canvas可以获得焦点
            _canvas.Focusable = true;
            _canvas.KeyDown += OnKeyDown;

            // 将图片显示区域添加到Grid的第二行
            Grid.SetRow(imageDisplayBorder, 1);
            _mainGrid.Children.Add(imageDisplayBorder);
        }


        // ShapeVisual 内部类
        private class ShapeVisual
        {
            public Polygon ShapePolygon { get; private set; }
            public DrawableShape Shape { get; set; }

            public ShapeVisual(DrawableShape shape)
            {
                Shape = shape;
                ShapePolygon = new Polygon
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Colors.Yellow) { Opacity = 0.2 },
                    IsHitTestVisible = false // 形状本身不响应鼠标点击
                };
                SetSelected(shape.IsSelected);
            }

            public void SetSelected(bool selected)
            {
                Shape.IsSelected = selected;
                var color = selected ? Brushes.Red : Brushes.Yellow;
                ShapePolygon.Stroke = color;
                ShapePolygon.Fill = new SolidColorBrush(selected ? Colors.Red : Colors.Yellow) { Opacity = 0.2 };
            }
        }

        private static void OnShapesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PictureViewer;
            control?.OnShapesChanged(e.OldValue as ObservableCollection<DrawableShape>, e.NewValue as ObservableCollection<DrawableShape>);
        }

        private void OnShapesChanged(ObservableCollection<DrawableShape> oldShapes, ObservableCollection<DrawableShape> newShapes)
        {
            if (oldShapes != null)
            {
                oldShapes.CollectionChanged -= OnShapesCollectionChanged;
                ClearAllShapeVisuals();
            }
            if (newShapes != null)
            {
                newShapes.CollectionChanged += OnShapesCollectionChanged;
                foreach (var shape in newShapes)
                {
                    CreateShapeVisual(shape);
                }
            }
        }

        private void OnShapesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null) foreach (DrawableShape shape in e.NewItems) CreateShapeVisual(shape);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null) foreach (DrawableShape shape in e.OldItems) RemoveShapeVisual(shape);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    ClearAllShapeVisuals();
                    break;
            }
        }

        private void CreateShapeVisual(DrawableShape shape)
        {
            if (_shapeVisuals.ContainsKey(shape)) return;
            var visual = new ShapeVisual(shape);
            _shapeVisuals[shape] = visual;
            Panel.SetZIndex(visual.ShapePolygon, 150);
            _canvas.Children.Add(visual.ShapePolygon);
            UpdateShapeVisual(shape);
        }

        private void RemoveShapeVisual(DrawableShape shape)
        {
            if (_shapeVisuals.TryGetValue(shape, out var visual))
            {
                _canvas.Children.Remove(visual.ShapePolygon);
                _shapeVisuals.Remove(shape);
            }
        }

        private void ClearAllShapeVisuals()
        {
            foreach (var visual in _shapeVisuals.Values)
            {
                _canvas.Children.Remove(visual.ShapePolygon);
            }
            _shapeVisuals.Clear();
        }

        private void UpdateShapeVisual(DrawableShape shape)
        {
            if (!_shapeVisuals.TryGetValue(shape, out var visual) || _image.Source == null) return;

            var canvasPoints = new PointCollection();
            var transform = _image.TransformToVisual(_canvas);

            foreach (var imagePoint in shape.Points)
            {
                canvasPoints.Add(transform.Transform(imagePoint));
            }

            visual.ShapePolygon.Points = canvasPoints;
        }

        private void UpdateAllShapeVisuals()
        {
            if (Shapes == null) return;
            foreach (var shape in Shapes)
            {
                UpdateShapeVisual(shape);
            }
        }


        // Canvas专用的鼠标事件处理
        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_image.Source == null) return;

            // 确保焦点在Canvas上，这样键盘事件才能正常工作
            _canvas.Focus();

            var clickPoint = e.GetPosition(_canvas);

            // 如果正在等待点击添加Point
            if (_waitingForPointClick)
            {
                var imagePoint = CanvasToImageCoordinates(clickPoint);
                var newPoint = new Point2 { X = imagePoint.X, Y = imagePoint.Y };

                if (Points == null)
                {
                    Points = new ObservableCollection<Point2>();
                }

                Points.Add(newPoint);

                // 结束等待状态
                _waitingForPointClick = false;
                _canvas.Cursor = _originalCursor;

                e.Handled = true;
                return;
            }

            // 检查是否点击在Point2上（仅当Points可交互时）
            if (PointsInteractive && _isDraggingPoint)
            {
                foreach (var kvp in _pointVisuals)
                {
                    var visual = kvp.Value;
                    var crossCenterX = Canvas.GetLeft(visual.CrossPath);
                    var crossCenterY = Canvas.GetTop(visual.CrossPath);

                    var distance = Math.Sqrt(Math.Pow(clickPoint.X - crossCenterX, 2) +
                                             Math.Pow(clickPoint.Y - crossCenterY, 2));

                    if (distance < 15)
                    {
                        return; // 点击在Point2上，不处理图片拖拽
                    }
                }
            }

            // 开始拖拽
            _isDragging = true;
            _lastPanPoint = clickPoint;

            // 在Canvas上捕获鼠标
            _canvas.CaptureMouse();
            _canvas.Cursor = Cursors.Hand;

            e.Handled = true;
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _canvas.ReleaseMouseCapture();
                _canvas.Cursor = _waitingForPointClick ? Cursors.Cross : Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            // 安全检查
            if (_isDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                _canvas.ReleaseMouseCapture();
                _canvas.Cursor = _waitingForPointClick ? Cursors.Cross : Cursors.Arrow;
                return;
            }

            if (_isDragging && _image.Source != null)
            {
                var currentPoint = e.GetPosition(_canvas);
                var deltaX = currentPoint.X - _lastPanPoint.X;
                var deltaY = currentPoint.Y - _lastPanPoint.Y;

                _translateTransform.X += deltaX;
                _translateTransform.Y += deltaY;
                _lastPanPoint = currentPoint;

                UpdateAllVisualsAfterTransform();

                e.Handled = true;
            }
        }

        private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_image.Source == null) return;

            var mousePos = e.GetPosition(_canvas);
            var delta = e.Delta > 0 ? ZoomFactorConstant : 1.0 / ZoomFactorConstant;

            ZoomAtPoint(mousePos, delta);
            UpdateZoomLabel();

            e.Handled = true;
        }

        // 双击检测（在Canvas上）
        private void OnCanvasDoubleClickDetection(object sender, MouseButtonEventArgs e)
        {
            var currentTime = DateTime.Now;
            var currentPosition = e.GetPosition(_canvas);
            var timeDiff = (currentTime - _lastClickTime).TotalMilliseconds;
            var positionDiff = Math.Sqrt(Math.Pow(currentPosition.X - _lastClickPosition.X, 2) +
                                         Math.Pow(currentPosition.Y - _lastClickPosition.Y, 2));

            if (timeDiff < DOUBLE_CLICK_INTERVAL && positionDiff < 5)
            {
                FitToControl();
                e.Handled = true;
                return;
            }

            _lastClickTime = currentTime;
            _lastClickPosition = currentPosition;
        }

        private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
        {
            // 如果鼠标离开Canvas且正在拖拽，重置拖拽状态
            if (_isDragging)
            {
                _isDragging = false;
                _canvas.ReleaseMouseCapture();
                Cursor = _waitingForPointClick ? Cursors.Cross : Cursors.Arrow;
            }
        }

        private Border CreateToolBarContainer()
        {
            _toolBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = 35,
                Margin = new Thickness(5)
            };

            // 创建工具栏按钮
            CreateToolBarButtons();

            // 创建边框容器
            var border = new Border
            {
                Child = _toolBar,
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Margin = new Thickness(0, 0, 0, 2)
            };

            // 添加阴影效果
            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 2,
                Opacity = 0.3,
                BlurRadius = 3
            };

            return border;
        }

        private void CreateToolBar()
        {
            var toolBarContainer = CreateToolBarContainer();

            // 将容器添加到 Grid
            Grid.SetRow(toolBarContainer, 0);
            Panel.SetZIndex(toolBarContainer, 1000);
            _mainGrid.Children.Add(toolBarContainer);
        }

        private void CreateToolBarButtons()
        {
            // 放大按钮
            var zoomInButton = new Button
            {
                Content = "放大",
                Width = 50,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            zoomInButton.Click += ZoomIn;
            _toolBar.Children.Add(zoomInButton);

            // 缩小按钮
            var zoomOutButton = new Button
            {
                Content = "缩小",
                Width = 50,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            zoomOutButton.Click += ZoomOut;
            _toolBar.Children.Add(zoomOutButton);

            // 适应窗口按钮
            var fitToWindowButton = new Button
            {
                Content = "适应窗口",
                Width = 70,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            fitToWindowButton.Click += FitToWindow;
            _toolBar.Children.Add(fitToWindowButton);

            // 实际大小按钮
            var actualSizeButton = new Button
            {
                Content = "实际大小",
                Width = 70,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            actualSizeButton.Click += ActualSize;
            _toolBar.Children.Add(actualSizeButton);

            // 分隔符
            var separator = new Rectangle
            {
                Width = 1,
                Height = 20,
                Fill = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(5, 0, 5, 0)
            };
            _toolBar.Children.Add(separator);

            // 显示/隐藏十字线按钮
            var crosshairButton = new Button
            {
                Content = "十字线",
                Width = 60,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            crosshairButton.Click += ToggleCrosshair;
            _toolBar.Children.Add(crosshairButton);

            // 调整十字线位置按钮
            var adjustCrosshairButton = new Button
            {
                Content = "调整十字线",
                Width = 80,
                Height = 25,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            adjustCrosshairButton.Click += AdjustCrosshair;
            _toolBar.Children.Add(adjustCrosshairButton);

            // 缩放比例显示
            _zoomLabel = new TextBlock
            {
                Text = "100%",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 5, 0),
                FontWeight = FontWeights.Bold
            };
            _toolBar.Children.Add(_zoomLabel);
        }

        // 新增：调整十字线位置方法
        private void AdjustCrosshair(object sender, RoutedEventArgs e)
        {
            if (_image.Source == null) return;

            var dialog = new CrosshairPositionDialog(_crosshairImagePosition, _image.Source.Width, _image.Source.Height);
            if (dialog.ShowDialog() == true)
            {
                _crosshairImagePosition = dialog.Position;
                if (!_crosshairVisible)
                {
                    _crosshairVisible = true;
                    _horizontalLine.Visibility = Visibility.Visible;
                    _verticalLine.Visibility = Visibility.Visible;
                }
                UpdateCrosshair();
            }
        }

        // 新增：等待点击添加Point的方法（公共方法，供外部调用）
        public void WaitForPointClick()
        {
            _waitingForPointClick = true;
            _originalCursor = _canvas.Cursor;
            _canvas.Cursor = Cursors.Cross;
        }

        // 新增：触发AddPointRequested事件的方法
        public void RequestAddPoint()
        {
            AddPointRequested?.Invoke(this, EventArgs.Empty);
        }

        // 修改这些方法的签名，添加 RoutedEventArgs 参数
        private void ResetToOriginalSize()
        {
            if (_image.Source == null) return;

            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;

            UpdateAllPointVisuals();
        }

        // 适应窗口方法
        private void FitToWindow(object sender, RoutedEventArgs e)
        {
            if (_image.Source == null) return;

            var imageWidth = _image.Source.Width;
            var imageHeight = _image.Source.Height;
            var canvasWidth = _canvas.ActualWidth;
            var canvasHeight = _canvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            // 计算缩放比例，保持宽高比
            var scaleX = canvasWidth / imageWidth;
            var scaleY = canvasHeight / imageHeight;
            var scale = Math.Min(scaleX, scaleY);

            // 应用缩放
            _scaleTransform.ScaleX = scale;
            _scaleTransform.ScaleY = scale;

            // 重置平移变换
            _translateTransform.X = 0;
            _translateTransform.Y = 0;

            // 更新图片位置（这会自动居中）
            UpdateAllPointVisuals();

            // 更新缩放标签
            UpdateZoomLabel();
        }

        private void ActualSize(object sender, RoutedEventArgs e)
        {
            if (_image.Source == null) return;

            // 设置为100%缩放
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;

            // 【修改】计算平移量，使图片居中
            var canvasWidth = _canvas.ActualWidth;
            var canvasHeight = _canvas.ActualHeight;
            var imageWidth = _image.Source.Width;
            var imageHeight = _image.Source.Height;

            _translateTransform.X = (canvasWidth - imageWidth) / 2;
            _translateTransform.Y = (canvasHeight - imageHeight) / 2;

            // 更新所有视觉元素
            UpdateAllVisualsAfterTransform();
            UpdateZoomLabel();
        }
        // 更新缩放标签的方法
        private void UpdateZoomLabel()
        {
            if (_zoomLabel != null)
            {
                var zoomPercentage = _scaleTransform.ScaleX * 100;
                _zoomLabel.Text = $"{zoomPercentage:F0}%";
            }
        }

        private void CreateCrosshair()
        {
            // 水平线
            _horizontalLine = new Line
            {
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };

            // 垂直线
            _verticalLine = new Line
            {
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };

            // 设置十字线的Z-Index，确保它们显示在图片之上
            Panel.SetZIndex(_horizontalLine, 100);
            Panel.SetZIndex(_verticalLine, 100);

            _canvas.Children.Add(_horizontalLine);
            _canvas.Children.Add(_verticalLine);
        }

        // 修改：更新十字线位置，使其固定在图片上
        private void UpdateCrosshair()
        {
            if (!_crosshairVisible || _image.Source == null)
                return;

            // 将十字线的图片坐标转换为Canvas坐标
            var canvasPoint = ImageToCanvasCoordinates(_crosshairImagePosition);

            // 计算十字线的范围（在图片范围内）
            var imageLeft = Canvas.GetLeft(_image) + _translateTransform.X;
            var imageTop = Canvas.GetTop(_image) + _translateTransform.Y;
            var imageRight = imageLeft + _image.Source.Width * _scaleTransform.ScaleX;
            var imageBottom = imageTop + _image.Source.Height * _scaleTransform.ScaleY;

            // 更新水平线（只在图片范围内显示）
            _horizontalLine.X1 = Math.Max(0, Math.Min(_canvas.ActualWidth, imageLeft));
            _horizontalLine.Y1 = canvasPoint.Y;
            _horizontalLine.X2 = Math.Max(0, Math.Min(_canvas.ActualWidth, imageRight));
            _horizontalLine.Y2 = canvasPoint.Y;

            // 更新垂直线（只在图片范围内显示）
            _verticalLine.X1 = canvasPoint.X;
            _verticalLine.Y1 = Math.Max(0, Math.Min(_canvas.ActualHeight, imageTop));
            _verticalLine.X2 = canvasPoint.X;
            _verticalLine.Y2 = Math.Max(0, Math.Min(_canvas.ActualHeight, imageBottom));

            // 如果十字线超出Canvas范围，隐藏相应部分
            if (canvasPoint.Y < 0 || canvasPoint.Y > _canvas.ActualHeight)
            {
                _horizontalLine.Visibility = Visibility.Collapsed;
            }
            else
            {
                _horizontalLine.Visibility = Visibility.Visible;
            }

            if (canvasPoint.X < 0 || canvasPoint.X > _canvas.ActualWidth)
            {
                _verticalLine.Visibility = Visibility.Collapsed;
            }
            else
            {
                _verticalLine.Visibility = Visibility.Visible;
            }
        }

        private void ZoomIn(object sender, RoutedEventArgs e)
        {
            if (_image.Source == null) return;

            var centerPoint = new System.Windows.Point(_canvas.ActualWidth / 2, _canvas.ActualHeight / 2);
            ZoomAtPoint(centerPoint, ZoomFactorConstant);

            // 更新缩放标签
            UpdateZoomLabel();
        }

        private void ZoomOut(object sender, RoutedEventArgs e)
        {
            if (_image.Source == null) return;

            var centerPoint = new System.Windows.Point(_canvas.ActualWidth / 2, _canvas.ActualHeight / 2);
            ZoomAtPoint(centerPoint, 1.0 / ZoomFactorConstant);
            // 更新缩放标签
            UpdateZoomLabel();
        }

        private void ZoomAtPoint(System.Windows.Point pointOnCanvas, double factor)
        {
            if (_image.Source == null) return;

            var oldScale = _scaleTransform.ScaleX;
            var newScale = oldScale * factor;
            newScale = Math.Max(MinZoom, Math.Min(MAX_ZOOM, newScale));

            if (Math.Abs(newScale - oldScale) < 0.001) return;

            // 获取鼠标在Canvas上的位置
            var mouseX = pointOnCanvas.X;
            var mouseY = pointOnCanvas.Y;

            // 计算鼠标在缩放前的图像上的相对位置
            // (鼠标位置 - 当前平移量) / 当前缩放比例
            var relativeX = (mouseX - _translateTransform.X) / oldScale;
            var relativeY = (mouseY - _translateTransform.Y) / oldScale;

            // 更新缩放比例
            _scaleTransform.ScaleX = newScale;
            _scaleTransform.ScaleY = newScale;

            // 计算新的平移量，以保持鼠标下的点位置不变
            // 新平移量 = 鼠标位置 - (相对位置 * 新缩放比例)
            _translateTransform.X = mouseX - relativeX * newScale;
            _translateTransform.Y = mouseY - relativeY * newScale;

            // 更新视觉元素
            UpdateAllPointVisuals();
            UpdateAllShapeVisuals();
            UpdateCrosshair();
        }

        private void ToggleCrosshair(object sender, RoutedEventArgs e)
        {
            _crosshairVisible = !_crosshairVisible;

            if (_crosshairVisible)
            {
                // 只有在十字线位置还没有初始化时才设置为图片中心
                if (_image.Source != null && !_crosshairPositionInitialized)
                {
                    _crosshairImagePosition = new System.Windows.Point(_image.Source.Width / 2, _image.Source.Height / 2);
                    _crosshairPositionInitialized = true;
                }

                _horizontalLine.Visibility = Visibility.Visible;
                _verticalLine.Visibility = Visibility.Visible;
                UpdateCrosshair();
            }
            else
            {
                _horizontalLine.Visibility = Visibility.Collapsed;
                _verticalLine.Visibility = Visibility.Collapsed;
            }
        }

        private void SetupEventHandlers()
        {
            // 只处理整个控件级别的事件
            SizeChanged += OnSizeChanged;

            // 设置默认光标
            Cursor = Cursors.Arrow;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_image.Source == null) return;

            switch (e.Key)
            {
                case Key.Add:
                case Key.OemPlus:
                    ZoomIn(null, null);
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    ZoomOut(null, null);
                    e.Handled = true;
                    break;
                case Key.F:
                    FitToWindow(null, null);
                    e.Handled = true;
                    break;
                case Key.D1:
                    ActualSize(null, null);
                    e.Handled = true;
                    break;
                case Key.C:
                    ToggleCrosshair(null, null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    // ESC键取消等待点击状态
                    if (_waitingForPointClick)
                    {
                        _waitingForPointClick = false;
                        _canvas.Cursor = _originalCursor;
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 延迟更新以避免频繁重绘
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAllPointVisuals();
                if (_crosshairVisible)
                {
                    UpdateCrosshair();
                }
            }), DispatcherPriority.Background);
        }

        private void CreateContextMenu()
        {
            var contextMenu = new ContextMenu();

            var saveBmpItem = new MenuItem { Header = "保存为 BMP" };
            saveBmpItem.Click += OnSaveBmpItemOnClick;
            contextMenu.Items.Add(saveBmpItem);

            var saveJpgItem = new MenuItem { Header = "保存为 JPG" };
            saveJpgItem.Click += OnSaveJpgItemOnClick;
            contextMenu.Items.Add(saveJpgItem);

            contextMenu.Items.Add(new Separator());

            var originalSizeItem = new MenuItem { Header = "原始大小" };
            originalSizeItem.Click += OnOriginalSizeItemOnClick;
            contextMenu.Items.Add(originalSizeItem);

            var fitToControlItem = new MenuItem { Header = "适应控件" };
            fitToControlItem.Click += OnFitToControlItemOnClick;
            contextMenu.Items.Add(fitToControlItem);

            ContextMenu = contextMenu;
        }

        private void OnFitToControlItemOnClick(object o, RoutedEventArgs routedEventArgs)
        {
            FitToControl();
        }

        private void OnOriginalSizeItemOnClick(object o, RoutedEventArgs routedEventArgs)
        {
            ResetToOriginalSize();
        }

        private void OnSaveJpgItemOnClick(object o, RoutedEventArgs routedEventArgs)
        {
            SaveImage("jpg");
        }

        private void OnSaveBmpItemOnClick(object o, RoutedEventArgs routedEventArgs)
        {
            SaveImage("bmp");
        }

        private static void OnPictureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PictureViewer;
            control?.OnPictureChanged();
        }

        // 修改：切换图像时，首次赋值时自动适应控件，后续保持当前缩放和位置
        private void OnPictureChanged()
        {
            if (Picture != null)
            {
                _image.Source = BitmapToBitmapSource(Picture);

                // 只有在十字线位置还没有初始化时才设置为图片中心
                if (!_crosshairPositionInitialized)
                {
                    _crosshairImagePosition = new System.Windows.Point(Picture.Width / 2.0, Picture.Height / 2.0);
                    _crosshairPositionInitialized = true;
                }

                // 【修改】使用新的依赖属性来决定是否重置视图
                if (ResetViewOnPictureChange)
                {
                    // 延迟执行适应控件操作
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
                        {
                            FitToControl();
                        }
                        else
                        {
                            // 如果Canvas尺寸还未确定，监听SizeChanged事件
                            void OnCanvasLoaded(object sender, SizeChangedEventArgs e)
                            {
                                if (_canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
                                {
                                    _canvas.SizeChanged -= OnCanvasLoaded;
                                    FitToControl();
                                }
                            }
                            _canvas.SizeChanged += OnCanvasLoaded;
                        }
                    }), DispatcherPriority.Loaded);
                }
                else
                {
                    // 保持当前的缩放和位置，只更新视觉元素
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateAllPointVisuals();
                        if (_crosshairVisible)
                        {
                            UpdateCrosshair();
                        }
                    }), DispatcherPriority.Render);
                }
            }
            else
            {
                _image.Source = null;
            }
        }

        private BitmapSource BitmapToBitmapSource(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // 冻结BitmapSource以提升性能
                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 延迟更新以避免频繁重绘
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAllPointVisuals();
                if (_crosshairVisible)
                {
                    UpdateCrosshair();
                }
            }), DispatcherPriority.Background);
        }



        public void FitToControl()
        {
            if (_image.Source == null || _canvas.ActualWidth == 0 || _canvas.ActualHeight == 0) return;

            var imageWidth = _image.Source.Width;
            var imageHeight = _image.Source.Height;
            var canvasWidth = _canvas.ActualWidth;
            var canvasHeight = _canvas.ActualHeight;

            var scaleX = canvasWidth / imageWidth;
            var scaleY = canvasHeight / imageHeight;
            var newScale = Math.Min(scaleX, scaleY);

            // 应用新的缩放
            _scaleTransform.ScaleX = newScale;
            _scaleTransform.ScaleY = newScale;

            // 【修改】计算新的平移量，使图片居中
            // 平移量 = (Canvas尺寸 - 图片缩放后尺寸) / 2
            _translateTransform.X = (canvasWidth - imageWidth * newScale) / 2;
            _translateTransform.Y = (canvasHeight - imageHeight * newScale) / 2;

            // 更新所有视觉元素的位置
            UpdateAllVisualsAfterTransform();
            UpdateZoomLabel();
        }
        // 添加手动刷新方法
        private void UpdateAllVisualsAfterTransform()
        {
            UpdateAllPointVisuals();
            UpdateAllShapeVisuals();
            if (_crosshairVisible)
            {
                UpdateCrosshair();
            }
        }
        private void SaveImage(string format)
        {
            if (Picture == null) return;

            var saveFileDialog = new SaveFileDialog();

            if (format.ToLower() == "bmp")
            {
                saveFileDialog.Filter = "BMP文件|*.bmp";
                saveFileDialog.DefaultExt = "bmp";
            }
            else if (format.ToLower() == "jpg")
            {
                saveFileDialog.Filter = "JPG文件|*.jpg";
                saveFileDialog.DefaultExt = "jpg";
            }

            if (saveFileDialog.ShowDialog() == true)
            {
                Picture.Save(saveFileDialog.FileName,
                    format.ToLower() == "bmp" ? ImageFormat.Bmp : ImageFormat.Jpeg);

                MessageBox.Show("图片保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public Bitmap GetCurrentBitmap()
        {
            return Picture;
        }

        public void LoadFromFile(string filePath)
        {
            Picture = new Bitmap(filePath);
        }

        #region 新增核心方法：执行平移和缩放

        /// <summary>
        /// 根据 CenterPoint 和 ZoomFactor 属性的值，执行图像的平移和缩放操作
        /// </summary>
        private void PerformCenterAndZoom()
        {
            if (_image.Source == null || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0 || CenterPoint.X < 0)
            {
                return;
            }

            // 1. 设置新的缩放级别
            var newScale = Math.Max(MinZoom, Math.Min(MAX_ZOOM, ZoomFactor));
            _scaleTransform.ScaleX = newScale;
            _scaleTransform.ScaleY = newScale;

            // 2. 【修改】计算新的平移量
            // 目标：将图像上的 CenterPoint 移动到 Canvas 的中心点
            var canvasCenterX = _canvas.ActualWidth / 2;
            var canvasCenterY = _canvas.ActualHeight / 2;

            // 新的平移量 = Canvas中心点 - (目标图像点 * 新缩放比例)
            _translateTransform.X = canvasCenterX - CenterPoint.X * newScale;
            _translateTransform.Y = canvasCenterY - CenterPoint.Y * newScale;

            // 3. 更新所有视觉元素
            UpdateAllVisualsAfterTransform();
            UpdateZoomLabel();
        }
        #endregion

        #region Points 相关处理

        // Points属性变化处理
        private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PictureViewer;
            control?.OnPointsChanged(e.OldValue as ObservableCollection<Point2>, e.NewValue as ObservableCollection<Point2>);
        }

        private void OnPointsChanged(ObservableCollection<Point2> oldPoints, ObservableCollection<Point2> newPoints)
        {
            // 移除旧的事件监听和视觉元素
            if (oldPoints != null)
            {
                oldPoints.CollectionChanged -= OnPointsCollectionChanged;
                foreach (var point in oldPoints)
                {
                    point.PropertyChanged -= OnPointPropertyChanged;
                }
                ClearAllPointVisuals();
            }

            // 添加新的事件监听和视觉元素
            if (newPoints != null)
            {
                newPoints.CollectionChanged += OnPointsCollectionChanged;
                foreach (var point in newPoints)
                {
                    point.PropertyChanged += OnPointPropertyChanged;
                    CreatePointVisual(point);

                    // 如果这个点是选中的，更新选中状态
                    if (point.IsSelected && SelectedPoint != point)
                    {
                        SelectedPoint = point;
                    }
                }
            }

            UpdateAllPointVisuals();
        }

        private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (Point2 point in e.NewItems)
                        {
                            point.PropertyChanged += OnPointPropertyChanged;
                            CreatePointVisual(point);

                            // 如果新添加的点是选中的，更新选中状态
                            if (point.IsSelected && SelectedPoint != point)
                            {
                                SelectedPoint = point;
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (Point2 point in e.OldItems)
                        {
                            point.PropertyChanged -= OnPointPropertyChanged;
                            RemovePointVisual(point);

                            // 如果删除的是选中的点，清除选中状态
                            if (SelectedPoint == point)
                            {
                                SelectedPoint = null;
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Clear() 操作会触发 Reset 事件
                    // 清除所有现有的视觉元素
                    var pointsToRemove = _pointVisuals.Keys.ToList();
                    foreach (var point in pointsToRemove)
                    {
                        point.PropertyChanged -= OnPointPropertyChanged;
                        RemovePointVisual(point);
                    }

                    // 清除选中状态
                    SelectedPoint = null;

                    // 如果集合不为空，重新添加所有元素
                    if (Points != null && Points.Count > 0)
                    {
                        foreach (var point in Points)
                        {
                            point.PropertyChanged += OnPointPropertyChanged;
                            CreatePointVisual(point);

                            if (point.IsSelected && SelectedPoint != point)
                            {
                                SelectedPoint = point;
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Replace:
                    // 处理替换操作
                    if (e.OldItems != null)
                    {
                        foreach (Point2 point in e.OldItems)
                        {
                            point.PropertyChanged -= OnPointPropertyChanged;
                            RemovePointVisual(point);
                        }
                    }
                    if (e.NewItems != null)
                    {
                        foreach (Point2 point in e.NewItems)
                        {
                            point.PropertyChanged += OnPointPropertyChanged;
                            CreatePointVisual(point);
                        }
                    }
                    break;
            }

            UpdateAllPointVisuals();
        }

        // 修改 OnPointPropertyChanged 方法以处理 IsSelected 属性变化
        private void OnPointPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is Point2 point && _pointVisuals.ContainsKey(point))
            {
                if (e.PropertyName == nameof(Point2.IsSelected))
                {
                    // 当点的选中状态改变时，更新视觉效果
                    if (_pointVisuals.TryGetValue(point, out var visual))
                    {
                        visual.SetSelected(point.IsSelected);
                        if (point.IsSelected)
                        {
                            _selectedPointVisual = visual;
                        }
                        else if (_selectedPointVisual == visual)
                        {
                            _selectedPointVisual = null;
                        }
                    }
                }
                else if (e.PropertyName == nameof(Point2.X) || e.PropertyName == nameof(Point2.Y))
                {
                    // 修复问题4：坐标变化时更新视觉位置
                    UpdatePointVisual(point);
                }
            }
        }

        // SelectedPoint属性变化处理
        private static void OnSelectedPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PictureViewer;
            control?.OnSelectedPointChanged(e.OldValue as Point2, e.NewValue as Point2);
        }

        private void OnSelectedPointChanged(Point2 oldPoint, Point2 newPoint)
        {
            // 更新旧点的选中状态
            if (oldPoint != null)
            {
                oldPoint.IsSelected = false;
                if (_pointVisuals.TryGetValue(oldPoint, out var oldVisual))
                {
                    oldVisual.SetSelected(false);
                }
            }

            // 更新新点的选中状态
            if (newPoint != null)
            {
                newPoint.IsSelected = true;
                if (_pointVisuals.TryGetValue(newPoint, out var newVisual))
                {
                    newVisual.SetSelected(true);
                    _selectedPointVisual = newVisual;
                }
            }
            else
            {
                _selectedPointVisual = null;
            }
        }

        #endregion

        #region Rectangles 相关处理

        // Rectangles属性变化处理






        #endregion

        #region 视觉元素类定义

        // PointVisual类定义
        private class PointVisual
        {
            public Path CrossPath { get; private set; }
            public Point2 Point { get; set; }
            public bool IsSelected { get; set; }

            public PointVisual(Point2 point)
            {
                Point = point;
                CreateVisualElements();
            }

            private void CreateVisualElements()
            {
                // 创建十字线路径
                var geometry = new GeometryGroup();

                // 水平线
                geometry.Children.Add(new LineGeometry(new System.Windows.Point(-10, 0), new System.Windows.Point(10, 0)));
                // 垂直线
                geometry.Children.Add(new LineGeometry(new System.Windows.Point(0, -10), new System.Windows.Point(0, 10)));

                CrossPath = new Path
                {
                    Data = geometry,
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand
                };
            }

            public void SetSelected(bool selected)
            {
                IsSelected = selected;
                var color = selected ? Brushes.Red : Brushes.Yellow;
                CrossPath.Stroke = color;
            }
        }

        // RectangleVisual类定义
        private class RectangleVisual
        {
            public Path RectanglePath { get; private set; }
            public Rectangle2 Rectangle { get; set; }
            public bool IsSelected { get; set; }

            public RectangleVisual(Rectangle2 rectangle)
            {
                Rectangle = rectangle;
                CreateVisualElements();
            }

            private void CreateVisualElements()
            {
                // 创建矩形路径
                RectanglePath = new Path
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };

                UpdateGeometry(1, 1);
            }

            public void UpdateGeometry(double scaleX, double scaleY)
            {
                // 应用图像缩放到矩形尺寸
                var scaledWidth = Rectangle.Width * scaleX;
                var scaledHeight = Rectangle.Height * scaleY;

                var rect = new RectangleGeometry(new Rect(-scaledWidth / 2, -scaledHeight / 2, scaledWidth, scaledHeight));

                // 应用旋转变换
                if (Math.Abs(Rectangle.Angle) > 0.001)
                {
                    var rotateTransform = new RotateTransform(Rectangle.Angle);
                    rect.Transform = rotateTransform;
                }

                RectanglePath.Data = rect;
            }

            public void SetSelected(bool selected)
            {
                IsSelected = selected;
                var color = selected ? Brushes.Red : Brushes.Yellow;
                RectanglePath.Stroke = color;
            }
        }

        #endregion

        #region 视觉元素管理方法

        // Point视觉元素管理方法
        private void CreatePointVisual(Point2 point)
        {
            if (_pointVisuals.ContainsKey(point))
                return;

            var visual = new PointVisual(point);
            _pointVisuals[point] = visual;

            // 只有在Points可交互时才添加事件处理
            if (PointsInteractive)
            {
                visual.CrossPath.MouseLeftButtonDown += (s, e) => OnPointMouseDown(point, e);
                visual.CrossPath.MouseLeftButtonUp += (s, e) => OnPointMouseUp(point, e);
                visual.CrossPath.MouseMove += (s, e) => OnPointMouseMove(point, e);
            }

            // 设置Z-Index，确保Point2显示在图片之上
            Panel.SetZIndex(visual.CrossPath, 200);

            // 添加到Canvas
            _canvas.Children.Add(visual.CrossPath);

            UpdatePointVisual(point);
        }

        private void RemovePointVisual(Point2 point)
        {
            if (_pointVisuals.TryGetValue(point, out var visual))
            {
                // 确保从Canvas中移除
                if (_canvas.Children.Contains(visual.CrossPath))
                {
                    _canvas.Children.Remove(visual.CrossPath);
                }

                _pointVisuals.Remove(point);

                if (_selectedPointVisual == visual)
                {
                    _selectedPointVisual = null;
                }
            }
        }

        private void ClearAllPointVisuals()
        {
            foreach (var visual in _pointVisuals.Values)
            {
                _canvas.Children.Remove(visual.CrossPath);
            }
            _pointVisuals.Clear();
            _selectedPointVisual = null;
        }

        private void UpdatePointVisual(Point2 point)
        {
            if (!_pointVisuals.TryGetValue(point, out var visual) || _image.Source == null)
                return;

            // 将Point2坐标转换为Canvas坐标
            var canvasPoint = ImageToCanvasCoordinates(new System.Windows.Point(point.X, point.Y));

            // 更新十字线位置
            Canvas.SetLeft(visual.CrossPath, canvasPoint.X);
            Canvas.SetTop(visual.CrossPath, canvasPoint.Y);
        }

        private void UpdateAllPointVisuals()
        {
            if (Points == null) return;

            foreach (var point in Points)
            {
                UpdatePointVisual(point);
            }
        }

        // 矩形视觉元素管理方法
        private void CreateRectangleVisual(Rectangle2 rectangle)
        {
            if (_rectangleVisuals.ContainsKey(rectangle))
                return;

            var visual = new RectangleVisual(rectangle);
            _rectangleVisuals[rectangle] = visual;

            // 设置Z-Index，确保矩形显示在图片之上，但在点之下
            Panel.SetZIndex(visual.RectanglePath, 150);

            // 添加到Canvas
            _canvas.Children.Add(visual.RectanglePath);

            UpdateRectangleVisual(rectangle);
        }

        private void RemoveRectangleVisual(Rectangle2 rectangle)
        {
            if (_rectangleVisuals.TryGetValue(rectangle, out var visual))
            {
                // 确保从Canvas中移除
                if (_canvas.Children.Contains(visual.RectanglePath))
                {
                    _canvas.Children.Remove(visual.RectanglePath);
                }

                _rectangleVisuals.Remove(rectangle);

                if (_selectedRectangleVisual == visual)
                {
                    _selectedRectangleVisual = null;
                }
            }
        }

        private void ClearAllRectangleVisuals()
        {
            foreach (var visual in _rectangleVisuals.Values)
            {
                _canvas.Children.Remove(visual.RectanglePath);
            }
            _rectangleVisuals.Clear();
            _selectedRectangleVisual = null;
        }

        private void UpdateRectangleVisual(Rectangle2 rectangle)
        {
            if (!_rectangleVisuals.TryGetValue(rectangle, out var visual) || _image.Source == null)
                return;

            // 更新几何体，传入当前缩放比例
            visual.UpdateGeometry(_scaleTransform.ScaleX, _scaleTransform.ScaleY);

            // 将矩形中心坐标转换为Canvas坐标
            var canvasPoint = ImageToCanvasCoordinates(new System.Windows.Point(rectangle.CenterX, rectangle.CenterY));

            // 更新矩形位置
            Canvas.SetLeft(visual.RectanglePath, canvasPoint.X);
            Canvas.SetTop(visual.RectanglePath, canvasPoint.Y);
        }


        #endregion

        #region 坐标转换方法

        // 修改 ImageToCanvasCoordinates 以正确处理 RenderTransformOrigin
        private System.Windows.Point ImageToCanvasCoordinates(System.Windows.Point imagePoint)
        {
            if (_image.Source == null) return imagePoint;

            // 这是将图像坐标转换到Canvas坐标的变换
            var transform = _image.TransformToVisual(_canvas);
            return transform.Transform(imagePoint);
        }

        // 修改 CanvasToImageCoordinates
        private System.Windows.Point CanvasToImageCoordinates(System.Windows.Point canvasPoint)
        {
            if (_image.Source == null) return canvasPoint;

            var transform = _image.TransformToVisual(_canvas);
            // 需要求逆变换
            if (transform.Inverse != null)
            {
                var imagePoint = transform.Inverse.Transform(canvasPoint);

                // 限制在图像边界内
                imagePoint.X = Math.Max(0, Math.Min(_image.Source.Width, imagePoint.X));
                imagePoint.Y = Math.Max(0, Math.Min(_image.Source.Height, imagePoint.Y));

                return imagePoint;
            }
            return canvasPoint;
        }

        #endregion

        #region Point鼠标事件处理

        // Point2鼠标事件处理（只有在PointsInteractive为true时才响应）
        private void OnPointMouseDown(Point2 point, MouseButtonEventArgs e)
        {
            if (!PointsInteractive) return;

            if (_pointVisuals.TryGetValue(point, out var visual))
            {
                // 设置选中点
                if (SelectedPoint != point)
                {
                    SelectedPoint = point;
                }

                // 开始拖拽
                _isDraggingPoint = true;
                _draggingPoint = point;

                visual.CrossPath.CaptureMouse();

                e.Handled = true;
            }
        }

        private void OnPointMouseUp(Point2 point, MouseButtonEventArgs e)
        {
            if (!PointsInteractive) return;

            if (_isDraggingPoint && _draggingPoint == point)
            {
                _isDraggingPoint = false;
                _draggingPoint = null;

                if (_pointVisuals.TryGetValue(point, out var visual))
                {
                    visual.CrossPath.ReleaseMouseCapture();
                }

                e.Handled = true;
            }
        }

        private void OnPointMouseMove(Point2 point, MouseEventArgs e)
        {
            if (!PointsInteractive) return;

            if (_isDraggingPoint && _draggingPoint == point && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvasPoint = e.GetPosition(_canvas);
                var imagePoint = CanvasToImageCoordinates(canvasPoint);

                // 确保坐标在图片范围内
                if (_image.Source != null)
                {
                    imagePoint.X = Math.Max(0, Math.Min(_image.Source.Width, imagePoint.X));
                    imagePoint.Y = Math.Max(0, Math.Min(_image.Source.Height, imagePoint.Y));
                }

                // 更新Point2的坐标
                point.X = imagePoint.X;
                point.Y = imagePoint.Y;

                e.Handled = true;
            }
        }

        #endregion
    }

    #region 十字线位置调整对话框

    // 十字线位置调整对话框保持不变
    public partial class CrosshairPositionDialog : Window
    {
        public System.Windows.Point Position { get; private set; }

        private TextBox _xTextBox;
        private TextBox _yTextBox;
        private double _imageWidth;
        private double _imageHeight;

        public CrosshairPositionDialog(System.Windows.Point currentPosition, double imageWidth, double imageHeight)
        {
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
            Position = currentPosition;

            InitializeDialog();
        }

        private void InitializeDialog()
        {
            Title = "调整十字线位置";
            Width = 300;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // X坐标
            var xLabel = new Label { Content = "X坐标:", Margin = new Thickness(10, 10, 5, 5) };
            Grid.SetRow(xLabel, 0);
            Grid.SetColumn(xLabel, 0);
            grid.Children.Add(xLabel);

            _xTextBox = new TextBox
            {
                Text = Position.X.ToString("F2"),
                Margin = new Thickness(5, 10, 10, 5)
            };
            Grid.SetRow(_xTextBox, 0);
            Grid.SetColumn(_xTextBox, 1);
            grid.Children.Add(_xTextBox);

            // Y坐标
            var yLabel = new Label { Content = "Y坐标:", Margin = new Thickness(10, 5, 5, 5) };
            Grid.SetRow(yLabel, 1);
            Grid.SetColumn(yLabel, 0);
            grid.Children.Add(yLabel);

            _yTextBox = new TextBox
            {
                Text = Position.Y.ToString("F2"),
                Margin = new Thickness(5, 5, 10, 5)
            };
            Grid.SetRow(_yTextBox, 1);
            Grid.SetColumn(_yTextBox, 1);
            grid.Children.Add(_yTextBox);

            // 居中按钮
            var centerButton = new Button
            {
                Content = "居中",
                Width = 80,
                Height = 25,
                Margin = new Thickness(10, 10, 10, 5)
            };
            centerButton.Click += CenterButton_Click;
            Grid.SetRow(centerButton, 2);
            Grid.SetColumn(centerButton, 0);
            Grid.SetColumnSpan(centerButton, 2);
            grid.Children.Add(centerButton);

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new Button
            {
                Content = "确定",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsDefault = true
            };
            okButton.Click += OkButton_Click;

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };
            cancelButton.Click += CancelButton_Click;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 5);
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void CenterButton_Click(object sender, RoutedEventArgs e)
        {
            _xTextBox.Text = (_imageWidth / 2).ToString("F2");
            _yTextBox.Text = (_imageHeight / 2).ToString("F2");
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(_xTextBox.Text, out double x) &&
                double.TryParse(_yTextBox.Text, out double y))
            {
                // 确保坐标在图片范围内
                x = Math.Max(0, Math.Min(_imageWidth, x));
                y = Math.Max(0, Math.Min(_imageHeight, y));

                Position = new System.Windows.Point(x, y);
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("请输入有效的数字坐标！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    #endregion
}
