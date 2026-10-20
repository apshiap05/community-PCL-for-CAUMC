Imports System.Reflection

' Called only by the explicit developer self-test entry point. All input is copied
' into the self-test's own game directory before any file operation is performed.
Friend Module CaumcRegression
    Friend Sub RunGlassPreviews(Results As List(Of String))
        ' Render the real window frame and real page controls without its Loaded
        ' lifecycle, so no player account or page download/search is started.
        Dim Main = FrmMain
        Dim OldLeft = Main.PanMainLeft.Child
        Dim OldRight = Main.PanMainRight.Child
        Dim OldLeftWidth = Main.PanMainLeft.Width
        Dim OldRightWidth = Main.PanMainRight.Width
        Dim OldDismissed = Settings.Get(Of Boolean)("CaumcGuideDismissed")
        Dim OldClip = Main.PanForm.Clip
        Dim OldLinkVisibility = Main.BtnTitleSelect2.Visibility
        Try
            Settings.Set("CaumcGuideDismissed", True)
            Main.ShapeTitleLogo.Visibility = Visibility.Collapsed
            Main.LabTitleLogo.Visibility = Visibility.Visible
            Main.LabTitleLogo.Text = CaumcName
            Main.BtnTitleSelect2.Visibility = Visibility.Collapsed
            Main.PanMainLeft.Child = FrmLaunchLeft
            Dim GuidePanel As New StackPanel With {.Margin = New Thickness(25, 25, 25, 10)}
            Dim Snapshot As New PCLCS.CAUMC.ArticleSnapshot With {.Source = CaumcArticle, .CheckedAt = DateTimeOffset.Now,
                .Nodes = PCLCS.CAUMC.ArticleParser.Parse(File.ReadAllText(Paths.Base & "homepage.html"), New Uri(CaumcArticle))}
            CaumcHomepage.BuildPage(GuidePanel, Snapshot)
            Dim GuideViewer As New MyScrollViewer With {.Content = GuidePanel, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
            Main.PanMainRight.Child = GuideViewer
            RenderGlassFrame(1000, 640, 300, "home", Results)
            AssertRegression(GuidePanel.Children.OfType(Of MyCard)().All(Function(Item) Item.GlassMaterial = "Navigation"),
                "首页指南和左侧登录区使用相同透明材质", Results)
            RunBackgroundChecks(Results)
            RunWindowCornerChecks(Results)
            RenderGlassFrame(774, 434, 300, "home-minimum", Results)

            Dim Dialog As New MyMsgText(New MyMsgBoxConverter With {.Title = "准备开始游玩", .Text = "登录 CAU 皮肤站后，导入社区提供的整合包。启动 Minecraft，再从多人游戏进入 CAUMC。", .Button1 = "知道了", .Button2 = "取消", .Button3 = ""})
            Dialog.TransformRotate.Angle = 0
            Dialog.TransformPos.Y = 0
            Main.PanMsg.Background = New SolidColorBrush(Color.FromArgb(90, 0, 0, 0))
            Main.PanMsg.Visibility = Visibility.Visible
            Main.PanMsg.Children.Add(Dialog)
            RenderGlassFrame(1000, 640, 300, "dialog", Results)
            Main.PanMsg.Children.Remove(Dialog)
            Main.PanMsg.Visibility = Visibility.Collapsed
            Main.PanMsg.Background = Brushes.Transparent

            Main.PanMainLeft.Child = New PageSetupLeft()
            Main.PanMainRight.Child = New PageSetupLaunch()
            RenderGlassFrame(1000, 640, 160, "settings-launch", Results)
            Main.PanMainRight.Child = New PageSetupUI()
            RenderGlassFrame(1000, 640, 160, "settings-appearance", Results)
            Main.PanMainRight.Child = New PageSetupSystem()
            RenderGlassFrame(1000, 640, 160, "settings-system", Results)
            Main.PanMainRight.Child = New PageInstanceSetup()
            RenderGlassFrame(1000, 640, 160, "instance-settings", Results)
            Dim Mods As New PageInstanceMod()
            Mods.PanAllBack.Visibility = Visibility.Visible : Mods.PanAllBack.Opacity = 1
            Mods.PanLoad.Visibility = Visibility.Collapsed
            Mods.PanEmpty.Visibility = Visibility.Visible : Mods.PanEmpty.Opacity = 1
            Main.PanMainRight.Child = Mods
            RenderGlassFrame(1000, 640, 160, "instance-mods", Results)
            Main.PanMainLeft.Child = New PageSelectLeft()
            Dim Versions As New PageSelectRight()
            Versions.PanAllBack.Visibility = Visibility.Visible : Versions.PanAllBack.Opacity = 1
            Versions.PanLoad.Visibility = Visibility.Collapsed
            Versions.PanEmpty.Visibility = Visibility.Visible : Versions.PanEmpty.Opacity = 1
            Main.PanMainRight.Child = Versions
            RenderGlassFrame(1000, 640, 160, "versions", Results)

            Main.PanMainLeft.Child = New PageDownloadLeft()
            Main.PanMainRight.Child = New PageDownloadMod()
            RenderGlassFrame(1000, 640, 160, "downloads", Results)
            Main.PanMainRight.Child = New PageDownloadInstall()
            RenderGlassFrame(1000, 640, 160, "download-install", Results)
            RunControlGallery(Results)
            Main.PanMainLeft.Child = New PageOtherLeft()
            Main.PanMainRight.Child = New PageOtherAbout()
            RenderGlassFrame(1000, 640, 160, "about", Results)

            ' Use sharply alternating background bands to verify that surfaces
            ' sample the scene, follow its changes, and leave content unblurred.
            Dim OriginalBackground = Main.ImgBack.Background
            Dim OriginalOpacity = Main.ImgBack.Opacity
            Try
                Dim Bands As New DrawingGroup()
                For Index = 0 To 39
                    Bands.Children.Add(New GeometryDrawing(If(Index Mod 2 = 0, Brushes.DarkBlue, Brushes.White), Nothing,
                        New RectangleGeometry(New Rect(Index * 25, 0, 25, 640))))
                Next
                Main.ImgBack.Background = New DrawingBrush(Bands) With {.Stretch = Stretch.Fill}
                Main.ImgBack.Opacity = 1
                Main.PanMainLeft.Child = Nothing
                Dim SampleCard As New MyCard With {.Title = "清晰前景 · 背景采样", .Margin = New Thickness(60), .Height = 250, .VerticalAlignment = VerticalAlignment.Top}
                SampleCard.Children.Add(New TextBlock With {.Text = "PCL for CAUMC", .FontSize = 32, .Margin = New Thickness(25, 80, 25, 25), .Effect = Nothing})
                Dim OpticalButton As New MyButton With {.Text = "折射按钮", .Width = 200, .Height = 42,
                    .Margin = New Thickness(25, 160, 0, 0), .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top}
                SampleCard.Children.Add(OpticalButton)
                Main.PanMainRight.Child = SampleCard
                RenderGlassFrame(1000, 640, 160, "material-bands", Results)
                Dim Surfaces = VisualDescendants(Main.PanForm).OfType(Of CaumcGlassSurface)().Where(Function(Item) Item.ActualWidth > 10).ToList()
                AssertRegression(Surfaces.Where(Function(Item) Item.HasBackdrop).Count() >= 2 AndAlso VisualDescendants(SampleCard).OfType(Of CaumcGlassSurface)().All(Function(Item) Item.HasBackdrop), "玻璃表面实际采样独立背景且无递归前景", Results)
                AssertRegression(VisualDescendants(SampleCard).OfType(Of TextBlock)().All(Function(Item) Item.Effect Is Nothing), "正文未被高斯模糊处理", Results)
                Dim BandImage = RenderGlassImage()
                Dim CardSurface = VisualDescendants(SampleCard).OfType(Of CaumcGlassSurface)().First()
                Dim ProbeEffect = DirectCast(VisualDescendants(CardSurface).OfType(Of Border)().First().Effect, CaumcGlassEffect)
                File.WriteAllText(Paths.Base & "optics-probe.txt", LayoutBounds(CardSurface, Main.PanForm).ToString() & vbCrLf &
                    ProbeEffect.GetValue(CaumcGlassEffect.ViewportProperty).ToString() & vbCrLf &
                    ProbeEffect.GetValue(CaumcGlassEffect.ShapeProperty).ToString() & vbCrLf &
                    ProbeEffect.GetValue(CaumcGlassEffect.OpticsProperty).ToString())
                CardSurface.RefractionEnabled = False
                SaveLayoutPreview(Main.PanForm, 1000, 640, "refraction-off")
                Dim NoRefraction = RenderGlassImage()
                Dim EdgeChanges As Integer = 0, InteriorChanges As Integer = 0
                For X = 222 To 243
                    For Y = 150 To 320 Step 3
                        If PixelDifference(BandImage, NoRefraction, X, Y) > 2 Then EdgeChanges += 1
                    Next
                Next
                For X = 480 To 650 Step 5
                    For Y = 260 To 315 Step 5
                        If PixelDifference(BandImage, NoRefraction, X, Y) > 1 Then InteriorChanges += 1
                    Next
                Next
                File.WriteAllText(Paths.Base & "refraction-probe.json", New JObject From {{"edgeChanges", EdgeChanges}, {"interiorChanges", InteriorChanges}}.ToString())
                AssertRegression(EdgeChanges > 10, "折射 A/B：边缘真实改变背景采样位置", Results)
                AssertRegression(InteriorChanges = 0, "折射 A/B：平坦内部不发生位移", Results)
                CardSurface.RefractionEnabled = True
                SaveLayoutPreview(Main.PanForm, 1000, 640, "refraction-on")
                Dim ButtonOn = RenderGlassImage()
                OpticalButton.GlassFace.RefractionEnabled = False
                Dim ButtonOff = RenderGlassImage()
                Dim ButtonBounds = LayoutBounds(OpticalButton, Main.PanForm)
                Dim ButtonChanges As Integer = 0
                For X = CInt(ButtonBounds.Left + 2) To CInt(ButtonBounds.Left + 10)
                    For Y = CInt(ButtonBounds.Top + 14) To CInt(ButtonBounds.Top + 28)
                        If PixelDifference(ButtonOn, ButtonOff, X, Y) > 2 Then ButtonChanges += 1
                    Next
                Next
                AssertRegression(ButtonChanges > 5, "折射 A/B：实际按钮也使用独立透镜材质", Results)
                OpticalButton.GlassFace.RefractionEnabled = True
                CardSurface.GaussianBlurEnabled = False
                Dim WithoutBlur = RenderGlassImage()
                CardSurface.GaussianBlurEnabled = True
                Dim WithBlur = RenderGlassImage()
                Dim BlurChanges As Integer = 0
                For X = 480 To 650 Step 2
                    For Y = 260 To 315 Step 5
                        If PixelDifference(WithBlur, WithoutBlur, X, Y) > 5 Then BlurChanges += 1
                    Next
                Next
                AssertRegression(BlurChanges > 100, "高斯模糊 A/B：平坦内部的背景确实经过模糊", Results)
                CardSurface.MoveLight(New Point(SampleCard.ActualWidth, 0))
                Dim OppositeLight = RenderGlassImage()
                CardSurface.HideLight()
                Dim DefaultLight = RenderGlassImage()
                Dim LightChanges As Integer = 0
                For X = 222 To 227
                    For Y = 150 To 315 Step 3
                        If PixelDifference(OppositeLight, DefaultLight, X, Y) > 2 Then LightChanges += 1
                    Next
                Next
                AssertRegression(LightChanges > 10, "镜面高光 A/B：光源方向真实改变边缘亮度", Results)
                Dim Inside As New List(Of Integer)
                Dim Outside As New List(Of Integer)
                For X = 260 To 840 Step 5
                    Inside.Add(GlassPixel(BandImage, X, 300).R)
                    Outside.Add(GlassPixel(BandImage, X, 430).R)
                Next
                Dim InsideRange = Inside.Max() - Inside.Min()
                Dim OutsideRange = Outside.Max() - Outside.Min()
                AssertRegression(OutsideRange > 200 AndAlso InsideRange < OutsideRange * 0.9, "实际像素：玻璃内背景条带对比降低、玻璃外保持原图", Results)
                Main.ImgBack.Background = Brushes.Blue
                Dim Blue = GlassPixel(RenderGlassImage(), 500, 300)
                Main.ImgBack.Background = Brushes.Red
                Dim Red = GlassPixel(RenderGlassImage(), 500, 300)
                AssertRegression(Red.R > Blue.R + 15 AndAlso Blue.B > Red.B + 15, "实际像素：更换背景后玻璃材质同步更新", Results)
                Main.ImgBack.Background = Brushes.Black
                Dim Hierarchy = RenderGlassImage()
                Dim PanelPixel = GlassPixel(Hierarchy, 500, 300)
                Dim ButtonPixel = GlassPixel(Hierarchy, CInt(ButtonBounds.Left + 40), CInt(ButtonBounds.Top + 10))
                AssertRegression(ButtonPixel.R > PanelPixel.R + 50 AndAlso ButtonPixel.G > PanelPixel.G + 50,
                    "实际像素：按钮比大面板更实更亮，避免挖空层级", Results)
                File.WriteAllText(Paths.Base & "glass-material-metrics.json", New JObject From {
                    {"renderTier", RenderCapability.Tier >> 16}, {"insideBandRange", InsideRange}, {"outsideBandRange", OutsideRange},
                    {"refractionEdgeChangedPixels", EdgeChanges}, {"refractionInteriorChangedPixels", InteriorChanges}, {"buttonRefractionChangedPixels", ButtonChanges},
                    {"gaussianBlurChangedPixels", BlurChanges}, {"directionalLightChangedPixels", LightChanges},
                    {"panelOnBlack", PanelPixel.ToString()}, {"buttonOnBlack", ButtonPixel.ToString()},
                    {"blueBackgroundSample", Blue.ToString()}, {"redBackgroundSample", Red.ToString()}}.ToString())
                SampleCard.Height = 20000
                RenderGlassFrame(1000, 640, 160, "long-article", Results)
                AssertRegression(VisualDescendants(SampleCard).OfType(Of CaumcGlassSurface)().All(Function(Item) Item.BackdropExtent.Height <= 724 AndAlso Item.BackdropExtent.Width <= 1084),
                    "超长文章仅分配可视区域的模糊图层", Results)
                RunScrollingGlassChecks(Results)
            Finally
                Main.ImgBack.Background = OriginalBackground
                Main.ImgBack.Opacity = OriginalOpacity
            End Try
        Finally
            Main.PanMainLeft.Child = OldLeft
            Main.PanMainRight.Child = OldRight
            Main.PanMainLeft.Width = OldLeftWidth
            Main.PanMainRight.Width = OldRightWidth
            Main.PanForm.Clip = OldClip
            Main.BtnTitleSelect2.Visibility = OldLinkVisibility
            Main.PanMsg.Children.Clear()
            Main.PanMsg.Visibility = Visibility.Collapsed
            Settings.Set("CaumcGuideDismissed", OldDismissed)
        End Try
    End Sub

    Private Sub RunScrollingGlassChecks(Results As List(Of String))
        Dim Main = FrmMain
        Dim Evidence As New JArray()
        For Each Material In {"Navigation", "Card"}
            Dim Card As New MyCard With {.Title = "滚动材质", .Height = 20000, .GlassMaterial = Material, .Margin = New Thickness(25)}
            Dim Viewer As New MyScrollViewer With {.Content = Card, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled}
            Main.PanMainRight.Child = Viewer
            RenderGlassFrame(1000, 640, 160, "scroll-" & Material & "-start", Results)
            Dim Surface = VisualDescendants(Card).OfType(Of CaumcGlassSurface)().First()
            Dim Lens = VisualDescendants(Surface).OfType(Of Border)().First()
            Dim Covered = True, Sampled = True, Bounded = True
            For Each Offset In {100.0, 700.0, 1500.0, 6000.0, 19500.0, 700.0, 0.0}
                Viewer.ScrollToVerticalOffset(Offset)
                ' Exercise the production LayoutUpdated path, without manually
                ' refreshing surfaces or resetting their cached sample bounds.
                Main.PanForm.UpdateLayout()
                Dim Frame = RenderGlassImage()
                Dim Top = Surface.TransformToVisual(Main.PanForm).Transform(New Point(0, Lens.Margin.Top))
                Dim Bottom = Surface.TransformToVisual(Main.PanForm).Transform(New Point(0, Lens.Margin.Top + Lens.Height))
                Covered = Covered AndAlso Top.Y <= 90 AndAlso Bottom.Y >= 600
                Dim Pixel = GlassPixel(Frame, 500, 550)
                Sampled = Sampled AndAlso Pixel.R < 180 AndAlso Math.Abs(CInt(Pixel.R) - CInt(Pixel.B)) < 8
                Bounded = Bounded AndAlso Surface.BackdropExtent.Height <= 724 AndAlso Surface.BackdropExtent.Width <= 1084
                Evidence.Add(New JObject From {{"material", Material}, {"offset", Viewer.VerticalOffset},
                    {"lensTop", Top.Y}, {"lensBottom", Bottom.Y}, {"bottomPixel", Pixel.ToString()}})
                If Offset = 6000 Then SaveLayoutPreview(Main.PanForm, 1000, 640, "glass-scroll-" & Material & "-deep")
            Next
            File.WriteAllText(Paths.Base & "glass-scroll-metrics.json", Evidence.ToString())
            AssertRegression(Covered AndAlso Sampled, "滚动、深滚动及返回顶部始终覆盖完整玻璃区域：" & Material, Results)
            AssertRegression(Bounded, "滚动后玻璃采样纹理仍限制在可视范围：" & Material, Results)
        Next
        For Each Guide In PCLCS.CAUMC.GuideCatalog.All
            Dim Fixture = If(Guide.PostId = 2, "homepage.html", If(Guide.PostId = 5, "guide-mods.html", "guide-skin.html"))
            Dim Panel As New StackPanel With {.Margin = New Thickness(25)}
            Dim Snapshot As New PCLCS.CAUMC.ArticleSnapshot With {.Source = Guide.Source.AbsoluteUri, .CheckedAt = DateTimeOffset.Now,
                .Nodes = PCLCS.CAUMC.ArticleParser.Parse(File.ReadAllText(Paths.Base & Fixture), Guide.Source)}
            CaumcHomepage.BuildPage(Panel, Snapshot, Guide.PostId)
            Dim Viewer As New MyScrollViewer With {.Content = Panel, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
            Main.PanMainRight.Child = Viewer
            RenderGlassFrame(1000, 640, 160, "scroll-guide-" & Guide.PostId, Results)
            CheckPageScrollCoverage(Viewer, "指南 " & Guide.PostId, Results)
        Next
        Dim Downloads As New PageDownloadMod()
        Downloads.Content.PanLoad.Visibility = Visibility.Collapsed
        Downloads.Content.PanContent.Visibility = Visibility.Visible
        Downloads.Content.HintError.Visibility = Visibility.Collapsed
        For Index = 1 To 40
            Downloads.Content.PanProjects.Children.Add(New MyListItem With {.Title = "Mod " & Index, .Info = "滚动回归样例", .Height = 76})
        Next
        Main.PanMainRight.Child = Downloads
        RenderGlassFrame(1000, 640, 160, "scroll-mod-results", Results)
        CheckPageScrollCoverage(Downloads.Content, "Mod 下载列表", Results)
    End Sub

    Private Sub CheckPageScrollCoverage(Viewer As MyScrollViewer, Name As String, Results As List(Of String))
        AssertRegression(Viewer.ScrollableHeight > 0, "实际页面具有可滚动内容：" & Name, Results)
        For Each Fraction In {0.15, 0.5, 0.9, 1.0, 0.0}
            Viewer.ScrollToVerticalOffset(Viewer.ScrollableHeight * Fraction)
            FrmMain.PanForm.UpdateLayout()
            Dim Frame = RenderGlassImage()
            Dim VisibleCount = 0
            For Each Surface In VisualDescendants(Viewer).OfType(Of CaumcGlassSurface)()
                Dim Transform = Surface.TransformToVisual(FrmMain.PanForm)
                Dim Visible = Rect.Intersect(Transform.TransformBounds(New Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight)),
                    New Rect(0, 48, 1000, 592))
                If Visible.IsEmpty OrElse Visible.Width < 1 OrElse Visible.Height < 1 Then Continue For
                VisibleCount += 1
                Dim Lens = VisualDescendants(Surface).OfType(Of Border)().First()
                Dim Coverage = Transform.TransformBounds(New Rect(Lens.Margin.Left, Lens.Margin.Top, Lens.Width, Lens.Height))
                Coverage.Inflate(1, 1)
                AssertRegression(Surface.HasBackdrop AndAlso Coverage.Contains(Visible),
                    "实际页面滚动后玻璃完整覆盖：" & Name & " / " & Fraction, Results)
            Next
            AssertRegression(VisibleCount > 0, "滚动后存在可见玻璃面板：" & Name & " / " & Fraction, Results)
        Next
    End Sub

    Private Sub RunBackgroundChecks(Results As List(Of String))
        Dim Backdrop = FrmMain.GlassAtmosphere
        Dim OriginalOpacity = Backdrop.Opacity
        Dim Expected = JArray.Parse(File.ReadAllText(Paths.Base & "expected-backgrounds.json"))
        Dim Seen As New HashSet(Of String)()
        Dim PhotoEvidence As New JArray()
        Backdrop.Opacity = 1
        Try
            AssertRegression(Backdrop.PhotoCount = 22 AndAlso Expected.Count = Backdrop.PhotoCount, "22 张照片清单内嵌在 EXE 中", Results)
            Dim FirstName = Backdrop.CurrentPhotoName
            For Index = 0 To Backdrop.PhotoCount - 1
                Dim Name = Backdrop.CurrentPhotoName
                Seen.Add(Name)
                Dim Hash As String
                Using Stream = Application.GetResourceStream(New Uri("/Plain Craft Launcher 2;component/Resources/Backgrounds/" & Name, UriKind.Relative)).Stream,
                    Sha = System.Security.Cryptography.SHA256.Create()
                    Hash = BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "")
                End Using
                Dim Entry = Expected.Single(Function(Item) CStr(Item("file")) = Name)
                AssertRegression(Hash = CStr(Entry("sha256")) AndAlso Backdrop.CachedPhotoCount = 1, "EXE 内照片解码及哈希一致，缓存有界：" & Name, Results)
                PhotoEvidence.Add(New JObject From {{"file", Name}, {"sha256", Hash}})
                Backdrop.AdvancePhoto(False)
            Next
            AssertRegression(Seen.Count = 22 AndAlso Backdrop.CurrentPhotoName = FirstName, "轮播遍历所有照片并回到首张", Results)
            Dim First = RenderBackdrop(Backdrop, "0")
            Backdrop.AdvancePhoto()
            Backdrop.BeginAnimation(CaumcGlassBackdrop.FadeProperty, Nothing)
            Backdrop.SetValue(CaumcGlassBackdrop.FadeProperty, 0.5)
            Dim Middle = RenderBackdrop(Backdrop, "50")
            AssertRegression(Backdrop.CachedPhotoCount = 2, "淡入淡出最多保留两张解码照片", Results)
            Backdrop.CompleteTransition()
            Dim Second = RenderBackdrop(Backdrop, "100")
            Dim Blended As Integer = 0
            For X = 100 To 900 Step 100
                For Y = 100 To 500 Step 100
                    Dim A = GlassPixel(First, X, Y), B = GlassPixel(Second, X, Y), M = GlassPixel(Middle, X, Y)
                    If PixelDifference(First, Second, X, Y) > 20 AndAlso
                        Math.Abs(CInt(M.R) - (CInt(A.R) + CInt(B.R)) / 2) + Math.Abs(CInt(M.G) - (CInt(A.G) + CInt(B.G)) / 2) + Math.Abs(CInt(M.B) - (CInt(A.B) + CInt(B.B)) / 2) < 6 Then Blended += 1
                Next
            Next
            AssertRegression(Blended > 10, "实际像素证明照片平滑混合", Results)
            For Index = 1 To Backdrop.PhotoCount - 1
                Backdrop.AdvancePhoto(False)
            Next
            File.WriteAllText(Paths.Base & "embedded-background-metrics.json", New JObject From {{"photos", PhotoEvidence}, {"blendSamples", Blended}, {"intervalSeconds", 5}, {"transitionSeconds", 1.2}}.ToString())

            ' Exercise the production timer in a small off-screen window. No
            ' launch page/account lifecycle is attached to this test host.
            Dim TimedBackdrop As New CaumcGlassBackdrop()
            Dim Host As New Window With {.Content = TimedBackdrop, .Width = 200, .Height = 120, .Left = -10000, .Top = -10000,
                .ShowInTaskbar = False, .ShowActivated = False, .WindowStyle = WindowStyle.None, .WindowStartupLocation = WindowStartupLocation.Manual}
            Try
                Host.Show()
                CaumcSmoke.PumpUntilComplete(System.Threading.Tasks.Task.Delay(100))
                AssertRegression(TimedBackdrop.RotationRunning, "可见窗口启动照片轮播计时器", Results)
                Host.WindowState = WindowState.Minimized
                CaumcSmoke.PumpUntilComplete(System.Threading.Tasks.Task.Delay(100))
                AssertRegression(Not TimedBackdrop.RotationRunning, "最小化暂停照片轮播", Results)
                Host.WindowState = WindowState.Normal
                Dim Started = DateTime.UtcNow
                Dim Initial = TimedBackdrop.CurrentPhotoName
                While Initial = TimedBackdrop.CurrentPhotoName AndAlso (DateTime.UtcNow - Started).TotalSeconds < 8
                    CaumcSmoke.PumpUntilComplete(System.Threading.Tasks.Task.Delay(100))
                End While
                Dim Elapsed = (DateTime.UtcNow - Started).TotalSeconds
                AssertRegression(Initial <> TimedBackdrop.CurrentPhotoName AndAlso Elapsed >= 5 AndAlso Elapsed < 8, "真实五秒计时与淡入淡出完成后切换照片", Results)
                Host.Hide()
                AssertRegression(Not TimedBackdrop.RotationRunning, "隐藏窗口暂停轮播并释放过渡帧", Results)
            Finally
                Host.Close()
            End Try
        Finally
            Backdrop.CompleteTransition()
            Backdrop.Opacity = OriginalOpacity
        End Try
    End Sub

    Private Function RenderBackdrop(Backdrop As CaumcGlassBackdrop, Name As String) As Media.Imaging.RenderTargetBitmap
        Backdrop.UpdateLayout()
        Dim Image As New Media.Imaging.RenderTargetBitmap(1000, 640, 96, 96, PixelFormats.Pbgra32)
        Image.Render(Backdrop)
        Dim Encoder As New Media.Imaging.PngBitmapEncoder()
        Encoder.Frames.Add(Media.Imaging.BitmapFrame.Create(Image))
        Using Stream = File.Create(Paths.Base & "background-transition-" & Name & ".png")
            Encoder.Save(Stream)
        End Using
        Return Image
    End Function

    Private Sub RunControlGallery(Results As List(Of String))
        Dim Card As New MyCard With {.Title = "统一控件", .Margin = New Thickness(25), .VerticalAlignment = VerticalAlignment.Top}
        Dim Content As New StackPanel With {.Margin = New Thickness(20, 50, 20, 20)}
        Dim Buttons As New WrapPanel With {.Margin = New Thickness(0, 0, 0, 18)}
        For Each Button In {New MyButton With {.Text = "普通按钮"}, New MyButton With {.Text = "主要操作", .IsPrimary = True},
            New MyButton With {.Text = "选中状态", .ColorType = MyButton.ColorState.Highlight}, New MyButton With {.Text = "禁用状态", .IsEnabled = False},
            New MyButton With {.Text = "删除", .ColorType = MyButton.ColorState.Red}}
            Button.Width = 105 : Button.Height = 34 : Button.Margin = New Thickness(0, 0, 10, 8)
            Buttons.Children.Add(Button)
        Next
        Content.Children.Add(Buttons)
        Dim Fields As New WrapPanel With {.Margin = New Thickness(0, 0, 0, 18)}
        Dim Text As New MyTextBox With {.Text = "搜索版本或整合包", .Width = 250, .Height = 34, .Margin = New Thickness(0, 0, 15, 10)}
        Dim Combo As New MyComboBox With {.Width = 250, .Height = 34, .Margin = New Thickness(0, 0, 15, 10), .IsEditable = True}
        Combo.Items.Add("自动选择 Java") : Combo.SelectedIndex = 0
        Fields.Children.Add(Text) : Fields.Children.Add(Combo)
        Fields.Children.Add(New PasswordBox With {.Password = "isolated-ui-fixture", .Width = 250, .Height = 34})
        Content.Children.Add(Fields)
        Content.Children.Add(New MyCheckBox With {.Text = "保持登录", .Checked = True, .Margin = New Thickness(0, 0, 0, 15)})
        Content.Children.Add(New MyRadioBox With {.Text = "自动配置", .Checked = True, .Margin = New Thickness(0, 0, 0, 15)})
        Content.Children.Add(New MyHint With {.Text = "提示与边框沿用相同材质，错误和删除仍保留语义颜色。", .Margin = New Thickness(0, 0, 0, 15)})
        Dim Action As New MyExtraTextButton With {.Text = "开始下载", .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(0), .RenderTransform = New ScaleTransform(1, 1)}
        Action.PanScale.Margin = New Thickness(0)
        Content.Children.Add(Action)
        Card.Children.Add(Content)
        FrmMain.PanMainRight.Child = Card
        RenderGlassFrame(1000, 640, 160, "controls", Results)
        Dim Inner = DirectCast(Combo.Template.FindName("PART_EditableTextBox", Combo), MyTextBox)
        AssertRegression(Text.CornerRadius.TopLeft = 10 AndAlso DirectCast(Combo.Template.FindName("border", Combo), Border).CornerRadius.TopLeft = 10,
            "输入框、密码框、下拉框和按钮使用统一尺寸样式", Results)
        AssertRegression(Not Inner.HasBackground AndAlso DirectCast(Inner.Template.FindName("GlassFace", Inner), CaumcGlassSurface).Visibility = Visibility.Collapsed,
            "可编辑下拉框不重复叠加内层玻璃", Results)
    End Sub

    Private Sub RunWindowCornerChecks(Results As List(Of String))
        Dim Main = FrmMain
        Dim OldMargin = Main.PanBack.Margin
        Dim OldWidth = Main.PanBack.Width, OldHeight = Main.PanBack.Height
        Dim OldAngle = Main.TransformRotate.Angle, OldY = Main.TransformPos.Y
        Dim OldClip = Main.PanForm.Clip, OldRect = Main.RectForm.Rect
        Dim Metrics As New JArray()
        Try
            ' Include the actual eight resize/shadow regions and production
            ' parent clip; a PanForm-only preview cannot detect corner gaps.
            Main.PanBack.Margin = New Thickness(0)
            Main.PanBack.Width = 1016 : Main.PanBack.Height = 656
            Main.TransformRotate.Angle = 0 : Main.TransformPos.Y = 0
            Main.PanForm.Clip = Nothing
            ArrangeLayout(Main.PanBack, 1016, 656)
            Main.RectForm.Rect = New Rect(0, 0, Main.BorderForm.ActualWidth, Main.BorderForm.ActualHeight)
            For Each Scale In {1.0, 1.25, 1.5}
                Dim Image As New Media.Imaging.RenderTargetBitmap(CInt(1016 * Scale), CInt(656 * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32)
                Image.Render(Main.PanBack)
                Dim Corners As New JArray()
                For Each Point In {New Point(8.5, 13.5), New Point(1007.5, 13.5), New Point(8.5, 642.5), New Point(1007.5, 642.5)}
                    Dim Alpha = GlassPixel(Image, CInt(Math.Floor(Point.X * Scale)), CInt(Math.Floor(Point.Y * Scale))).A
                    Corners.Add(CInt(Alpha))
                Next
                Metrics.Add(New JObject From {{"scale", Scale}, {"cornerGapAlpha", Corners}})
                AssertRegression(Corners.All(Function(Value) CInt(Value) > 8 AndAlso CInt(Value) < 90),
                    "完整外框四角阴影连续、没有透明缺口：" & Scale.ToString("P0"), Results)
                AssertRegression(GlassPixel(Image, 0, 0).A = 0, "完整外框四角最外侧保持透明：" & Scale.ToString("P0"), Results)
                Dim Encoder As New Media.Imaging.PngBitmapEncoder()
                Encoder.Frames.Add(Media.Imaging.BitmapFrame.Create(Image))
                Using Stream = File.Create(Paths.Base & "layout-window-frame-" & CInt(Scale * 100) & ".png")
                    Encoder.Save(Stream)
                End Using
            Next
            File.WriteAllText(Paths.Base & "window-corner-metrics.json", Metrics.ToString())
        Finally
            Main.PanBack.Margin = OldMargin
            Main.PanBack.Width = OldWidth : Main.PanBack.Height = OldHeight
            Main.TransformRotate.Angle = OldAngle : Main.TransformPos.Y = OldY
            Main.PanForm.Clip = OldClip : Main.RectForm.Rect = OldRect
        End Try
    End Sub

    Private Sub RenderGlassFrame(Width As Double, Height As Double, LeftWidth As Double, Name As String, Results As List(Of String))
        Dim Main = FrmMain
        Main.PanForm.Width = Width
        Main.PanForm.Height = Height
        Main.PanMain.Width = Width
        Main.PanMain.Height = Height - 48
        Main.PanMainLeft.Width = LeftWidth
        Main.PanMainRight.Width = Width - LeftWidth
        Main.RectLeftBackground.Width = LeftWidth
        Main.PanForm.Clip = New RectangleGeometry(New Rect(0, 0, Width, Height), 12, 12)
        ArrangeLayout(Main.PanForm, Width, Height)
        For Each Card In VisualDescendants(Main.PanForm).OfType(Of MyCard)().ToList()
            Dim Title = Card.MainTextBlock ' Initialize title without invoking a page Loaded handler.
        Next
        For Each Item In VisualDescendants(Main.PanForm).OfType(Of MyListItem)()
            Item.GlassSelection.Visibility = If(Item.Checked AndAlso Item.Type = MyListItem.CheckType.RadioBox, Visibility.Visible, Visibility.Collapsed)
        Next
        For Each Item In VisualDescendants(Main.PanForm).OfType(Of MyRadioButton)()
            Item.GlassFace.Visibility = If(Item.Checked, Visibility.Visible, Visibility.Collapsed)
        Next
        For Each Item In VisualDescendants(Main.PanForm).OfType(Of PageSetupLeft)()
            Item.ItemLink.Visibility = Visibility.Collapsed
        Next
        ArrangeLayout(Main.PanForm, Width, Height)
        For Each Surface In VisualDescendants(Main.PanForm).OfType(Of CaumcGlassSurface)()
            Surface.RefreshBackdrop()
        Next
        ArrangeLayout(Main.PanForm, Width, Height)
        For Each Surface In VisualDescendants(Main.PanForm).OfType(Of CaumcGlassSurface)()
            Surface.RefreshBackdrop()
        Next
        SaveLayoutPreview(Main.PanForm, Width, Height, "glass-" & Name)
        AssertRegression(Main.PanMainLeft.ActualWidth + Main.PanMainRight.ActualWidth <= Width + 1 AndAlso Main.PanTitle.ActualHeight = 48,
            "玻璃框架与实际页面布局：" & Name, Results)
    End Sub

    Private Function RenderGlassImage() As Media.Imaging.RenderTargetBitmap
        FrmMain.PanForm.UpdateLayout()
        FrmMain.Dispatcher.Invoke(Windows.Threading.DispatcherPriority.Render, New Action(Sub()
                                                                                       End Sub))
        Dim Image As New Media.Imaging.RenderTargetBitmap(1000, 640, 96, 96, PixelFormats.Pbgra32)
        Image.Render(FrmMain.PanForm)
        Return Image
    End Function

    Private Function GlassPixel(Image As Media.Imaging.RenderTargetBitmap, X As Integer, Y As Integer) As Color
        Dim Pixel(3) As Byte
        Image.CopyPixels(New Int32Rect(X, Y, 1, 1), Pixel, 4, 0)
        Return Color.FromArgb(Pixel(3), Pixel(2), Pixel(1), Pixel(0))
    End Function

    Private Function PixelDifference(A As Media.Imaging.RenderTargetBitmap, B As Media.Imaging.RenderTargetBitmap, X As Integer, Y As Integer) As Integer
        Dim First = GlassPixel(A, X, Y)
        Dim Second = GlassPixel(B, X, Y)
        Return Math.Abs(CInt(First.R) - Second.R) + Math.Abs(CInt(First.G) - Second.G) + Math.Abs(CInt(First.B) - Second.B)
    End Function

    Private Iterator Function VisualDescendants(Root As DependencyObject) As IEnumerable(Of DependencyObject)
        For Index = 0 To VisualTreeHelper.GetChildrenCount(Root) - 1
            Dim Child = VisualTreeHelper.GetChild(Root, Index)
            Yield Child
            For Each Descendant In VisualDescendants(Child)
                Yield Descendant
            Next
        Next
    End Function

    Friend Sub RunLayouts(Results As List(Of String))
        ' Measure actual controls without showing/loading the whole launcher page:
        ' its Loaded handler starts game discovery, unrelated to layout validation.
        Dim Height = FrmMain.MinHeight - 36 - 48
        Dim Left = FrmLaunchLeft
        Dim PreviousBackground = Left.Background
        Left.Background = Brushes.White
        Dim PreviousStatus = FrmLoginAuth.TextStatus.Text
        Dim Notes As New List(Of String) From {"Logical layout at minimum window size; not a Windows DPI setting test."}
        Try
            For Each LongError In {False, True}
                FrmLoginAuth.TextStatus.Text = If(LongError,
                    "登录失败：" & String.Join(vbCrLf, Enumerable.Repeat("连接皮肤站失败，请检查网络连接后重试。", 8)), PreviousStatus)
                ArrangeLayout(Left, 300, Height)
                Dim LoginBounds = LayoutBounds(FrmLoginAuth.BtnLogin, Left)
                Dim LaunchBounds = LayoutBounds(Left.BtnLaunch, Left)
                Dim InputBounds = LayoutBounds(FrmLoginAuth.ComboName, Left)
                Dim StatusViewer = TryCast(FrmLoginAuth.TextStatus.Parent, ScrollViewer)
                Dim StatusElement As FrameworkElement = If(DirectCast(StatusViewer, FrameworkElement), FrmLoginAuth.TextStatus)
                Dim StatusBounds = LayoutBounds(StatusElement, Left)
                Notes.Add($"longError={LongError}, bodyHeight={Height}, inputTop={InputBounds.Top}, loginBottom={LoginBounds.Bottom}, statusBottom={StatusBounds.Bottom}, launchTop={LaunchBounds.Top}")
                File.WriteAllLines(Paths.Base & "layout-metrics.txt", Notes)
                SaveLayoutPreview(Left, 300, Height, If(LongError, "login-long-error", "login-minimum"))
                AssertRegression(InputBounds.Top >= 45 AndAlso LoginBounds.Bottom <= LaunchBounds.Top - 1 AndAlso StatusBounds.Bottom <= LaunchBounds.Top - 1,
                    If(LongError, "最小窗口下长登录错误不遮挡账号、登录与启动按钮", "最小窗口下 CAU 登录与启动按钮不重叠"), Results)
                If LongError Then
                    AssertRegression(StatusViewer IsNot Nothing AndAlso StatusViewer.ScrollableHeight > 0, "长登录错误完整保留且超出部分可滚动", Results)
                    StatusViewer.ScrollToEnd()
                    StatusViewer.UpdateLayout()
                    Notes.Add($"statusOffset={StatusViewer.VerticalOffset}, statusScrollableHeight={StatusViewer.ScrollableHeight}")
                    File.WriteAllLines(Paths.Base & "layout-metrics.txt", Notes)
                    AssertRegression(StatusViewer.VerticalOffset > 0 AndAlso Math.Abs(StatusViewer.VerticalOffset - StatusViewer.ScrollableHeight) < 1,
                        "长登录错误可滚动到末尾", Results)
                End If
            Next
        Finally
            FrmLoginAuth.TextStatus.Text = PreviousStatus
            Left.Background = PreviousBackground
        End Try
        Dim Width = FrmMain.MinWidth - 36 - 300
        Dim WasDismissed = Settings.Get(Of Boolean)("CaumcGuideDismissed")
        Try
            Settings.Set("CaumcGuideDismissed", True)
            For Each Guide In PCLCS.CAUMC.GuideCatalog.All
                Dim Fixture = Paths.Base & If(Guide.PostId = 2, "homepage.html", If(Guide.PostId = 5, "guide-mods.html", "guide-skin.html"))
                Dim Snapshot As New PCLCS.CAUMC.ArticleSnapshot With {.Source = Guide.Source.AbsoluteUri, .CheckedAt = DateTimeOffset.Now,
                    .Nodes = PCLCS.CAUMC.ArticleParser.Parse(File.ReadAllText(Fixture), Guide.Source)}
                Dim Panel As New StackPanel With {.Margin = New Thickness(25, 25, 25, 10)}
                CaumcHomepage.BuildPage(Panel, Snapshot, Guide.PostId)
                Dim Viewer As New MyScrollViewer With {.Content = Panel, .Background = Brushes.White, .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled}
                ArrangeLayout(Viewer, Width, Height)
                Dim Tabs = Panel.Children.OfType(Of Grid).Single(Function(Item) Item.Name = "CaumcGuideTabs").Children.OfType(Of MyButton).ToList()
                AssertRegression(Tabs.All(Function(Tab) LayoutBounds(Tab, Viewer).Left >= 24 AndAlso LayoutBounds(Tab, Viewer).Right <= Width - 24 AndAlso
                    Tab.LabText.DesiredSize.Width <= Tab.ActualWidth), "最小窗口下三个指南标题完整且不越界：" & Guide.PostId, Results)
                Dim Body = Panel.Children.OfType(Of MyCard).Last().Children.OfType(Of StackPanel).Single()
                Dim Actions = Body.Children.OfType(Of WrapPanel).Single().Children.OfType(Of MyButton).ToList()
                AssertRegression(Actions.All(Function(Action) LayoutBounds(Action, Viewer).Left >= 24 AndAlso LayoutBounds(Action, Viewer).Right <= Width - 24),
                    "最小窗口下工具按钮换行后不越界：" & Guide.PostId, Results)
                SaveLayoutPreview(Viewer, Width, Height, "guide-minimum-" & Guide.PostId)
            Next
        Finally
            Settings.Set("CaumcGuideDismissed", WasDismissed)
        End Try
        Dim Downloads As New PageDownloadMod()
        Downloads.Content.Background = Brushes.White
        ArrangeLayout(Downloads, Width, Height)
        SaveLayoutPreview(Downloads, Width, Height, "mod-download-minimum")
        Notes.Add("Rendered Mod download page without starting its Loaded/search lifecycle.")
        File.WriteAllLines(Paths.Base & "layout-metrics.txt", Notes)
    End Sub

    Private Sub ArrangeLayout(Element As FrameworkElement, Width As Double, Height As Double)
        Element.Measure(New Size(Width, Height))
        Element.Arrange(New Rect(0, 0, Width, Height))
        Element.UpdateLayout()
    End Sub

    Private Function LayoutBounds(Element As FrameworkElement, Root As Visual) As Rect
        Return Element.TransformToAncestor(Root).TransformBounds(New Rect(0, 0, Element.ActualWidth, Element.ActualHeight))
    End Function

    Private Sub SaveLayoutPreview(Element As FrameworkElement, Width As Double, Height As Double, Name As String)
        Dim Image As New Media.Imaging.RenderTargetBitmap(CInt(Width), CInt(Height), 96, 96, PixelFormats.Pbgra32)
        Image.Render(Element)
        Dim Encoder As New Media.Imaging.PngBitmapEncoder()
        Encoder.Frames.Add(Media.Imaging.BitmapFrame.Create(Image))
        Using Stream = File.Create(Paths.Base & "layout-" & Name & ".png")
            Encoder.Save(Stream)
        End Using
    End Sub

    Friend Sub RunOnlineModUpdate(Results As List(Of String))
        Dim RequestFile = Paths.Base & "mod-update-request.json"
        If Not File.Exists(RequestFile) Then Return
        Dim Request = File.ReadAllText(RequestFile).DeserializeJson(Of JObject)()
        Dim PreviousInstance = PageInstanceLeft.Instance
        Dim PreviousPage = FrmInstanceMod
        Dim PreviousFolder = McFolderSelected
        Try
            McFolderSelected = CaumcGameDirectory
            Dim Instance As New McInstance(CaumcGameDirectory & "versions\caumc-mod-update-regression\")
            Settings.Set("VersionArgumentIndieV2", True, Instance)
            PageInstanceLeft.Instance = Instance
            Dim Page As New PageInstanceMod()
            FrmInstanceMod = Page
            Dim Folder = Instance.PathIndie & "mods\"
            LoadMods(Page, Folder)
            WaitForRegressionLoader(LocalResourceOnlineLoader)
            Dim Entry = LocalResourceLoader.Output.Single()
            AssertRegression(Entry.Project?.Platform = ResourcePlatforms.Modrinth AndAlso Entry.ProjectVersion IsNot Nothing,
                "原生在线加载器通过 Modrinth SHA1 识别真实 Mod", Results)
            AssertRegression(Entry.HasUpdate AndAlso Entry.UpdateFile.FileName = CStr(Request("latestFile")),
                "原生在线检查发现适配 Minecraft 1.21.1 的新版 Fabric API", Results)
            Settings.Set("HintUpdateMod", True) ' Only the independent fixture is being updated.
            Page.UpdateMods({Entry})
            Dim Update = LoaderTaskbar.Last(Function(Item) Item.Name = "Mod 更新：" & Instance.Name)
            WaitForRegressionLoader(Update)
            LoadMods(Page, Folder)
            WaitForRegressionLoader(LocalResourceOnlineLoader)
            Dim NewPath = Folder & CStr(Request("latestFile"))
            AssertRegression(File.Exists(NewPath) AndAlso Not File.Exists(Folder & CStr(Request("oldFile"))) AndAlso Directory.EnumerateFiles(Folder, "*.jar").Count() = 1,
                "原生更新流程下载并替换隔离副本中的旧 Mod", Results)
            AssertRegression(CryptographyUtils.ComputeFileHash(NewPath, CryptographyUtils.HashMethod.Sha1).Equals(CStr(Request("latestSHA1")), StringComparison.OrdinalIgnoreCase),
                "实际更新后的 Mod SHA1 与 Modrinth 官方元数据一致", Results)
            AssertRegression(LocalResourceLoader.Output.Count = 1 AndAlso Not LocalResourceLoader.Output.Single().HasUpdate,
                "更新并重新检查后不再提示同一 Mod 更新", Results)
        Finally
            LocalResourceLoader.Cancel()
            LocalResourceOnlineLoader.Cancel()
            FrmInstanceMod = PreviousPage
            PageInstanceLeft.Instance = PreviousInstance
            McFolderSelected = PreviousFolder
        End Try
    End Sub

    Private Sub WaitForRegressionLoader(Loader As LoaderBase)
        Dim Operation = System.Threading.Tasks.Task.Run(
            Sub()
                Dim Deadline = DateTime.UtcNow.AddMinutes(4)
                While Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting
                    File.WriteAllText(Paths.Base & "mod-update-progress.txt", Loader.Name & " " & Loader.Progress.ToString("P1"))
                    If DateTime.UtcNow > Deadline Then
                        Loader.Cancel()
                        Throw New TimeoutException("Mod 更新自检超时：" & Loader.Name)
                    End If
                    Thread.Sleep(100)
                End While
                If Loader.State <> LoadState.Finished Then Throw New Exception("Mod 更新自检失败：" & Loader.Name, Loader.Error)
            End Sub)
        CaumcSmoke.PumpUntilComplete(Operation)
        Operation.GetAwaiter().GetResult()
    End Sub

    Friend Sub RunFabric(Results As List(Of String))
        Dim RequestFile = Paths.Base & "fabric-install-request.json"
        If Not File.Exists(RequestFile) Then Return
        Dim Config = File.ReadAllText(RequestFile).DeserializeJson(Of JObject)()
        Dim Minecraft = CStr(Config("MinecraftVersion"))
        Dim Fabric = CStr(Config("LoaderVersion"))
        Dim JsonAddress = CStr(Config("MinecraftJson"))
        Dim ExpectedSha1 = CStr(Config("ClientSha1"))
        If Minecraft <> "1.21.1" OrElse Not Fabric.RegexCheck("^[0-9]+\.[0-9]+\.[0-9]+$") Then Throw New Exception("Fabric 自检版本参数无效")
        McFolderSelected = CaumcGameDirectory
        Settings.Set("CaumcLoginChoice", 0)
        FrmLaunchLeft.RefreshPage(False, False)
        Dim Name = "caumc-fabric-regression"
        Dim Folder = CaumcGameDirectory & "versions\" & Name & "\"
        Dim Instance As New McInstance(Folder)
        Settings.Set("VersionArgumentIndieV2", True, Instance)
        Dim Request As New McInstallRequest With {.NewInstanceName = Name, .InstanceFolder = Folder, .MinecraftJson = JsonAddress,
            .Components = New McComponents(Minecraft, New Dictionary(Of ModLoaders, String) From {{ModLoaders.Fabric, Fabric}})}
        Dim Loader As New LoaderCombo(Of String)("Fabric installation regression", McInstallLoadersGet(Request))
        Dim Operation = System.Threading.Tasks.Task.Run(
            Sub()
                Loader.Start(Folder)
                Dim Deadline = DateTime.UtcNow.AddMinutes(20)
                While Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting
                    Dim Active = Loader.GetLoaderList(False).Where(Function(Item) Item.State = LoadState.Loading).Select(Function(Item) Item.Name & " " & Item.Progress.ToString("P1"))
                    File.WriteAllText(Paths.Base & "fabric-progress.txt", Loader.Progress.ToString("P1") & vbCrLf & String.Join(vbCrLf, Active), New UTF8Encoding(False))
                    If DateTime.UtcNow > Deadline Then
                        Loader.Cancel()
                        Throw New TimeoutException("Fabric 安装自检超过 20 分钟")
                    End If
                    Thread.Sleep(500)
                End While
                If Loader.State <> LoadState.Finished Then Throw New Exception("Fabric 安装失败：" & Loader.State.ToString(), Loader.Error)
            End Sub)
        Try
            CaumcSmoke.PumpUntilComplete(Operation)
            Operation.GetAwaiter().GetResult()
        Finally
            If Loader.State = LoadState.Loading Then Loader.Cancel()
        End Try
        Instance = New McInstance(Folder).Load()
        AssertRegression(Instance.Version.VanillaName = Minecraft, "Fabric 真实下载与安装识别 Minecraft " & Minecraft, Results)
        AssertRegression(Instance.Version.ModLoaderVersions.ContainsKey(ModLoaders.Fabric) AndAlso Instance.Version.ModLoaderVersions(ModLoaders.Fabric) = Fabric,
            "Fabric 真实安装识别 Loader " & Fabric, Results)
        AssertRegression(Not File.Exists(Folder & ".pclignore") AndAlso Directory.Exists(Instance.PathIndie & "mods"), "Fabric 安装完成后取消忽略标记并创建 Mod 目录", Results)
        Dim Jar = Folder & Name & ".jar"
        AssertRegression(File.Exists(Jar) AndAlso CryptographyUtils.ComputeFileHash(Jar, CryptographyUtils.HashMethod.Sha1).Equals(ExpectedSha1, StringComparison.OrdinalIgnoreCase),
            "Fabric 游戏主 JAR 与 Mojang 官方 SHA1 一致", Results)
        AssertRegression(File.Exists(CaumcGameDirectory & "libraries\net\fabricmc\fabric-loader\" & Fabric & "\fabric-loader-" & Fabric & ".jar"),
            "Fabric Loader 支持库实际下载完成", Results)
        CaumcSmoke.RunOfflineInstance(Instance, "真实 Fabric " & Fabric, "Loading Minecraft " & Minecraft & " with Fabric Loader " & Fabric, Results)
    End Sub

    Friend Sub Run(Results As List(Of String))
        RunImageFailureTests(Results)
        If File.Exists(Paths.Base & "guide-image-tests") Then RunGuideImages(Results)
        RunDiagnostics(Results)
        RunDiagnosticExport(Results)
        Dim InstancePath = CaumcGameDirectory & "versions\caumc-mod-regression\"
        If File.Exists(InstancePath & "caumc-mod-regression.json") Then RunMods(InstancePath, Results)
    End Sub

    Private Iterator Function ImageElements(Root As DependencyObject) As IEnumerable(Of Image)
        If TypeOf Root Is Image Then Yield DirectCast(Root, Image)
        For Each Child In LogicalTreeHelper.GetChildren(Root).OfType(Of DependencyObject)()
            For Each Image In ImageElements(Child)
                Yield Image
            Next
        Next
    End Function

    Private Sub RunGuideImages(Results As List(Of String))
        Dim Metrics As New List(Of String)
        For Each PostId In {5, 6}
            Dim Guide = PCLCS.CAUMC.GuideCatalog.ForPost(PostId)
            Dim Fixture = Paths.Base & If(PostId = 5, "guide-mods.html", "guide-skin.html")
            Dim Snapshot As New PCLCS.CAUMC.ArticleSnapshot With {.Source = Guide.Source.AbsoluteUri,
                .Nodes = PCLCS.CAUMC.ArticleParser.Parse(File.ReadAllText(Fixture), Guide.Source)}
            Dim Panel As New StackPanel()
            CaumcHomepage.BuildPage(Panel, Snapshot, PostId)
            Dim Viewer As New MyScrollViewer With {.Content = Panel, .Background = Brushes.White, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
            Dim Window As New Window With {.Content = Viewer, .Width = 850, .Height = 700, .Left = -10000, .Top = -10000,
                .ShowActivated = False, .ShowInTaskbar = False, .WindowStyle = WindowStyle.None}
            Try
                Window.Show()
                Window.UpdateLayout()
                Dim Reader = Panel.Children.OfType(Of MyCard).Last().Children.OfType(Of StackPanel).Single().Children.OfType(Of RichTextBox).Single()
                Dim Images = ImageElements(Reader.Document).ToList()
                CaumcSmoke.PumpUntilComplete(System.Threading.Tasks.Task.WhenAll(Images.Select(Function(Item) TryCast(Item.Tag, System.Threading.Tasks.Task)).Where(Function(Item) Item IsNot Nothing)))
                Window.UpdateLayout()
                AssertRegression(Images.Count = If(PostId = 5, 1, 6), "真实指南图片数：" & PostId, Results)
                For Index = 0 To Images.Count - 1
                    Dim Image = Images(Index)
                    Dim Bitmap = TryCast(Image.Source, Imaging.BitmapSource)
                    Dim Offset = Image.TransformToAncestor(Panel).Transform(New Point(0, 0)).Y
                    Viewer.ScrollToVerticalOffset(Offset - 20)
                    Viewer.Dispatcher.Invoke(Sub()
                                             End Sub, Windows.Threading.DispatcherPriority.ApplicationIdle)
                    Window.UpdateLayout()
                    Dim Bounds = Image.TransformToAncestor(Viewer).TransformBounds(New Rect(0, 0, Image.ActualWidth, Image.ActualHeight))
                    Dim Render As New Imaging.RenderTargetBitmap(CInt(Math.Ceiling(Viewer.ActualWidth)), CInt(Math.Ceiling(Viewer.ActualHeight)), 96, 96, PixelFormats.Pbgra32)
                    Render.Render(Viewer)
                    Dim Encoder As New Imaging.PngBitmapEncoder()
                    Encoder.Frames.Add(Imaging.BitmapFrame.Create(Render))
                    Using Stream = File.Create(Paths.Base & "guide-image-" & PostId & "-" & Index & ".png")
                        Encoder.Save(Stream)
                    End Using
                    Dim Pixels(Render.PixelWidth * Render.PixelHeight * 4 - 1) As Byte
                    Render.CopyPixels(Pixels, Render.PixelWidth * 4, 0)
                    Dim Colored As Integer = 0
                    For Y = Math.Max(0, CInt(Bounds.Top) + 2) To Math.Min(Render.PixelHeight - 1, CInt(Bounds.Bottom) - 2) Step 4
                        For X = Math.Max(0, CInt(Bounds.Left) + 2) To Math.Min(Render.PixelWidth - 1, CInt(Bounds.Right) - 2) Step 4
                            Dim Position = (Y * Render.PixelWidth + X) * 4
                            If Pixels(Position + 3) > 200 AndAlso Math.Min(Pixels(Position), Math.Min(Pixels(Position + 1), Pixels(Position + 2))) < 225 Then Colored += 1
                        Next
                    Next
                    Metrics.Add(PostId & "/" & Index & " pixels=" & If(Bitmap Is Nothing, "none", Bitmap.PixelWidth & "x" & Bitmap.PixelHeight) & " bounds=" & Bounds.ToString() & " colored=" & Colored)
                    If Colored <= 50 Then
                        Dim Ancestor As DependencyObject = Image
                        While Ancestor IsNot Nothing
                            Dim Element = TryCast(Ancestor, UIElement)
                            Metrics.Add(Ancestor.GetType().FullName & " clip=" & If(VisualTreeHelper.GetClip(Ancestor)?.Bounds.ToString(), "none") &
                                If(Element Is Nothing, "", " opacity=" & Element.Opacity & " visible=" & Element.IsVisible & " render=" & Element.RenderSize.ToString()))
                            Ancestor = VisualTreeHelper.GetParent(Ancestor)
                        End While
                    End If
                    File.WriteAllLines(Paths.Base & "image-layout-diagnostics.txt", Metrics, New UTF8Encoding(False))
                    AssertRegression(Bitmap IsNot Nothing AndAlso Bitmap.PixelWidth > 1 AndAlso Image.ActualHeight > 10 AndAlso Colored > 50,
                        "滚动到真实指南图片后实际绘制：" & PostId & "/" & Index, Results)
                Next
                Window.Width = 540
                Window.UpdateLayout()
                AssertRegression(Images.All(Function(Item) Item.TransformToAncestor(Reader).TransformBounds(New Rect(0, 0, Item.ActualWidth, Item.ActualHeight)).Right <= Reader.ActualWidth + 1),
                    "窗口缩窄后图片不超出正文宽度：" & PostId, Results)
                AssertRegression(Images.All(Function(Item) Math.Abs(Item.ActualWidth / Item.ActualHeight - DirectCast(Item.Source, Imaging.BitmapSource).PixelWidth / DirectCast(Item.Source, Imaging.BitmapSource).PixelHeight) < 0.01),
                    "窗口缩窄后图片保持原始宽高比：" & PostId, Results)
            Finally
                Window.Close()
            End Try
        Next
    End Sub

    Private Sub RunImageFailureTests(Results As List(Of String))
        Dim Visual As New DrawingVisual()
        Using Drawing = Visual.RenderOpen()
            Drawing.DrawRectangle(Brushes.Blue, Nothing, New Rect(0, 0, 100, 50))
        End Using
        Dim Bitmap As New Imaging.RenderTargetBitmap(100, 50, 96, 96, PixelFormats.Pbgra32)
        Bitmap.Render(Visual)
        Dim Encoder As New Imaging.PngBitmapEncoder()
        Encoder.Frames.Add(Imaging.BitmapFrame.Create(Bitmap))
        Dim Bytes As Byte()
        Using Stream As New MemoryStream()
            Encoder.Save(Stream)
            Bytes = Stream.ToArray()
        End Using
        Dim Handler As New ImageTestHandler With {.Bytes = Bytes}
        Using Client As New HttpClient(Handler)
            Dim Url As New Uri("https://example.invalid/image-regression/" & Guid.NewGuid().ToString("N"))
            Dim Host = DirectCast(CaumcGuideImage.Create(Url, "fixture", Client), StackPanel)
            Dim Picture = Host.Children.OfType(Of Image).Single()
            CaumcSmoke.PumpUntilComplete(DirectCast(Picture.Tag, System.Threading.Tasks.Task))
            AssertRegression(Picture.Source IsNot Nothing AndAlso Picture.Source.IsFrozen AndAlso Host.Children.OfType(Of TextBlock).Single().Visibility = Visibility.Collapsed,
                "图片下载后完整解码并冻结，再显示正文图片", Results)
            Handler.Fail = True
            Host = DirectCast(CaumcGuideImage.Create(Url, "fixture", Client), StackPanel)
            Picture = Host.Children.OfType(Of Image).Single()
            CaumcSmoke.PumpUntilComplete(DirectCast(Picture.Tag, System.Threading.Tasks.Task))
            AssertRegression(Picture.Source IsNot Nothing AndAlso Picture.Visibility = Visibility.Visible AndAlso Host.Children.OfType(Of TextBlock).Single().Text.Contains("缓存"),
                "图片联网失败时显示有效图片缓存", Results)
            Host = DirectCast(CaumcGuideImage.Create(New Uri(Url.AbsoluteUri & "/missing"), "fixture", Client), StackPanel)
            Picture = Host.Children.OfType(Of Image).Single()
            CaumcSmoke.PumpUntilComplete(DirectCast(Picture.Tag, System.Threading.Tasks.Task))
            Host.Measure(New Size(700, Double.PositiveInfinity))
            AssertRegression(Picture.Source Is Nothing AndAlso Picture.Visibility = Visibility.Collapsed AndAlso Host.DesiredSize.Height < 100 AndAlso Host.Children.OfType(Of TextBlock).Single().Text.Contains("图片加载失败"),
                "无缓存且图片失败时显示紧凑错误提示，不留下大段空白", Results)
            Handler.Fail = False
            Handler.Bytes = Encoding.UTF8.GetBytes("not-an-image")
            Host = DirectCast(CaumcGuideImage.Create(New Uri(Url.AbsoluteUri & "/corrupt"), "fixture", Client), StackPanel)
            Picture = Host.Children.OfType(Of Image).Single()
            CaumcSmoke.PumpUntilComplete(DirectCast(Picture.Tag, System.Threading.Tasks.Task))
            AssertRegression(Picture.Source Is Nothing AndAlso Host.Children.OfType(Of TextBlock).Single().Text.Contains("图片加载失败"),
                "损坏图片解码失败时保留正文并显示错误", Results)
        End Using
    End Sub

    Private Class ImageTestHandler
        Inherits HttpMessageHandler
        Friend Bytes As Byte()
        Friend Fail As Boolean
        Protected Overrides Function SendAsync(Request As HttpRequestMessage, CancellationToken As System.Threading.CancellationToken) As System.Threading.Tasks.Task(Of HttpResponseMessage)
            Return System.Threading.Tasks.Task.FromResult(New HttpResponseMessage(If(Fail, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK)) With {.Content = New ByteArrayContent(Bytes)})
        End Function
    End Class

    Private Sub AssertRegression(Condition As Boolean, Name As String, Results As List(Of String))
        If Not Condition Then Throw New Exception(Name)
        Results.Add("PASS " & Name)
    End Sub

    Private Function HashFile(Path As String) As String
        Using Algorithm = System.Security.Cryptography.SHA256.Create(), Stream = File.OpenRead(Path)
            Return BitConverter.ToString(Algorithm.ComputeHash(Stream)).Replace("-", "")
        End Using
    End Function

    Private Sub InvokePrivate(Target As Object, Method As String, ParamArray Arguments As Object())
        Target.GetType().GetMethod(Method, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(Target, Arguments)
    End Sub

    Private Sub LoadMods(Page As PageInstanceMod, Folder As String)
        Dim Load = System.Threading.Tasks.Task.Run(Sub() LocalResourceLoader.WaitForExit(Folder, IsForceRestart:=True))
        CaumcSmoke.PumpUntilComplete(Load)
        Load.GetAwaiter().GetResult()
        InvokePrivate(Page, "LoadUIFromLoaderOutput")
    End Sub

    Private Sub RunMods(InstancePath As String, Results As List(Of String))
        Dim PreviousInstance = PageInstanceLeft.Instance
        Dim PreviousPage = FrmInstanceMod
        Dim PreviousOnlineLoader = LocalResourceOnlineLoader
        Dim PreviousFolder = McFolderSelected
        Try
            McFolderSelected = CaumcGameDirectory
            Dim Instance As New McInstance(InstancePath)
            Settings.Set("VersionArgumentIndieV2", True, Instance)
            PageInstanceLeft.Instance = Instance
            AssertRegression(Instance.PathIndie = InstancePath AndAlso Instance.Version.VanillaName = "1.20.1", "Mod 回归使用独立 1.20.1 实例副本", Results)
            ' Online update checks are a separate network test; keep local operations deterministic.
            LocalResourceOnlineLoader = New LoaderTask(Of (ModList As List(Of LocalResourceFile), Cache As JObject, Target As McVersion), Integer)(
                "RegressionNoOnlineLookup", Sub(Loader) Loader.Output = 0)
            Dim Page As New PageInstanceMod()
            FrmInstanceMod = Page
            Dim Folder = Instance.PathIndie & "mods\"
            Dim Originals = Directory.EnumerateFiles(Folder, "*.jar").ToDictionary(Function(Path) IO.Path.GetFileName(Path), AddressOf HashFile)
            AssertRegression(Originals.Count = 9, "指定整合包的 9 个真实 Mod 已复制到隔离测试目录", Results)
            LoadMods(Page, Folder)
            AssertRegression(LocalResourceLoader.Output.Count = 9 AndAlso Page.ModItems.Count = 9 AndAlso Page.PanList.Children.Count = 9,
                "原生 Mod 加载器与管理页面列出全部 9 个 Mod", Results)
            Dim TargetName = Originals.Keys.First(Function(Name) Name.Contains("gd656peek"))
            Dim Target = Page.ModItems(TargetName)
            Page.ED_Click(New MyIconButton With {.Tag = Target}, EventArgs.Empty)
            AssertRegression(Not File.Exists(Folder & TargetName) AndAlso File.Exists(Folder & TargetName & ".disabled") AndAlso
                Not Page.ModItems(TargetName).Entry.IsEnabled AndAlso LocalResourceLoader.Output.Where(Function(Item) Not Item.IsEnabled).Count = 1,
                "原生禁用操作同步磁盘文件、加载器和 Mod 列表状态", Results)
            AssertRegression(HashFile(Folder & TargetName & ".disabled") = Originals(TargetName), "禁用 Mod 不改变文件 SHA256", Results)
            Page.Filter = PageInstanceMod.FilterType.Disabled
            AssertRegression(Page.PanList.Children.Count = 1 AndAlso DirectCast(Page.PanList.Children(0), MyLocalModItem).Entry.EnabledName = TargetName,
                "禁用筛选只显示被禁用的 Mod", Results)
            Page.ED_Click(New MyIconButton With {.Tag = Page.ModItems(TargetName)}, EventArgs.Empty)
            AssertRegression(File.Exists(Folder & TargetName) AndAlso Not File.Exists(Folder & TargetName & ".disabled") AndAlso
                LocalResourceLoader.Output.All(Function(Item) Item.IsEnabled), "原生启用操作恢复 JAR 及全部启用状态", Results)
            AssertRegression(Page.PanList.Visibility = Visibility.Collapsed, "启用后立即隐藏已为空的禁用筛选结果，无需再次刷新", Results)
            Page.Filter = PageInstanceMod.FilterType.All
            Page.SearchBox.Text = "gd656peek"
            AssertRegression(Page.IsSearching AndAlso Page.PanList.Children.OfType(Of MyLocalModItem).Any(Function(Item) Item.Entry.EnabledName = TargetName) AndAlso Page.PanList.Children.Count < 9,
                "Mod 搜索通过原生搜索事件筛出目标", Results)
            Page.Filter = PageInstanceMod.FilterType.Enabled
            Page.ED_Click(New MyIconButton With {.Tag = Page.ModItems(TargetName)}, EventArgs.Empty)
            AssertRegression((Page.PanList.Visibility = Visibility.Collapsed OrElse Not Page.PanList.Children.OfType(Of MyLocalModItem).Any(Function(Item) Item.Entry.EnabledName = TargetName)) AndAlso LocalResourceLoader.Output.Where(Function(Item) Item.IsEnabled).Count = 8,
                "搜索和已启用筛选组合下禁用目标立即隐藏", Results)
            Page.Filter = PageInstanceMod.FilterType.Disabled
            AssertRegression(Page.PanList.Visibility = Visibility.Visible AndAlso Page.PanList.Children.Count = 1,
                "搜索和已禁用筛选组合显示目标", Results)
            Page.ED_Click(New MyIconButton With {.Tag = Page.ModItems(TargetName)}, EventArgs.Empty)
            AssertRegression(Page.PanList.Visibility = Visibility.Collapsed AndAlso LocalResourceLoader.Output.All(Function(Item) Item.IsEnabled),
                "搜索和已禁用筛选组合下启用目标立即隐藏", Results)
            Page.SearchBox.Text = ""
            Page.Filter = PageInstanceMod.FilterType.All
            InvokePrivate(Page, "EDMods", LocalResourceLoader.Output.ToList(), False)
            AssertRegression(Directory.EnumerateFiles(Folder, "*.jar").Count = 0 AndAlso Directory.EnumerateFiles(Folder, "*.disabled").Count = 9 AndAlso
                LocalResourceLoader.Output.All(Function(Item) Not Item.IsEnabled), "原生批量禁用 9 个真实 Mod", Results)
            LoadMods(Page, Folder)
            AssertRegression(LocalResourceLoader.Output.Count = 9 AndAlso LocalResourceLoader.Output.All(Function(Item) Not Item.IsEnabled), "刷新后重新识别全部禁用 Mod", Results)
            InvokePrivate(Page, "EDMods", LocalResourceLoader.Output.ToList(), True)
            LoadMods(Page, Folder)
            AssertRegression(LocalResourceLoader.Output.Count = 9 AndAlso LocalResourceLoader.Output.All(Function(Item) Item.IsEnabled) AndAlso Page.PanList.Children.Count = 9,
                "批量启用及重新加载后列表和磁盘一致", Results)
            AssertRegression(Originals.All(Function(Item) File.Exists(Folder & Item.Key) AndAlso HashFile(Folder & Item.Key) = Item.Value),
                "单项及批量启停后全部 Mod SHA256 与输入一致", Results)
        Finally
            LocalResourceLoader.Cancel()
            LocalResourceOnlineLoader.Cancel()
            LocalResourceOnlineLoader = PreviousOnlineLoader
            FrmInstanceMod = PreviousPage
            PageInstanceLeft.Instance = PreviousInstance
            McFolderSelected = PreviousFolder
        End Try
    End Sub

    Private Function AnalyzeFile(Path As String, Results As List(Of String), Label As String) As String
        Dim Analyzer As New CrashAnalyzer()
        Analyzer.Import(Path)
        AssertRegression(Analyzer.Prepare(), Label & "：成功导入可分析日志", Results)
        Analyzer.Analyze()
        ' Obtain the same text shown by Output(), without opening dialogs or exporting reports.
        Return CStr(GetType(CrashAnalyzer).GetMethod("GetAnalyzeResult", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(Analyzer, {True}))
    End Function

    Private Sub RunDiagnosticExport(Results As List(Of String))
        Dim Folder = Paths.Base & "diagnostic-export-regression\"
        Directory.CreateDirectory(Folder)
        Dim Marker = "ABCDE-export-fixture-token-123456-VWXYZ"
        Dim Content = "java.lang.OutOfMemoryError: Java heap space" & vbCrLf &
            "--accessToken " & Marker & " --gameDir C:\Users\ExportFixtureUser\Minecraft" & vbCrLf &
            "current token: " & Marker & vbCrLf & "C:/Users/ExportFixtureUser/Minecraft"
        Dim Inputs = {"latest.log", "LatestLaunch.bat", "Log1.txt", "RawOutput.log"}.Select(Function(Name) Folder & Name).ToList()
        For Each Path In Inputs
            File.WriteAllText(Path, Content, New UTF8Encoding(False))
        Next
        Dim Hashes = Inputs.ToDictionary(Function(Path) Path, AddressOf HashFile)
        Dim Analyzer As New CrashAnalyzer()
        Analyzer.Import(Inputs(0))
        AssertRegression(Analyzer.Prepare(), "诊断导出样例成功准备日志", Results)
        Analyzer.Analyze()
        Dim Temporary = CStr(GetType(CrashAnalyzer).GetField("TempFolder", BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(Analyzer))
        AssertRegression(IO.Path.GetFullPath(Temporary).StartsWith(IO.Path.GetFullPath(Paths.Base & "selftest-temp\"), StringComparison.OrdinalIgnoreCase),
            "诊断导出暂存与清理限定于当前隔离测试目录", Results)
        Dim PreviousLogin = McLoginLoader.Output
        Try
            McLoginLoader.Output = New McLoginResult With {.AccessToken = Marker, .Uuid = "0123456789abcdef0123456789abcdef"}
            Dim Archive = Folder & "exported-report.zip"
            Analyzer.ExportReportArchive(Archive, Inputs.Skip(1).ToList())
            AssertRegression(File.Exists(Archive) AndAlso New FileInfo(Archive).Length > 0, "实际导出诊断报告 ZIP", Results)
            Dim Extracted = Folder & "verify\"
            FileUtils.ExtractToDirectory(Archive, Extracted)
            Dim Files = Directory.EnumerateFiles(Extracted, "*", SearchOption.AllDirectories).ToList()
            Dim Names = Files.Select(Function(Path) IO.Path.GetFileName(Path)).ToList()
            AssertRegression({"latest.log", "启动脚本.bat", "PCL 启动器日志.txt", "游戏崩溃前的输出.txt"}.All(Function(Name) Names.Contains(Name)),
                "报告 ZIP 包含游戏日志与正确命名的脚本和启动器输出", Results)
            Dim Texts = Files.ToDictionary(Function(Path) IO.Path.GetFileName(Path), Function(Path) File.ReadAllText(Path))
            AssertRegression(Texts.Values.All(Function(Text) Not Text.Contains(Marker) AndAlso Not Text.Contains("ExportFixtureUser")),
                "导出文件移除完整令牌和两种 Windows 路径中的用户名", Results)
            AssertRegression(Texts("启动脚本.bat").Contains(New String("F"c, Marker.Length - 10)) AndAlso Texts("latest.log").Contains("*****"),
                "脚本和文本日志沿用对应令牌遮盖字符", Results)
            AssertRegression(Texts.Values.All(Function(Text) Text.Contains("java.lang.OutOfMemoryError")), "报告脱敏后保留可诊断的异常信息", Results)
            AssertRegression(Hashes.All(Function(Item) HashFile(Item.Key) = Item.Value) AndAlso Not Directory.Exists(Temporary & "Report\"),
                "报告导出不改写输入日志，完成后清理隔离暂存报告", Results)
        Finally
            McLoginLoader.Output = PreviousLogin
        End Try
    End Sub

    Private Sub RunDiagnostics(Results As List(Of String))
        Dim Folder = Paths.Base & "diagnostic-regression\"
        Directory.CreateDirectory(Folder & "oom\")
        Dim Oom = Folder & "oom\latest.log"
        File.WriteAllText(Oom, "[12:00:00] [Render thread/ERROR]: java.lang.OutOfMemoryError: Java heap space" & vbCrLf &
            "[12:00:01] [Render thread/INFO]: Stopping!", New UTF8Encoding(False))
        Dim Hash = HashFile(Oom)
        AssertRegression(AnalyzeFile(Oom, Results, "手动导入内存不足样例").Contains("Minecraft 内存不足"), "诊断输出准确识别内存不足样例", Results)
        Dim Archive = Folder & "oom-report.zip"
        FileUtils.CreateZipFromDirectory(Archive, Folder & "oom\")
        AssertRegression(AnalyzeFile(Archive, Results, "导入错误报告 ZIP").Contains("Minecraft 内存不足"), "诊断 ZIP 与直接日志得到相同内存不足结论", Results)
        Directory.CreateDirectory(Folder & "unknown\")
        Dim Unknown = Folder & "unknown\latest.log"
        File.WriteAllLines(Unknown, Enumerable.Range(1, 120).Select(Function(Index) "[12:00:00] [Render thread/ERROR]: Unclassified regression marker " & Index), New UTF8Encoding(False))
        AssertRegression(AnalyzeFile(Unknown, Results, "导入未知错误样例").Contains("无法确定错误原因"), "未知错误保持无法确定的诊断提示", Results)
        Dim Live = Folder & "collect\"
        Directory.CreateDirectory(Live & "logs\")
        File.Copy(Oom, Live & "logs\latest.log", True)
        File.SetLastWriteTime(Live & "logs\latest.log", DateTime.Now)
        Dim Collected As New CrashAnalyzer()
        Collected.Collect(Live)
        AssertRegression(Collected.Prepare(), "自动收集独立实例的近期游戏日志", Results)
        Collected.Analyze()
        Dim Result = CStr(GetType(CrashAnalyzer).GetMethod("GetAnalyzeResult", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(Collected, {True}))
        AssertRegression(Result.Contains("Minecraft 内存不足") AndAlso HashFile(Oom) = Hash AndAlso HashFile(Live & "logs\latest.log") = Hash,
            "自动收集可生成诊断且不改写原日志", Results)
        Dim RealLog = Paths.Base & "regression-game-latest.log"
        If File.Exists(RealLog) Then
            Dim RealHash = HashFile(RealLog)
            Dim RealResult = AnalyzeFile(RealLog, Results, "导入真实 Forge 离线启动日志")
            AssertRegression(Not String.IsNullOrWhiteSpace(RealResult) AndAlso HashFile(RealLog) = RealHash,
                "真实 Forge 日志可生成诊断文字且不改写输入", Results)
            File.WriteAllText(Paths.Base & "real-log-diagnostic.txt", RealResult, New UTF8Encoding(False))
        End If
    End Sub
End Module
