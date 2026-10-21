Imports System.Windows.Media.Effects
Imports System.Windows.Media.Media3D

Public Class CaumcGlassEffect
    Inherits ShaderEffect

    Private Shared ReadOnly Program As New PixelShader With {
        .UriSource = New Uri("/Plain Craft Launcher 2;component/Resources/LiquidGlass.ps", UriKind.Relative)}
    Public Shared ReadOnly InputProperty As DependencyProperty = RegisterPixelShaderSamplerProperty("Input", GetType(CaumcGlassEffect), 0, SamplingMode.Bilinear)
    Public Shared ReadOnly ViewportProperty As DependencyProperty = DependencyProperty.Register("Viewport", GetType(Point4D), GetType(CaumcGlassEffect), New UIPropertyMetadata(New Point4D(1, 1, 0, 0), PixelShaderConstantCallback(0)))
    Public Shared ReadOnly ShapeProperty As DependencyProperty = DependencyProperty.Register("Shape", GetType(Point4D), GetType(CaumcGlassEffect), New UIPropertyMetadata(New Point4D(1, 1, 1, 1), PixelShaderConstantCallback(1)))
    Public Shared ReadOnly OpticsProperty As DependencyProperty = DependencyProperty.Register("Optics", GetType(Point4D), GetType(CaumcGlassEffect), New UIPropertyMetadata(New Point4D(24, 0.045, 1.12, 1), PixelShaderConstantCallback(2)))
    Public Shared ReadOnly LightProperty As DependencyProperty = DependencyProperty.Register("Light", GetType(Point4D), GetType(CaumcGlassEffect), New UIPropertyMetadata(New Point4D(-0.707, -0.707, 0.32, 0), PixelShaderConstantCallback(3)))
    Public Shared ReadOnly TintProperty As DependencyProperty = DependencyProperty.Register("Tint", GetType(Point4D), GetType(CaumcGlassEffect), New UIPropertyMetadata(New Point4D(1, 1, 1, 0.30), PixelShaderConstantCallback(4)))

    Public Sub New()
        PixelShader = Program
        For Each Item In {InputProperty, ViewportProperty, ShapeProperty, OpticsProperty, LightProperty, TintProperty}
            UpdateShaderValue(Item)
        Next
    End Sub

    Friend Sub Configure(Viewport As Rect, Size As Size, Radius As Double, Bezel As Double, Thickness As Double, Density As Double, Enabled As Boolean)
        SetValue(ViewportProperty, New Point4D(Viewport.Width, Viewport.Height, Viewport.X, Viewport.Y))
        SetValue(ShapeProperty, New Point4D(Size.Width, Size.Height, Math.Max(0, Radius), Math.Max(1, Bezel)))
        SetValue(OpticsProperty, New Point4D(Thickness, 0.012, 1.02, If(Enabled, 1, 0)))
        SetValue(TintProperty, New Point4D(1, 1, 1, Density))
    End Sub

    Friend Sub SetLight(Direction As Vector)
        If Direction.Length < 1 Then Direction = New Vector(-1, -1)
        Direction.Normalize()
        SetValue(LightProperty, New Point4D(Direction.X, Direction.Y, 0.065, 0))
    End Sub

End Class
