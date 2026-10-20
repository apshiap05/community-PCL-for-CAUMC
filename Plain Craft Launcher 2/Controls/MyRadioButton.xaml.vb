Imports System.Windows.Markup

<ContentProperty("Inlines")>
Public Class MyRadioButton

    '基础

    Public Uuid As Integer = GetUuid()
    Public Event Check(sender As Object, raiseByMouse As Boolean)

    '自定义属性

    Public Property Logo As String
        Get
            Return ShapeLogo.Data.ToString
        End Get
        Set(value As String)
            ShapeLogo.Data = (New GeometryConverter).ConvertFromString(value)
        End Set
    End Property
    Private _LogoScale As Double = 1
    Public Property LogoScale() As Double
        Get
            Return _LogoScale
        End Get
        Set(value As Double)
            _LogoScale = value
            If Not IsNothing(ShapeLogo) Then ShapeLogo.RenderTransform = New ScaleTransform With {.ScaleX = LogoScale, .ScaleY = LogoScale}
        End Set
    End Property

    Private _Checked As Boolean = False '是否选中
    Public Property Checked As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            SetChecked(value, False, True)
        End Set
    End Property
    ''' <summary>
    ''' 手动设置 Checked 属性。
    ''' </summary>
    ''' <param name="value">新的 Checked 属性。</param>
    ''' <param name="raiseByMouse">是否由用户引发。</param>
    ''' <param name="anime">是否执行动画。</param>
    Public Sub SetChecked(value As Boolean, raiseByMouse As Boolean, anime As Boolean)
        Try

            '自定义属性基础

            Dim IsChanged As Boolean = False
            If _Checked <> value Then
                _Checked = value
                IsChanged = True
            End If

            '保证只有一个单选框选中

            If Parent Is Nothing Then Return
            Dim RadioboxList As New List(Of MyRadioButton)
            Dim CheckedCount As Integer = 0
            '收集控件列表与选中个数
            For Each Control In CType(Parent, Object).Children
                If TypeOf Control Is MyRadioButton Then
                    RadioboxList.Add(Control)
                    If Control.Checked Then CheckedCount += 1
                End If
            Next
            '判断选中情况
            Select Case CheckedCount
                Case 0
                    '没有任何单选框被选中，选择第一个
                    RadioboxList(0).Checked = True
                Case Is > 1
                    '选中项目多于 1 个
                    If Me.Checked Then
                        '如果本控件选中，则取消其他所有控件的选中
                        For Each Control As MyRadioButton In RadioboxList
                            If Control.Checked AndAlso Not Control.Equals(Me) Then Control.Checked = False
                        Next
                    Else
                        '如果本控件未选中，则只保留第一个选中的控件
                        Dim FirstChecked = False
                        For Each Control As MyRadioButton In RadioboxList
                            If Control.Checked Then
                                If FirstChecked Then
                                    Control.Checked = False '修改 Checked 会自动触发 Change 事件，所以不用额外触发
                                Else
                                    FirstChecked = True
                                End If
                            End If
                        Next
                    End If
            End Select

            '更改动画

            If Not IsChanged Then Return
            RefreshColor(Nothing, anime)

            '触发事件
            If Checked Then RaiseEvent Check(Me, raiseByMouse)
            RaiseCustomEvent()

        Catch ex As Exception
            Logger.Error(ex, "单选按钮勾选改变错误", LogBehavior.Toast)
        End Try
    End Sub

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
    End Property '内容
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyRadioButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender, e) If sender IsNot Nothing Then CType(sender, MyRadioButton).LabText.Text = e.NewValue)))
    Public Enum ColorState
        White
        Highlight
    End Enum
    Private _ColorType As ColorState = ColorState.White
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
        End Set
    End Property '颜色类别

    '点击事件

    Public Event PreviewClick(sender As Object, e As RouteEventArgs)
    Private IsMouseDown As Boolean = False
    Private Sub Radiobox_MouseUp() Handles Me.MouseLeftButtonUp
        If Checked Then Return
        If Not IsMouseDown Then Return
        Logger.Info($"按下单选按钮：{Text}")
        IsMouseDown = False
        Dim e As New RouteEventArgs(True)
        RaiseEvent PreviewClick(Me, e)
        If e.Handled Then Return
        SetChecked(True, True, True)
    End Sub
    Private Sub Radiobox_MouseDown() Handles Me.MouseLeftButtonDown
        If Checked Then Return
        IsMouseDown = True
        RefreshColor()
    End Sub
    Private Sub Radiobox_MouseLeave() Handles Me.MouseLeave
        IsMouseDown = False
    End Sub

    '动画

    Private Const AnimationTimeOfMouseIn As Integer = 90 '鼠标指向动画长度
    Private Const AnimationTimeOfMouseOut As Integer = 150 '鼠标移出动画长度
    Private Const AnimationTimeOfCheck As Integer = 120 '勾选状态变更动画长度
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded
        Try
            If ShapeLogo Is Nothing OrElse LabText Is Nothing Then Return
            If GlassFace IsNot Nothing Then GlassFace.Visibility = If(Checked OrElse IsMouseOver, Visibility.Visible, Visibility.Collapsed)
            Dim Fore = If(Checked OrElse IsMouseOver, "GlassAccentInk", "ColorBrush1")
            Dim Fill = If(Checked, "GlassControlSelected", If(IsMouseOver, "GlassControlHover", "ColorBrushTransparent"))
            If IsLoaded AndAlso AniControlEnabled = 0 AndAlso Not False.Equals(e) Then
                AniStart({AaColor(ShapeLogo, Shapes.Path.FillProperty, Fore, AnimationTimeOfCheck),
                    AaColor(LabText, TextBlock.ForegroundProperty, Fore, AnimationTimeOfCheck),
                    AaColor(Me, BackgroundProperty, Fill, AnimationTimeOfCheck)}, "MyRadioButton Checked " & Uuid)
            Else
                AniStop("MyRadioButton Checked " & Uuid)
                ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, Fore)
                LabText.SetResourceReference(TextBlock.ForegroundProperty, Fore)
                SetResourceReference(BackgroundProperty, Fill)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新玻璃导航颜色出错")
        End Try
    End Sub

End Class
