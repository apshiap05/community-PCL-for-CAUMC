Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Media.Imaging

Friend NotInheritable Class CaumcGuideImage
    Private Const Limit As Integer = 8 * 1024 * 1024
    Private Shared ReadOnly Http As New HttpClient(New HttpClientHandler With {.MaxConnectionsPerServer = 6}) With {.Timeout = TimeSpan.FromSeconds(30)}

    Friend Shared Function Create(Address As Uri, Alternative As String, Optional Client As HttpClient = Nothing) As FrameworkElement
        Dim Host As New StackPanel With {.MaxWidth = 700, .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(0, 6, 0, 6)}
        Dim Picture As New Image With {.MaxWidth = 700, .Stretch = Stretch.Uniform, .StretchDirection = StretchDirection.DownOnly,
            .HorizontalAlignment = HorizontalAlignment.Left, .Visibility = Visibility.Collapsed}
        Automation.AutomationProperties.SetName(Picture, Alternative)
        Dim Status As New TextBlock With {.Text = "正在加载图片……", .TextWrapping = TextWrapping.Wrap, .FontSize = 12, .MaxWidth = 600}
        Status.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        Host.Children.Add(Picture)
        Host.Children.Add(Status)
        Picture.Tag = LoadAsync(Host, Picture, Status, Address, If(Client, Http))
        Return Host
    End Function

    Private Shared Function Decode(Bytes As Byte()) As BitmapImage
        ' Never put a deferred network BitmapImage into an InlineUIContainer: its late
        ' decoder can leave a valid-sized but unpainted image in the FlowDocument.
        Using Stream As New MemoryStream(Bytes, False)
            Dim Bitmap As New BitmapImage()
            Bitmap.BeginInit()
            Bitmap.CacheOption = BitmapCacheOption.OnLoad
            Bitmap.DecodePixelWidth = 1400
            Bitmap.StreamSource = Stream
            Bitmap.EndInit()
            Bitmap.Freeze()
            Return Bitmap
        End Using
    End Function

    Private Shared Async Function LoadAsync(Host As StackPanel, Picture As Image, Status As TextBlock, Address As Uri, Client As HttpClient) As Task
        Using Cancel As New CancellationTokenSource()
            Cancel.CancelAfter(TimeSpan.FromSeconds(30))
            Dim Cache As String
            Using Hash = System.Security.Cryptography.SHA256.Create()
                Cache = Paths.Base & "PCLForCAUMC\Cache\Images\" & BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Address.AbsoluteUri))).Replace("-", "") & ".img"
            End Using
            Try
                If File.Exists(Cache) AndAlso New FileInfo(Cache).Length <= Limit Then
                    Try
                        Picture.Source = Decode(File.ReadAllBytes(Cache))
                        Picture.Visibility = Visibility.Visible
                        Status.Visibility = Visibility.Collapsed
                    Catch
                        ' A bad image cache must not prevent downloading a valid replacement.
                    End Try
                End If
                Using Request As New HttpRequestMessage(HttpMethod.Get, Address)
                    Request.Headers.UserAgent.ParseAdd("PCL-for-CAUMC/0.1")
                    Request.Headers.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
                    Using Response = Await Client.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead, Cancel.Token)
                        Response.EnsureSuccessStatusCode()
                        If Response.Content.Headers.ContentLength > Limit Then Throw New InvalidDataException("图片过大")
                        Using Stream = Await Response.Content.ReadAsStreamAsync(), Buffer As New MemoryStream()
                            Dim Block(16383) As Byte
                            While True
                                Dim Count = Await Stream.ReadAsync(Block, 0, Block.Length, Cancel.Token)
                                If Count = 0 Then Exit While
                                If Buffer.Length + Count > Limit Then Throw New InvalidDataException("图片过大")
                                Buffer.Write(Block, 0, Count)
                            End While
                            Cancel.Token.ThrowIfCancellationRequested()
                            Dim Bytes = Buffer.ToArray()
                            Dim Bitmap = Decode(Bytes)
                            Picture.Source = Bitmap
                            Picture.Visibility = Visibility.Visible
                            Status.Visibility = Visibility.Collapsed
                            Try
                                Directory.CreateDirectory(IO.Path.GetDirectoryName(Cache))
                                File.WriteAllBytes(Cache, Bytes)
                            Catch ex As Exception
                                Logger.Warn(ex, "保存指南图片缓存失败")
                            End Try
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                If Picture.Source Is Nothing Then
                    Status.Text = "图片加载失败，请刷新重试，或点击「浏览器查看原文」。"
                Else
                    Status.Text = "图片更新失败，正在显示缓存。"
                End If
                Status.Visibility = Visibility.Visible
                Logger.Warn(ex, "加载指南图片失败：" & Address.AbsoluteUri)
            End Try
        End Using
    End Function
End Class
