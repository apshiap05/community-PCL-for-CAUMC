Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading

' Photos are WPF resources in the executable, independent of the source folder.
' Keep only the current and next decoded frame; the shared glass scene samples
' this visual, so every page and control follows the same crossfade.
Public Class CaumcGlassBackdrop
    Inherits FrameworkElement

    Private Const ResourceRoot As String = "/Plain Craft Launcher 2;component/Resources/Backgrounds/"
    Private Shared ReadOnly PhotoNames As String() = ReadPhotoNames()
    Private ReadOnly Rotation As New DispatcherTimer(DispatcherPriority.Background) With {.Interval = TimeSpan.FromSeconds(5)}
    Private CurrentPhoto As BitmapSource
    Private NextPhoto As BitmapSource
    Private CurrentIndex As Integer
    Private NextIndex As Integer
    Private Owner As Window
    Friend Shared ReadOnly FadeProperty As DependencyProperty = DependencyProperty.Register("Fade", GetType(Double), GetType(CaumcGlassBackdrop),
        New FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Sub New()
        CurrentPhoto = LoadPhoto(0)
        AddHandler Rotation.Tick, Sub() AdvancePhoto()
        AddHandler Loaded, AddressOf AttachWindow
        AddHandler Unloaded, AddressOf DetachWindow
        AddHandler IsVisibleChanged, Sub() UpdateRotation()
    End Sub

    Private Shared Function ReadPhotoNames() As String()
        Using Reader As New IO.StreamReader(Application.GetResourceStream(New Uri(ResourceRoot & "index.txt", UriKind.Relative)).Stream)
            Dim Names = Reader.ReadToEnd().Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
            If Names.Length = 0 OrElse Names.Any(Function(Name) Not System.Text.RegularExpressions.Regex.IsMatch(Name, "^scene-[0-9]+\.jpg$")) Then
                Throw New IO.InvalidDataException("内嵌背景图片目录无效")
            End If
            Return Names
        End Using
    End Function

    Private Shared Function LoadPhoto(Index As Integer) As BitmapSource
        Using Stream = Application.GetResourceStream(New Uri(ResourceRoot & PhotoNames(Index), UriKind.Relative)).Stream
            Dim Image As New BitmapImage()
            Image.BeginInit()
            Image.CacheOption = BitmapCacheOption.OnLoad
            Image.StreamSource = Stream
            Image.EndInit()
            Image.Freeze()
            Return Image
        End Using
    End Function

    Private Sub AttachWindow(sender As Object, e As RoutedEventArgs)
        Owner = Window.GetWindow(Me)
        If Owner IsNot Nothing Then AddHandler Owner.StateChanged, AddressOf OwnerStateChanged
        UpdateRotation()
    End Sub

    Private Sub DetachWindow(sender As Object, e As RoutedEventArgs)
        Rotation.Stop()
        CompleteTransition()
        If Owner IsNot Nothing Then RemoveHandler Owner.StateChanged, AddressOf OwnerStateChanged
        Owner = Nothing
    End Sub

    Private Sub OwnerStateChanged(sender As Object, e As EventArgs)
        UpdateRotation()
    End Sub

    Friend Sub UpdateRotation()
        If IsLoaded AndAlso IsVisible AndAlso Opacity > 0 AndAlso Owner IsNot Nothing AndAlso Owner.WindowState <> WindowState.Minimized AndAlso PhotoNames.Length > 1 Then
            Rotation.Start()
        Else
            Rotation.Stop()
            CompleteTransition()
        End If
    End Sub

    Friend Sub AdvancePhoto(Optional Animate As Boolean = True)
        If NextPhoto IsNot Nothing OrElse PhotoNames.Length < 2 Then Return
        NextIndex = (CurrentIndex + 1) Mod PhotoNames.Length
        NextPhoto = LoadPhoto(NextIndex)
        If Animate Then
            Dim Transition As New DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2)) With {.FillBehavior = FillBehavior.Stop}
            AddHandler Transition.Completed, Sub() CompleteTransition()
            BeginAnimation(FadeProperty, Transition)
        Else
            CompleteTransition()
        End If
    End Sub

    Friend Sub CompleteTransition()
        BeginAnimation(FadeProperty, Nothing)
        If NextPhoto IsNot Nothing Then
            CurrentPhoto = NextPhoto
            CurrentIndex = NextIndex
            NextPhoto = Nothing
        End If
        SetValue(FadeProperty, 0.0)
        InvalidateVisual()
    End Sub

    Protected Overrides Sub OnRender(Context As DrawingContext)
        MyBase.OnRender(Context)
        If ActualWidth <= 0 OrElse ActualHeight <= 0 Then Return
        Context.PushClip(New RectangleGeometry(New Rect(0, 0, ActualWidth, ActualHeight)))
        DrawPhoto(Context, CurrentPhoto)
        If NextPhoto IsNot Nothing Then
            Context.PushOpacity(Math.Max(0, Math.Min(1, CDbl(GetValue(FadeProperty)))))
            DrawPhoto(Context, NextPhoto)
            Context.Pop()
        End If
        Context.DrawRectangle(New SolidColorBrush(Color.FromArgb(25, 15, 31, 50)), Nothing, New Rect(0, 0, ActualWidth, ActualHeight))
        Context.Pop()
    End Sub

    Private Sub DrawPhoto(Context As DrawingContext, Photo As BitmapSource)
        Dim Scale = Math.Max(ActualWidth / Photo.PixelWidth, ActualHeight / Photo.PixelHeight)
        Dim Width = Photo.PixelWidth * Scale, Height = Photo.PixelHeight * Scale
        Context.DrawImage(Photo, New Rect((ActualWidth - Width) / 2, (ActualHeight - Height) / 2, Width, Height))
    End Sub

    Friend ReadOnly Property PhotoCount As Integer
        Get
            Return PhotoNames.Length
        End Get
    End Property
    Friend ReadOnly Property CurrentPhotoName As String
        Get
            Return PhotoNames(CurrentIndex)
        End Get
    End Property
    Friend ReadOnly Property CachedPhotoCount As Integer
        Get
            Return If(CurrentPhoto Is Nothing, 0, 1) + If(NextPhoto Is Nothing, 0, 1)
        End Get
    End Property
    Friend ReadOnly Property RotationRunning As Boolean
        Get
            Return Rotation.IsEnabled
        End Get
    End Property
End Class
