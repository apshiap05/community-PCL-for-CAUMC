Imports System.Windows.Markup

<ContentProperty("Inlines")>
Public Class MyButton

    '声明
    Public Event Click(sender As Object, e As MouseButtonEventArgs) '自定义事件

    '自定义属性
    Public Uuid As Integer = GetUuid()
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return LabText.Inlines
        End Get
    End Property
    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set(value As String)
            SetValue(TextProperty, value)
        End Set
    End Property '显示文本
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender As DependencyObject, e As DependencyPropertyChangedEventArgs)
        If sender IsNot Nothing Then CType(sender, MyButton).LabText.Text = e.NewValue
    End Sub)))
    Public Property TextPadding As Thickness
        Get
            Return LabText.Padding
        End Get
        Set(value As Thickness)
            LabText.Padding = value
        End Set
    End Property
    Private _ColorType As ColorState = ColorState.Normal  '配色方案
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
        End Set
    End Property
    Public Enum ColorState
        Normal = 0
        Highlight = 1
        Red = 2
    End Enum
    Private _IsPrimary As Boolean
    Public Property IsPrimary As Boolean
        Get
            Return _IsPrimary
        End Get
        Set(value As Boolean)
            _IsPrimary = value
            RefreshColor()
        End Set
    End Property
    '属性穿透
    Public Shared Shadows ReadOnly PaddingProperty As DependencyProperty = DependencyProperty.Register("Padding", GetType(Thickness), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
        Sub(sender As MyButton, e As DependencyPropertyChangedEventArgs) If sender IsNot Nothing Then sender.LabText.Margin = e.NewValue)))
    Public Overloads Property Padding As Thickness
        Get
            Return LabText.Margin
        End Get
        Set(value As Thickness)
            LabText.Margin = value
        End Set
    End Property
    Public Property RealRenderTransform As Transform
        Get
            Return PanFore.RenderTransform
        End Get
        Set(value As Transform)
            PanFore.RenderTransform = value
        End Set
    End Property

    '自定义事件
    Private Const AnimationColorIn As Integer = 100
    Private Const AnimationColorOut As Integer = 200
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged, Me.GotKeyboardFocus, Me.LostKeyboardFocus
        Try
            If PanFore Is Nothing OrElse LabText Is Nothing Then Return
            Dim Active = IsMouseOver OrElse IsKeyboardFocusWithin
            LabText.FontWeight = If(IsPrimary, FontWeights.SemiBold, FontWeights.Normal)
            Dim Fore = If(Not IsEnabled, "ColorBrushGray3", If(IsPrimary, "ColorBrushWhite",
                If(ColorType = ColorState.Red, "ColorBrushRedDark", If(ColorType = ColorState.Highlight OrElse Active, "GlassAccentInk", "ColorBrush1"))))
            Dim Fill = If(Not IsEnabled, "GlassControlDisabled", If(IsPrimary,
                If(Active, "GlassPrimaryHoverFill", "GlassPrimaryFill"), If(ColorType = ColorState.Red AndAlso Active,
                "ColorBrushRedBack", If(Active, "GlassControlHover", If(ColorType = ColorState.Highlight, "GlassControlSelected", "GlassControlFill")))))
            Dim Stroke = If(IsEnabled AndAlso (Active OrElse ColorType = ColorState.Highlight OrElse IsPrimary), "GlassControlAccentStroke", "GlassControlStroke")
            If IsLoaded AndAlso AniControlEnabled = 0 Then
                Dim Duration = If(IsMouseOver, AnimationColorIn, AnimationColorOut)
                AniStart({AaColor(LabText, TextBlock.ForegroundProperty, Fore, Duration),
                    AaColor(PanFore, Border.BackgroundProperty, Fill, Duration),
                    AaColor(PanFore, Border.BorderBrushProperty, Stroke, Duration)}, "MyButton Color " & Uuid)
            Else
                AniStop("MyButton Color " & Uuid)
                LabText.SetResourceReference(TextBlock.ForegroundProperty, Fore)
                PanFore.SetResourceReference(Border.BackgroundProperty, Fill)
                PanFore.SetResourceReference(Border.BorderBrushProperty, Stroke)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新按钮颜色出错")
        End Try
    End Sub

    '鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        Logger.Info($"按下按钮：{Text}")
        RaiseEvent Click(sender, e)
        RaiseCustomEvent() '自定义事件
    End Sub
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        IsMouseDown = True
        Focus()
        AniStart({
                 AaScaleTransform(PanFore, 0.976 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 80,, New AniEaseOutFluent(AniEasePower.ExtraStrong))
                 }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart({
               AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 220, 10, New AniEaseOutFluent(AniEasePower.Middle))
           }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart(AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 180,, New AniEaseOutFluent(AniEasePower.Strong)), "MyButton Scale " & Uuid)
    End Sub

End Class
