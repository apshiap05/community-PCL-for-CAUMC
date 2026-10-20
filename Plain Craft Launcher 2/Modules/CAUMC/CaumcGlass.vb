Imports System.Windows.Media.Effects

' A real two-stage backdrop: Gaussian blur, then a Snell refraction pixel shader.
' Content remains a sibling of this layer and is never supplied to the shader.
Public Class CaumcGlassSurface
    Inherits Grid

    Private ReadOnly Frost As Border
    Private ReadOnly Lens As Border
    Private ReadOnly LensEffect As New CaumcGlassEffect()
    Private ReadOnly Sample As New VisualBrush With {.ViewboxUnits = BrushMappingMode.Absolute, .Stretch = Stretch.Fill}
    Private ReadOnly Shape As New RectangleGeometry()
    Private LastBounds As Rect = Rect.Empty
    Private LastViewport As Rect = Rect.Empty
    Private LastSurfaceSize As Size = Size.Empty
    Private PointerHost As UIElement
    Private _Material As String = "Card"
    Private Density As Double = 0.52
    Private Thickness As Double = 5
    Private Bezel As Double = 10
    Private _RefractionEnabled As Boolean = True
    Private _BlurEnabled As Boolean = True
    Private BlurRadius As Double = 6

    Public Shared ReadOnly RadiusProperty As DependencyProperty = DependencyProperty.Register(
        "Radius", GetType(Double), GetType(CaumcGlassSurface), New PropertyMetadata(14.0,
        Sub(d As DependencyObject, e As DependencyPropertyChangedEventArgs)
            Dim Surface = DirectCast(d, CaumcGlassSurface)
            Surface.LastBounds = Rect.Empty
        End Sub))
    Public Property Radius As Double
        Get
            Return CDbl(GetValue(RadiusProperty))
        End Get
        Set(value As Double)
            SetValue(RadiusProperty, Math.Max(0, value))
        End Set
    End Property

    Public Property Material As String
        Get
            Return _Material
        End Get
        Set(value As String)
            _Material = value
            If Frost IsNot Nothing Then ConfigureMaterial()
        End Set
    End Property

    Friend Property RefractionEnabled As Boolean
        Get
            Return _RefractionEnabled
        End Get
        Set(value As Boolean)
            _RefractionEnabled = value
            LastBounds = Rect.Empty
            RefreshBackdrop()
        End Set
    End Property

    Friend Property GaussianBlurEnabled As Boolean
        Get
            Return _BlurEnabled
        End Get
        Set(value As Boolean)
            _BlurEnabled = value
            Frost.Effect = If(value, New BlurEffect With {.Radius = BlurRadius, .RenderingBias = RenderingBias.Performance}, Nothing)
        End Set
    End Property

    Public Sub New()
        IsHitTestVisible = False
        Clip = Shape
        Background = New LinearGradientBrush(Color.FromRgb(222, 235, 249), Color.FromRgb(240, 247, 255), 35)
        Frost = New Border With {.Background = Sample}
        ' BlurEffect expands its output bounds. Clip that expansion before the
        ' lens so shader UVs still describe the declared viewport precisely.
        Dim FrostHost As New Grid With {.ClipToBounds = True}
        FrostHost.Children.Add(Frost)
        Lens = New Border With {.Child = FrostHost, .Effect = LensEffect,
            .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top}
        Children.Add(Lens)
        HideLight()
        ConfigureMaterial()
        AddHandler LayoutUpdated, AddressOf RefreshSample
        AddHandler Loaded, AddressOf AttachPointer
        AddHandler Unloaded, AddressOf ReleaseBackdrop
    End Sub

    Protected Overrides Function MeasureOverride(Available As Size) As Size
        MyBase.MeasureOverride(Available)
        ' A material must not grow its host to accommodate the off-edge samples.
        ' The foreground alone defines the desired size of buttons and fields.
        Return New Size(0, 0)
    End Function

    Private Sub ConfigureMaterial()
        Dim Blur As Double
        Select Case Material
            Case "Control"
                Density = 0.86 : Thickness = 2.5 : Bezel = 6 : Blur = 8
            Case "Indicator"
                Density = 0.82 : Thickness = 2 : Bezel = 4 : Blur = 6
            Case "Navigation"
                Density = 0.52 : Thickness = 4.5 : Bezel = 10 : Blur = 12
            Case "Popup"
                Density = 0.90 : Thickness = 4 : Bezel = 10 : Blur = 16
            Case Else
                Density = 0.52 : Thickness = 5 : Bezel = 10 : Blur = 12
        End Select
        BlurRadius = Blur
        GaussianBlurEnabled = GaussianBlurEnabled
        LastBounds = Rect.Empty
    End Sub

    Private Sub AttachPointer(sender As Object, e As RoutedEventArgs)
        PointerHost = TryCast(VisualTreeHelper.GetParent(Me), UIElement)
        If PointerHost Is Nothing Then Return
        AddHandler PointerHost.MouseMove, AddressOf TrackPointer
        AddHandler PointerHost.MouseLeave, AddressOf ClearPointer
    End Sub

    Private Sub TrackPointer(sender As Object, e As MouseEventArgs)
        MoveLight(e.GetPosition(Me))
    End Sub

    Private Sub ClearPointer(sender As Object, e As MouseEventArgs)
        HideLight()
    End Sub

    Private Sub ReleaseBackdrop(sender As Object, e As RoutedEventArgs)
        Sample.Visual = Nothing
        LastBounds = Rect.Empty
        If PointerHost IsNot Nothing Then
            RemoveHandler PointerHost.MouseMove, AddressOf TrackPointer
            RemoveHandler PointerHost.MouseLeave, AddressOf ClearPointer
            PointerHost = Nothing
        End If
    End Sub

    Friend Sub MoveLight(Position As Point)
        LensEffect.SetLight(New Vector(Position.X - ActualWidth * 0.5, Position.Y - ActualHeight * 0.5))
    End Sub

    Friend Sub HideLight()
        LensEffect.SetLight(New Vector(-1, -1))
    End Sub

    Private Sub RefreshSample(sender As Object, e As EventArgs)
        If ActualWidth <= 0 OrElse ActualHeight <= 0 Then Return
        Dim R = Math.Min(Radius, Math.Min(ActualWidth, ActualHeight) * 0.5)
        Shape.Rect = New Rect(0, 0, ActualWidth, ActualHeight)
        Shape.RadiusX = R
        Shape.RadiusY = R
        Dim Scene = If(FrmMain Is Nothing, Nothing, FrmMain.GlassScene)
        If Scene Is Nothing OrElse Scene.ActualWidth <= 0 OrElse Scene.ActualHeight <= 0 Then Return
        Try
            Dim ToScene As GeneralTransform
            If PresentationSource.FromVisual(Me) Is PresentationSource.FromVisual(Scene) Then
                ToScene = TransformToVisual(Scene)
            Else
                If Not IsLoaded OrElse Not Scene.IsLoaded Then Return
                ' Popups use another HWND, but share the UI dispatcher and scene.
                Dim Origin = Scene.PointFromScreen(PointToScreen(New Point(0, 0)))
                Dim EndPoint = Scene.PointFromScreen(PointToScreen(New Point(1, 1)))
                ToScene = New MatrixTransform(New Matrix(EndPoint.X - Origin.X, 0, 0, EndPoint.Y - Origin.Y, Origin.X, Origin.Y))
            End If
            Dim Visible = Rect.Intersect(ToScene.TransformBounds(New Rect(0, 0, ActualWidth, ActualHeight)),
                New Rect(0, 0, Scene.ActualWidth, Scene.ActualHeight))
            If Visible.IsEmpty OrElse Visible.Width <= 0 OrElse Visible.Height <= 0 Then
                Lens.Visibility = Visibility.Collapsed
                Sample.Visual = Nothing
                LastBounds = Rect.Empty
                Return
            End If
            ' Bounded allocation even for 20,000-pixel guide cards. Extra pixels
            ' are real background samples, so the rim can refract past the edge.
            Dim Local = ToScene.Inverse.TransformBounds(Visible)
            Const Padding As Double = 42
            Dim Viewport = New Rect(Local.X - Padding, Local.Y - Padding, Local.Width + Padding * 2, Local.Height + Padding * 2)
            Dim Bounds = ToScene.TransformBounds(Viewport)
            Dim SurfaceSize As New Size(ActualWidth, ActualHeight)
            ' A scrolling long card can cover the same scene bounds while the
            ' visible viewport moves within the card. Both coordinate spaces
            ' must match before reusing the lens placement and shader inputs.
            If Bounds = LastBounds AndAlso Viewport = LastViewport AndAlso SurfaceSize = LastSurfaceSize AndAlso Sample.Visual Is Scene Then Return
            Lens.Visibility = Visibility.Visible
            Lens.Width = Viewport.Width
            Lens.Height = Viewport.Height
            Lens.Margin = New Thickness(Viewport.X, Viewport.Y, 0, 0)
            LensEffect.Configure(Viewport, SurfaceSize, R, Math.Min(Bezel, Math.Max(1, R)),
                Thickness, Density, RefractionEnabled)
            LastBounds = Bounds
            LastViewport = Viewport
            LastSurfaceSize = SurfaceSize
            Sample.Viewbox = Bounds
            Sample.Visual = Scene
        Catch ex As InvalidOperationException
            Sample.Visual = Nothing
        End Try
    End Sub

    Friend Sub RefreshBackdrop()
        RefreshSample(Me, EventArgs.Empty)
    End Sub

    Friend ReadOnly Property HasBackdrop As Boolean
        Get
            Return Sample.Visual IsNot Nothing
        End Get
    End Property

    Friend ReadOnly Property BackdropExtent As Size
        Get
            Return New Size(If(Double.IsNaN(Lens.Width), 0, Lens.Width), If(Double.IsNaN(Lens.Height), 0, Lens.Height))
        End Get
    End Property
End Class

Friend Module CaumcGlass
    Friend Function CreateControlLayer(Host As Border, Optional Radius As Double = 10) As Grid
        Dim Layer As New Grid()
        Layer.Children.Add(New CaumcGlassSurface With {.Material = "Control", .Radius = Radius})
        Dim State As New Border With {.CornerRadius = New CornerRadius(Radius), .IsHitTestVisible = False}
        State.SetBinding(Border.BackgroundProperty, New Binding("Background") With {.Source = Host})
        Layer.Children.Add(State)
        Return Layer
    End Function

    Friend Sub RefreshTheme()
        If FrmMain Is Nothing OrElse FrmMain.GlassScene Is Nothing Then Return
        FrmMain.PanTitle.Background = Brushes.Transparent
        FrmMain.PanForm.Background = Brushes.Transparent
        FrmMain.GlassAtmosphere.Opacity = If(Settings.Get(Of Boolean)("UiBackgroundColorful"), Settings.Get(Of Integer)("UiBackgroundOpacity") / 1000, 0)
        FrmMain.GlassAtmosphere.UpdateRotation()
        FrmMain.LabTitleLogo.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        FrmMain.LabTitleInner.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        FrmMain.ShapeTitleLogo.SetResourceReference(Shapes.Path.StrokeProperty, "ColorBrush2")
    End Sub
End Module

