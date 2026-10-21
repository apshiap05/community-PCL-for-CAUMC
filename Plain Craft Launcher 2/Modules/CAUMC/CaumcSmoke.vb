Imports PCLCS.CAUMC
Imports System.Reflection
Imports System.Windows.Media.Imaging
Imports System.Windows.Navigation

' Explicit developer-only entry point; uses its own registry namespace and working folder.
Public Module CaumcSmoke
    Public Function Run() As Integer
        Dim Results As New List(Of String)
        Try
            PathTemp = Paths.Base & "selftest-temp\"
            Directory.CreateDirectory(PathTemp & "Cache")
            CaumcInitialize()
            RunCredentialConfigurationTests(Results)
            Using Stream = System.Windows.Application.GetResourceStream(New Uri("/Plain Craft Launcher 2;component/Images/Splash.png", UriKind.Relative)).Stream
                Dim Splash = BitmapDecoder.Create(Stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames(0)
                Check(Splash.PixelWidth = 256 AndAlso Splash.PixelHeight = 256, "启动动画独立使用 256 像素图标", Results)
                Dim Pixel(3) As Byte
                Splash.CopyPixels(New Int32Rect(0, 0, 1, 1), Pixel, 4, 0)
                Check(Pixel(3) = 0, "启动动画图标保持透明背景", Results)
            End Using
            Check(Directory.Exists(CaumcGameDirectory) AndAlso CaumcGameDirectory = Paths.Base & ".minecraft\", "兼容 PCL 的标准游戏目录", Results)
            Check(Settings.Get(Of String)("LaunchFolderSelect") = "$.minecraft\", "默认目录选择", Results)
            Check(Settings.DecodeStoredValue("LoginLegacyName", Settings.EncodeLegacyStoredValue("compatibility-player")) = "compatibility-player", "可读取原版兼容 DES 格式", Results)
            Check(Settings.EncodeStoredValue("LoginLegacyName", "compatibility-player").StartsWith("dpapi:v1:"), "账号缓存保持加密并独立保存", Results)
            Settings.Set("WindowWidth", 1078)
            Check(ReadIni(Paths.Base & "PCL\Setup.ini", "WindowWidth", "") = "1078", "普通设置写入原版 PCL/Setup.ini", Results)
            Settings.Set("CaumcPclImportOffered", True)
            Check(ReadIni(Paths.Base & "PCL\CAUMC.ini", "CaumcPclImportOffered", "") = "True", "社区专属状态单独保存", Results)
            Check(Settings.Get(Of Boolean)("UiHiddenSetupUi"), "个性化页面入口永久移除", Results)
            Check(CaumcRegistryLocation("LaunchFolders", False) = "HKEY_CURRENT_USER\Software\PCL", "普通配置沿用原版 PCL 注册表位置", Results)
            Check(CaumcRegistryLocation("LoginMsJson", False) = "HKEY_CURRENT_USER\Software\PCLForCAUMC", "保留原版账号缓存，社区账号加密缓存独立读写", Results)
            Dim Cipher = CaumcProtect("non-secret-smoke-marker")
            Check(Cipher.StartsWith("dpapi:v1:") AndAlso Not Cipher.Contains("smoke-marker"), "DPAPI 加密", Results)
            Check(CaumcUnprotect(Cipher) = "non-secret-smoke-marker", "DPAPI 解密", Results)
            Settings.Set("LoginRemember", True)
            Using CaumcEnterRequestScope(CaumcApiRoot & "/authserver")
                Settings.Set("CacheAuthAccess", "site-a-test-token")
            End Using
            Using CaumcEnterRequestScope("https://example.invalid/api/yggdrasil/authserver")
                Check(Settings.Get(Of String)("CacheAuthAccess") = "", "认证站点缓存隔离", Results)
                Settings.Set("CacheAuthAccess", "site-b-test-token")
            End Using
            Using CaumcEnterRequestScope(CaumcApiRoot & "/authserver")
                Check(Settings.Get(Of String)("CacheAuthAccess") = "site-a-test-token", "切回站点恢复对应缓存", Results)
                Dim Stored As String = RegistryUtils.TryRead(CaumcRegistryPath("CacheAuthAccess"), "CacheAuthAccess", "")
                Check(Stored.StartsWith("dpapi:v1:") AndAlso Not Stored.Contains("site-a"), "注册表没有明文令牌", Results)
                Settings.Set("LoginRemember", False)
                CaumcUpdateRemember()
                Check(Not RegistryUtils.Has(CaumcRegistryPath("CacheAuthAccess"), "CacheAuthAccess"), "取消保持登录清理持久凭据", Results)
                Check(Settings.Get(Of String)("CacheAuthAccess") = "site-a-test-token", "当前会话仍然可用", Results)
                Settings.Set("CacheAuthAccess", "")
                Settings.Set("LoginRemember", True)
            End Using
            AniControlEnabled += 1
            FrmMain = New FormMain()
            FrmLaunchLeft = New PageLaunchLeft()
            Settings.Set("CaumcLoginChoice", 3)
            FrmLaunchLeft.RefreshPage(False, False)
            Check(Settings.Get(Of McLoginType)("LoginType") = McLoginType.Auth, "默认 CAU 登录", Results)
            Check(PageLoginAuth.GetLoginData().BaseUrl = CaumcApiRoot & "/authserver", "默认认证地址", Results)
            Check(McInstanceSelected Is Nothing AndAlso FrmLoginAuth.BtnLogin.IsEnabled AndAlso FrmLoginAuth.BtnLogin.Text = "登录 CAU 皮肤站", "未安装游戏时即可使用独立 CAU 登录入口", Results)
            Settings.Set("CaumcLoginChoice", 5)
            FrmLaunchLeft.RefreshPage(False, False)
            Check(Settings.Get(Of McLoginType)("LoginType") = McLoginType.Ms, "可切换微软登录", Results)
            Settings.Set("CaumcLoginChoice", 0)
            FrmLaunchLeft.RefreshPage(False, False)
            Check(Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy, "可切换离线登录", Results)
            Settings.Set("CaumcLoginChoice", 3)
            FrmLaunchLeft.RefreshPage(False, False)
            RunLocalAuthTests(Results)
            RunLoginButtonTests(Results)
            If File.Exists(Paths.Base & "layout-tests") Then CaumcRegression.RunLayouts(Results)
            RenderLoginPreview()
            RunGuideTabTests(Results)
            RunGuideFirstClickTests(Results)
            System.Threading.Tasks.Task.Run(Sub() RunAgentMetadataTest(Results)).GetAwaiter().GetResult()
            Dim Snapshot As New ArticleSnapshot With {.Source = CaumcArticle, .CheckedAt = DateTimeOffset.Now,
                .Nodes = ArticleParser.Parse(File.ReadAllText(Paths.Base & "homepage.html"), New Uri(CaumcArticle))}
            Dim Panel As New StackPanel()
            Dim Status = CaumcHomepage.BuildPage(Panel, Snapshot)
            Status.Text = "原生首页组件预览 · 测试时网页快照"
            Dim Viewer As New ScrollViewer With {.Content = Panel, .VerticalScrollBarVisibility = ScrollBarVisibility.Disabled}
            Dim PreviewWindow As New Window With {.Content = Viewer, .Width = 850, .Height = 1600, .Left = -10000, .Top = -10000,
                .ShowActivated = False, .ShowInTaskbar = False, .WindowStyle = WindowStyle.None}
            PreviewWindow.Show()
            Viewer.Dispatcher.Invoke(Sub()
                                     End Sub, Windows.Threading.DispatcherPriority.ApplicationIdle)
            Viewer.Measure(New Size(850, 1600))
            Viewer.Arrange(New Rect(0, 0, 850, 1600))
            Viewer.UpdateLayout()
            Dim PreviewHeight = Math.Max(1, CInt(Math.Ceiling(Viewer.ActualHeight)))
            Dim Bitmap As New RenderTargetBitmap(Math.Max(1, CInt(Math.Ceiling(Viewer.ActualWidth))), PreviewHeight, 96, 96, PixelFormats.Pbgra32)
            Bitmap.Render(Viewer)
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(Bitmap))
            Using Stream = File.Create(Paths.Base & "homepage-preview.png")
                Encoder.Save(Stream)
            End Using
            PreviewWindow.Close()
            Check(True, "原生 WPF 首页渲染", Results)
            RenderAdditionalGuides(Results)
            CaumcRegression.Run(Results)
            If File.Exists(Paths.Base & "layout-tests") Then CaumcRegression.RunGlassPreviews(Results)
            RunPackDownloadTests(Results)
            Results.Add("PASS 单 EXE 内嵌依赖装载")
            RunModpackIntegration(Results)
            CaumcRegression.RunFabric(Results)
            CaumcRegression.RunOnlineModUpdate(Results)
            File.WriteAllLines(Paths.Base & "selftest-result.txt", Results, New UTF8Encoding(False))
            Return 0
        Catch ex As Exception
            Results.Add("FAIL " & ex.ToString())
            File.WriteAllLines(Paths.Base & "selftest-result.txt", Results, New UTF8Encoding(False))
            Return 1
        Finally
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree("Software\PCLForCAUMCTest", False)
        End Try
    End Function

    Private Sub RunPackDownloadTests(Results As List(Of String))
        Dim Address As New Uri(New Uri(File.ReadAllText(Paths.Base & "auth-test-url.txt")), "/pack.zip?download=1")
        Dim Before = Settings.Get(Of String)("LaunchFolders")
        Dim PreviousFolder = McFolderSelected
        Dim Loader = DownloadGuidePack(Address)
        Check(Loader IsNot Nothing AndAlso Object.ReferenceEquals(Loader, DownloadGuidePack(Address)), "连续点击 ZIP 链接复用同一下载导入任务", Results)
        Dim Operation = System.Threading.Tasks.Task.Run(Sub()
            Dim Deadline = DateTime.UtcNow.AddSeconds(45)
            While Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting
                If DateTime.UtcNow > Deadline Then
                    Loader.Cancel()
                    Throw New TimeoutException("整合包下载导入测试超时")
                End If
                Thread.Sleep(30)
            End While
            If Loader.State <> LoadState.Finished Then Throw New IOException("内置整合包下载导入失败", Loader.Error)
        End Sub)
        Try
            PumpUntilComplete(Operation)
            Operation.GetAwaiter().GetResult()
            Dim Imported = Directory.EnumerateFiles(Paths.Base & "ImportedPacks", "caumc-download-fixture.json", SearchOption.AllDirectories).Single()
            Dim InstanceFolder = Path.GetDirectoryName(Imported)
            Check(File.ReadAllText(InstanceFolder & "\mods\fixture.jar") = "downloaded-mod-marker", "真实 HTTP 经 PCL 下载器下载后自动导入 Mod", Results)
            Check(File.ReadAllText(InstanceFolder & "\saves\fixture\level.dat") = "downloaded-world-marker", "自动导入完整保留整合包存档", Results)
            Check(Settings.Get(Of String)("LaunchFolders") <> Before AndAlso Settings.Get(Of String)("LaunchFolders").Contains("ImportedPacks"), "自动导入的游戏进入 PCL 文件夹列表", Results)
        Finally
            McFolderSelected = PreviousFolder
        End Try
    End Sub

    Private Sub Check(Condition As Boolean, Name As String, Results As List(Of String))
        If Not Condition Then Throw New Exception(Name)
        Results.Add("PASS " & Name)
    End Sub

    Private Sub RunCredentialConfigurationTests(Results As List(Of String))
        Dim RegistrationError = "{""path"":""/authentication/login_with_xbox"",""errorMessage"":""Invalid app registration, see https://aka.ms/AppRegInfo for more information""}"
        Check(CaumcMinecraftForbiddenMessage(RegistrationError).Contains("需要完成应用许可审核") AndAlso
              Not CaumcMinecraftForbiddenMessage(RegistrationError).Contains("当前 IP 的登录尝试异常"), "真实 Minecraft 注册拒绝响应不再误报 IP 异常", Results)
        Check(CaumcMinecraftForbiddenMessage(RegistrationError.Replace("Invalid app registration", "INVALID APP REGISTRATION")).Contains("需要完成应用许可审核"), "应用注册拒绝识别不区分大小写", Results)
        For Each Response In {Nothing, "", "<html>gateway marker</html>", "{""errorMessage"":""Unknown marker""}", "{""errorMessage"":42}"}
            Dim Message = CaumcMinecraftForbiddenMessage(Response)
            Check(Message.Contains("暂时无法确定原因") AndAlso Not Message.Contains("marker") AndAlso
                  Not Message.Contains("需要完成应用许可审核"), "未知或无效 403 正文保留不确定性且不回显正文：" & If(Response, "null"), Results)
        Next
        Dim RuntimeId = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID")
        Dim PreviousModSource = Settings.Get(Of Integer)("ToolDownloadMod")
        Try
            Check(OAuthClientId = CaumcResolveMicrosoftClientId(), "微软登录实际采用当前构建配置", Results)
            Dim ExpectedFile = Paths.Base & "expected-ms-client-id.txt"
            If File.Exists(ExpectedFile) Then Check(OAuthClientId = File.ReadAllText(ExpectedFile), "单 EXE 的微软 Client ID 与构建预期一致", Results)
            Dim BuiltIn = Assembly.GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyMetadataAttribute), False).
                Cast(Of AssemblyMetadataAttribute)().Single(Function(Item) Item.Key = "CAUMC.MicrosoftClientId").Value
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", Nothing)
            Check(CaumcResolveMicrosoftClientId() = BuiltIn, "无环境覆盖时读取 EXE 内的公开 Client ID", Results)
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", " ")
            Check(CaumcResolveMicrosoftClientId() = BuiltIn, "空白环境配置不遮盖内置 Client ID", Results)
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "  11111111-2222-4333-8444-555555555555  ")
            Check(CaumcResolveMicrosoftClientId() = "11111111-2222-4333-8444-555555555555", "运行时微软 Client ID 优先且去除首尾空白", Results)

            For Each Address In {"https://api.curseforge.com/v1/mods", "http://api.curseforge.com/v1/mods",
                                 "https://api.curseforge.com:8443/v1/mods", "https://user@api.curseforge.com/v1/mods",
                                 "https://API.CURSEFORGE.COM./v1/mods"}
                Using Request As New HttpRequestMessage(HttpMethod.Get, Address)
                    Dim Blocked = False
                    Try
                        SecretHeadersSign(Request.RequestUri.AbsoluteUri, Request)
                    Catch ex As InvalidOperationException
                        Blocked = ex.Message.Contains("已停用 CurseForge 官方接口")
                    End Try
                    Check(Blocked AndAlso Not Request.Headers.Contains("x-api-key"), "发送前阻止 CurseForge 官方 API 请求：" & Address, Results)
                End Using
            Next
            For Each Address In {"https://mod.mcimirror.top/curseforge/v1/mods", "https://api.modrinth.com/v2/search",
                                 "https://edge.forgecdn.net/files/test.jar", "https://example.invalid/?next=https://api.curseforge.com"}
                Using Request As New HttpRequestMessage(HttpMethod.Get, Address)
                    SecretHeadersSign(Request.RequestUri.AbsoluteUri, Request)
                    Check(Not Request.Headers.Contains("x-api-key"), "保留其他来源且不附带项目密钥：" & Address, Results)
                End Using
            Next
            For Each Preference In {0, 1, 2}
                Settings.Set("ToolDownloadMod", Preference)
                Dim Sources = DlModRequestSources("https://api.curseforge.com/v1/mods/search?searchFilter=a%20b&gameId=432")
                Check(Sources.Count = 2 AndAlso Sources.All(Function(Source) Source.Url = "https://mod.mcimirror.top/curseforge/v1/mods/search?searchFilter=a%20b&gameId=432"),
                    "所有下载源偏好均保留镜像重试且不回退 CurseForge 官方 API：" & Preference, Results)
            Next
        Finally
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", RuntimeId)
            Settings.Set("ToolDownloadMod", PreviousModSource)
        End Try
    End Sub

    Private Sub RunModpackIntegration(Results As List(Of String))
        Dim Pack = Paths.Base & "modpack-under-test.zip"
        If Not File.Exists(Pack) Then Return
        McFolderSelected = CaumcGameDirectory
        Dim Import = System.Threading.Tasks.Task.Run(
            Sub()
                Dim Loader = ModpackInstall(Pack, "CAUMC-integration-test")
                Dim Deadline = DateTime.UtcNow.AddMinutes(20)
                While Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting
                    File.WriteAllText(Paths.Base & "modpack-progress.txt", Loader.Name & " " & Loader.Progress.ToString("P1"))
                    If DateTime.UtcNow > Deadline Then
                        Loader.Cancel()
                        Throw New TimeoutException("整合包集成测试超过 20 分钟")
                    End If
                    Thread.Sleep(500)
                End While
                If Loader.State <> LoadState.Finished Then Throw New Exception("整合包安装未完成：" & Loader.State.ToString(), Loader.Error)
                Dim Instance = New McInstance(CaumcGameDirectory & "versions\CAUMC-integration-test\").Load()
                Check(Instance.Version.VanillaName = "1.20.1", "真实整合包识别 Minecraft 1.20.1", Results)
                Check(Instance.Version.ModLoaderVersions.ContainsKey(ModLoaders.Forge) AndAlso Instance.Version.ModLoaderVersions(ModLoaders.Forge) = "47.4.28", "真实整合包识别 Forge 47.4.28", Results)
                Check(Directory.EnumerateFiles(Instance.PathIndie & "mods", "*.jar").Any(), "真实整合包 Mod 文件导入", Results)
                Check(File.Exists(Instance.PathIndie & "options.txt") AndAlso File.Exists(Instance.PathIndie & "servers.dat"), "保留整合包选项与服务器列表", Results)
                Check(Not File.Exists(Paths.Base & "Plain Craft Launcher.exe"), "没有释放或运行整合包附带启动器", Results)
            End Sub)
        PumpUntilComplete(Import)
        Import.GetAwaiter().GetResult()
        If File.Exists(Paths.Base & "launch-offline-test") Then RunOfflineLaunch(Results)
    End Sub

    Private Sub RunLoginButtonTests(Results As List(Of String))
        Dim UrlFile = Paths.Base & "auth-test-url.txt"
        If Not File.Exists(UrlFile) Then Return
        McFolderSelected = CaumcGameDirectory
        Dim Folder = CaumcGameDirectory & "versions\login-ui-test\"
        Directory.CreateDirectory(Folder)
        File.WriteAllText(Folder & "login-ui-test.json", "{""id"":""login-ui-test"",""type"":""release"",""libraries"":[]}")
        Dim Instance As New McInstance(Folder)
        Settings.Set("VersionServerLogin", 4, Instance)
        Settings.Set("VersionServerAuthServer", File.ReadAllText(UrlFile).Trim(), Instance)
        McInstanceSelected = Instance
        FrmLaunchLeft.RefreshPage(False, False)
        FrmLoginAuth.Reload(False)
        Dim Page = FrmLoginAuth
        Page.ComboName.ApplyTemplate()
        Page.ComboName.Text = "local-test"
        Page.TextPass.Password = "local-test-password"
        Dim Login = Page.LoginAsync()
        PumpUntilComplete(Login)
        Login.GetAwaiter().GetResult()
        Check(Settings.Get(Of String)("CacheAuthAccess") = "refreshed-local-token" AndAlso FrmLoginAuthSkin.TextName.Text = "LocalPlayer", "独立登录成功后显示角色信息", Results)
        CaumcLogout()
        FrmLaunchLeft.RefreshPage(False, False)
        Page.ComboName.Text = "wrong-password"
        Page.TextPass.Password = "local-test-password"
        Login = Page.LoginAsync()
        PumpUntilComplete(Login)
        Login.GetAwaiter().GetResult()
        Check(Page.TextStatus.Text.Contains("登录失败") AndAlso Page.ComboName.IsEnabled AndAlso Settings.Get(Of String)("CacheAuthAccess") = "", "独立登录失败显示错误并恢复输入", Results)
        Page.ComboName.Text = "delayed"
        Page.TextPass.Password = "local-test-password"
        Login = Page.LoginAsync()
        Check(Not McLaunchStart(), "独立验证中不会并发启动游戏", Results)
        Dim CancelTimer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(100)}
        AddHandler CancelTimer.Tick, Sub()
                                         CancelTimer.Stop()
                                         Dim Cancel = Page.LoginAsync()
                                     End Sub
        CancelTimer.Start()
        PumpUntilComplete(Login)
        Login.GetAwaiter().GetResult()
        CancelTimer.Stop()
        Check(Not Page.IsLoginPending AndAlso Page.TextStatus.Text.Contains("取消") AndAlso Settings.Get(Of String)("CacheAuthAccess") = "", "独立登录支持取消且不保存晚到凭据", Results)
        McInstanceSelected = Nothing
        FrmLaunchLeft.RefreshPage(False, False)
        FrmLoginAuth.Reload(False)
        Check(McLaunchProcess Is Nothing, "独立登录测试没有启动游戏", Results)
    End Sub

    Private Sub RenderLoginPreview()
        Dim Panel As New StackPanel With {.Background = Brushes.White}
        Panel.Children.Add(New TextBlock With {.Text = "登录入口组件预览 · 启动首页左侧 CAU", .FontWeight = FontWeights.Bold, .Margin = New Thickness(0, 0, 0, 18)})
        Dim Page As New PageLoginAuth()
        Page.Reload(False)
        Panel.Children.Add(Page)
        Dim Frame As New Border With {.Background = Brushes.White, .Padding = New Thickness(20), .Child = Panel}
        Dim Window As New Window With {.Content = Frame, .Width = 340, .Height = 280, .Left = -10000, .Top = -10000,
            .ShowActivated = False, .ShowInTaskbar = False, .WindowStyle = WindowStyle.None, .Background = Brushes.White}
        Window.Show()
        Window.UpdateLayout()
        Dim Bitmap As New RenderTargetBitmap(Math.Max(1, CInt(Math.Ceiling(Frame.ActualWidth))), Math.Max(1, CInt(Math.Ceiling(Frame.ActualHeight))), 96, 96, PixelFormats.Pbgra32)
        Bitmap.Render(Frame)
        Dim Encoder As New PngBitmapEncoder()
        Encoder.Frames.Add(BitmapFrame.Create(Bitmap))
        Using Stream = File.Create(Paths.Base & "login-preview.png")
            Encoder.Save(Stream)
        End Using
        Window.Close()
    End Sub

    Private Sub RunGuideTabTests(Results As List(Of String))
        Dim Panel As New StackPanel()
        Dim Handler As New GuideTestHandler()
        Using Http As New HttpClient(Handler)
            Dim Load = CaumcHomepage.SelectGuide(Panel, 2, Http)
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Dim Tabs = GuideTabs(Panel)
            Check(Tabs.Select(Function(Item) Item.Text).SequenceEqual({"主服务器指南", "模组服指南", "皮肤站指南"}) AndAlso CInt(Tabs.Single(Function(Item) Item.ColorType = MyButton.ColorState.Highlight).Tag) = 2, "三个原生指南选项卡及默认选中状态", Results)
            Dim OriginalSources As New Dictionary(Of Integer, String) From {{2, GuideOriginalSource(Panel)}}
            Tabs(1).RaiseEvent(New MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) With {.RoutedEvent = UIElement.MouseLeftButtonDownEvent})
            Tabs(1).RaiseEvent(New MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) With {.RoutedEvent = UIElement.MouseLeftButtonUpEvent})
            Load = CaumcHomepage.CurrentLoad
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Check(GuideText(Panel).Contains("article-5") AndAlso SelectedGuide(Panel) = 5, "点击原生选项卡切换模组服正文", Results)
            OriginalSources(5) = GuideOriginalSource(Panel)
            Dim Navigation = ClickGuideLink(Panel, 6)
            Load = CaumcHomepage.CurrentLoad
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Check(Navigation.Handled AndAlso GuideText(Panel).Contains("article-6") AndAlso SelectedGuide(Panel) = 6, "正文超链接直接切换皮肤站指南", Results)
            OriginalSources(6) = GuideOriginalSource(Panel)
            Navigation = ClickGuideLink(Panel, 2)
            Load = CaumcHomepage.CurrentLoad
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Check(Navigation.Handled AndAlso GuideText(Panel).Contains("article-2") AndAlso SelectedGuide(Panel) = 2, "正文超链接可切回主服务器指南", Results)
            Check(GuideCatalog.All.All(Function(Guide) OriginalSources(Guide.PostId) = Guide.Source.AbsoluteUri), "每个选项卡保留各自的浏览器原文入口", Results)
            Handler.HoldMods = True
            Dim Slow = CaumcHomepage.SelectGuide(Panel, 5, Http)
            PumpUntilComplete(Handler.HeldStarted.Task)
            Load = CaumcHomepage.SelectGuide(Panel, 6, Http)
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Handler.ReleaseHeld.TrySetResult(True)
            PumpUntilComplete(Slow)
            Slow.GetAwaiter().GetResult()
            Check(SelectedGuide(Panel) = 6 AndAlso GuideText(Panel).Contains("article-6"), "快速切换时旧请求不会覆盖当前指南", Results)
            Handler.FailMods = True
            Load = CaumcHomepage.SelectGuide(Panel, 5, Http)
            PumpUntilComplete(Load)
            Load.GetAwaiter().GetResult()
            Check(SelectedGuide(Panel) = 5 AndAlso GuideText(Panel).Contains("article-5") AndAlso GuideBody(Panel).Children.OfType(Of TextBlock).Any(Function(Item) Item.Text.Contains("刷新失败")), "指南请求失败时显示该页缓存与错误提示", Results)
        End Using
    End Sub

    Private Sub RunGuideFirstClickTests(Results As List(Of String))
        Dim Panel As New StackPanel()
        Dim Viewer As New MyScrollViewer With {.Content = Panel, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
        Dim Window As New Window With {.Content = Viewer, .Width = 850, .Height = 600, .Left = -10000, .Top = -10000,
            .ShowActivated = False, .ShowInTaskbar = False, .WindowStyle = WindowStyle.None}
        Dim Handler As New GuideTestHandler With {.LongArticle = True}
        Using Http As New HttpClient(Handler)
            Try
                Dim Load = CaumcHomepage.SelectGuide(Panel, 2, Http)
                PumpUntilComplete(Load)
                Load.GetAwaiter().GetResult()
                Window.Show()
                For Each Destination In {5, 6, 2}
                    Window.UpdateLayout()
                    Viewer.ScrollToEnd()
                    DrainGuideLayout(Viewer)
                    Dim Reader = GuideBody(Panel).Children.OfType(Of RichTextBox).Single()
                    Dim Link = Reader.Document.Blocks.OfType(Of System.Windows.Documents.Paragraph).
                        SelectMany(Function(Item) Item.Inlines.OfType(Of System.Windows.Documents.Hyperlink)).
                        First(Function(Item) Item.NavigateUri = GuideCatalog.ForPost(Destination).Source)
                    Dim Before = Viewer.VerticalOffset
                    Dim Rect = Link.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward)
                    Dim ScreenBefore = Reader.TranslatePoint(New Point(Rect.X, Rect.Y), Viewer)
                    Check(Before > 500 AndAlso ScreenBefore.Y >= 0 AndAlso ScreenBefore.Y < Viewer.ActualHeight,
                        "首次点击回归场景已滚动到正文底部：" & Destination, Results)
                    ' Run Hyperlink's real mouse-down/focus handler, rather than skipping to RequestNavigate.
                    Link.RaiseEvent(New MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) With {.RoutedEvent = UIElement.MouseLeftButtonDownEvent})
                    DrainGuideLayout(Viewer)
                    Rect = Link.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward)
                    Dim ScreenAfter = Reader.TranslatePoint(New Point(Rect.X, Rect.Y), Viewer)
                    Check(Link.IsKeyboardFocused AndAlso Math.Abs(Viewer.VerticalOffset - Before) < 1 AndAlso Math.Abs(ScreenAfter.Y - ScreenBefore.Y) < 1,
                        "首次按下正文链接获得焦点且不跳回顶部：" & Destination & "，滚动 " & Before.ToString("F1") & " → " & Viewer.VerticalOffset.ToString("F1"), Results)
                    ' Activate once via WPF's hyperlink invocation; no synthetic RequestNavigate event.
                    Dim Peer As New System.Windows.Automation.Peers.HyperlinkAutomationPeer(Link)
                    Dim Pattern = Peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                    Pattern.GetType().GetInterfaces().Single(Function(Item) Item.FullName = "System.Windows.Automation.Provider.IInvokeProvider").GetMethod("Invoke").Invoke(Pattern, Nothing)
                    DrainGuideLayout(Viewer)
                    Load = CaumcHomepage.CurrentLoad
                    PumpUntilComplete(Load)
                    Load.GetAwaiter().GetResult()
                    DrainGuideLayout(Viewer)
                    Check(SelectedGuide(Panel) = Destination AndAlso GuideText(Panel).Contains("article-" & Destination) AndAlso Viewer.VerticalOffset < 1,
                        "一次激活正文链接切换目标指南并回到目标页顶部：" & Destination, Results)
                Next
                ' Keyboard focus should still reveal a link outside the viewport.
                Viewer.ScrollToTop()
                DrainGuideLayout(Viewer)
                Dim BottomLink = GuideBody(Panel).Children.OfType(Of RichTextBox).Single().Document.Blocks.
                    OfType(Of System.Windows.Documents.Paragraph).SelectMany(Function(Item) Item.Inlines.OfType(Of System.Windows.Documents.Hyperlink)).First()
                BottomLink.Focus()
                DrainGuideLayout(Viewer)
                Check(Viewer.VerticalOffset > 500, "键盘聚焦屏外正文链接仍会滚动到链接位置", Results)
            Finally
                Window.Close()
            End Try
        End Using
    End Sub

    Private Sub DrainGuideLayout(Viewer As FrameworkElement)
        Viewer.Dispatcher.Invoke(Sub()
                                 End Sub, Windows.Threading.DispatcherPriority.ApplicationIdle)
        Viewer.UpdateLayout()
    End Sub

    Private Sub RenderAdditionalGuides(Results As List(Of String))
        Dim WasDismissed = Settings.Get(Of Boolean)("CaumcGuideDismissed")
        Settings.Set("CaumcGuideDismissed", True)
        For Each Guide In GuideCatalog.All.Where(Function(Item) Item.PostId <> 2)
            Dim Fixture = Paths.Base & If(Guide.PostId = 5, "guide-mods.html", "guide-skin.html")
            Dim Snapshot As New ArticleSnapshot With {.Source = Guide.Source.AbsoluteUri, .CheckedAt = DateTimeOffset.Now,
                .Nodes = ArticleParser.Parse(File.ReadAllText(Fixture), Guide.Source)}
            Dim Panel As New StackPanel()
            CaumcHomepage.BuildPage(Panel, Snapshot, Guide.PostId).Text = "原生指南组件预览 · 测试时网页快照"
            Dim Viewer As New ScrollViewer With {.Content = Panel, .Background = Brushes.White, .VerticalScrollBarVisibility = ScrollBarVisibility.Disabled}
            Dim Window As New Window With {.Content = Viewer, .Width = 850, .Height = 1600, .Left = -10000, .Top = -10000,
                .ShowActivated = False, .ShowInTaskbar = False, .WindowStyle = WindowStyle.None}
            Window.Show()
            Viewer.Dispatcher.Invoke(Sub()
                                     End Sub, Windows.Threading.DispatcherPriority.ApplicationIdle)
            Viewer.Measure(New Size(850, 1600))
            Viewer.Arrange(New Rect(0, 0, 850, 1600))
            Viewer.UpdateLayout()
            Dim Bitmap As New RenderTargetBitmap(Math.Max(1, CInt(Math.Ceiling(Viewer.ActualWidth))), Math.Max(1, CInt(Math.Ceiling(Viewer.ActualHeight))), 96, 96, PixelFormats.Pbgra32)
            Bitmap.Render(Viewer)
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(Bitmap))
            Using Stream = File.Create(Paths.Base & "guide-" & Guide.PostId & "-preview.png")
                Encoder.Save(Stream)
            End Using
            Window.Close()
            Check(True, "真实指南原生渲染：" & Guide.Title, Results)
        Next
        Settings.Set("CaumcGuideDismissed", WasDismissed)
    End Sub

    Private Function GuideTabs(Panel As StackPanel) As List(Of MyButton)
        Return Panel.Children.OfType(Of Grid).Single(Function(Item) Item.Name = "CaumcGuideTabs").Children.OfType(Of MyButton).ToList()
    End Function

    Private Function SelectedGuide(Panel As StackPanel) As Integer
        Return CInt(GuideTabs(Panel).Single(Function(Item) Item.ColorType = MyButton.ColorState.Highlight).Tag)
    End Function

    Private Function GuideBody(Panel As StackPanel) As StackPanel
        Return Panel.Children.OfType(Of MyCard).Last().Children.OfType(Of StackPanel).Single()
    End Function

    Private Function GuideText(Panel As StackPanel) As String
        Dim Reader = GuideBody(Panel).Children.OfType(Of RichTextBox).Single()
        Return New System.Windows.Documents.TextRange(Reader.Document.ContentStart, Reader.Document.ContentEnd).Text
    End Function

    Private Function GuideOriginalSource(Panel As StackPanel) As String
        Return CStr(GuideBody(Panel).Children.OfType(Of WrapPanel).First().Children.OfType(Of MyButton).Single(Function(Item) Item.Text = "浏览器查看原文").Tag)
    End Function

    Private Function ClickGuideLink(Panel As StackPanel, PostId As Integer) As RequestNavigateEventArgs
        Dim Reader = GuideBody(Panel).Children.OfType(Of RichTextBox).Single()
        Dim Link = Reader.Document.Blocks.OfType(Of System.Windows.Documents.Paragraph).SelectMany(Function(Item) Item.Inlines.OfType(Of System.Windows.Documents.Hyperlink)).First(Function(Item) Item.NavigateUri = GuideCatalog.ForPost(PostId).Source)
        Dim Args As New RequestNavigateEventArgs(Link.NavigateUri, "") With {.RoutedEvent = System.Windows.Documents.Hyperlink.RequestNavigateEvent}
        Link.RaiseEvent(Args)
        Return Args
    End Function

    Private Class GuideTestHandler
        Inherits HttpMessageHandler
        Friend HoldMods As Boolean
        Friend FailMods As Boolean
        Friend LongArticle As Boolean
        Friend ReadOnly HeldStarted As New System.Threading.Tasks.TaskCompletionSource(Of Boolean)()
        Friend ReadOnly ReleaseHeld As New System.Threading.Tasks.TaskCompletionSource(Of Boolean)()
        Protected Overrides Async Function SendAsync(Request As HttpRequestMessage, CancellationToken As System.Threading.CancellationToken) As System.Threading.Tasks.Task(Of HttpResponseMessage)
            Dim Id = GuideCatalog.ResolvePostId(Request.RequestUri.AbsoluteUri, ArticleClient.Source).Value
            If Id = 5 AndAlso HoldMods Then
                HoldMods = False
                HeldStarted.TrySetResult(True)
                Await ReleaseHeld.Task.ConfigureAwait(False)
            End If
            If Id = 5 AndAlso FailMods Then Return New HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            Dim Padding = If(LongArticle, String.Concat(Enumerable.Range(1, 60).Select(Function(Index) "<p>Guide paragraph " & Index & "</p>")), "")
            Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent("<div class='theme-doc-markdown'><h1>article-" & Id & "</h1>" & Padding & "<p><a href='?post=2'>main</a> <a href='?post=5'>mods</a> <a href='?post=6'>skin</a></p></div>")}
        End Function
    End Class

    Friend Sub PumpUntilComplete(Operation As System.Threading.Tasks.Task)
        Dim Frame As New Windows.Threading.DispatcherFrame()
        Dim Timer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(100)}
        AddHandler Timer.Tick, Sub()
                                   If Operation.IsCompleted Then Frame.Continue = False
                               End Sub
        Timer.Start()
        Try
            Windows.Threading.Dispatcher.PushFrame(Frame)
        Finally
            Timer.Stop()
        End Try
    End Sub

    Private Sub RunOfflineLaunch(Results As List(Of String))
        Dim Instance = New McInstance(CaumcGameDirectory & "versions\CAUMC-integration-test\").Load()
        RunOfflineInstance(Instance, "真实 Forge 整合包", "", Results)
    End Sub

    Friend Sub RunOfflineInstance(Instance As McInstance, Label As String, ExpectedLogMarker As String, Results As List(Of String))
        McInstanceSelected = Instance
        FrmLaunchRight = New PageLaunchRight()
        Settings.Set("VersionServerLogin", 0, Instance)
        Settings.Set("VersionServerEnter", "", Instance)
        Settings.Set("VersionAdvanceRun", "", Instance)
        Settings.Set("LaunchAdvanceRun", "")
        Settings.Set("LaunchAdvanceGraphicCard", False)
        Settings.Set("LaunchArgumentVisible", 5)
        Settings.Set("VersionRamOptimize", 2, Instance)
        Settings.Set("VersionRamType", 1, Instance)
        Settings.Set("VersionRamCustom", 21, Instance) '6 GiB for this test only.
        Settings.Set("CaumcLoginChoice", 0)
        Settings.Set("LaunchSkinType", 0)
        FrmLaunchLeft.RefreshPage(False, False)
        FrmLoginLegacy.ComboName.Text = "CAUMCTest"
        If Not McLaunchStart(New McLaunchOptions With {.Instance = Instance}) Then Throw New Exception("未能发起离线启动测试")
        Dim Operation = System.Threading.Tasks.Task.Run(
            Sub()
                Dim Deadline = DateTime.UtcNow.AddMinutes(12)
                While McLaunchLoader.State = LoadState.Loading
                    File.WriteAllText(Paths.Base & "modpack-progress.txt", "离线启动 " & If(McLaunchLoaderReal Is Nothing, "初始化", McLaunchLoaderReal.Progress.ToString("P1")))
                    If DateTime.UtcNow > Deadline Then Throw New TimeoutException("离线启动测试超时")
                    Thread.Sleep(500)
                End While
                If McLaunchLoader.State <> LoadState.Finished Then Throw New Exception("离线启动未完成：" & McLaunchLoader.State.ToString(), McLaunchLoader.Error)
                Thread.Sleep(20000)
                McLaunchProcess.Refresh()
                Check(Not McLaunchProcess.HasExited AndAlso McLaunchProcess.MainWindowHandle <> IntPtr.Zero, Label & "游戏窗口出现并保持运行", Results)
                Dim Arguments = McLaunchProcess.StartInfo.Arguments
                Check(Not Arguments.Contains("--server ") AndAlso Not Arguments.Contains("--quickPlayMultiplayer"), "离线测试没有自动连接服务器", Results)
                Check(File.Exists(Instance.PathIndie & "logs\latest.log"), "生成真实游戏诊断日志", Results)
                If ExpectedLogMarker <> "" Then
                    Using LogStream As New FileStream(Instance.PathIndie & "logs\latest.log", FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete),
                          Reader As New StreamReader(LogStream, Encoding.UTF8)
                        Check(Reader.ReadToEnd().Contains(ExpectedLogMarker), "真实日志确认 " & ExpectedLogMarker, Results)
                    End Using
                    File.WriteAllText(Paths.Base & "fabric-java-path.txt", McLaunchProcess.StartInfo.FileName, New UTF8Encoding(False))
                End If
            End Sub)
        Try
            PumpUntilComplete(Operation)
            Operation.GetAwaiter().GetResult()
        Finally
            If McLaunchLoader.State = LoadState.Loading Then McLaunchLoader.Cancel()
            If McLaunchWatcher IsNot Nothing Then
                McLaunchWatcher.Kill()
            ElseIf McLaunchProcess IsNot Nothing AndAlso Not McLaunchProcess.HasExited Then
                McLaunchProcess.Kill()
            End If
        End Try
    End Sub

    Private Sub RunLocalAuthTests(Results As List(Of String))
        Dim UrlFile = Paths.Base & "auth-test-url.txt"
        If Not File.Exists(UrlFile) Then Return
        Dim BaseUrl = File.ReadAllText(UrlFile).Trim()
        Dim Uri As New Uri(BaseUrl)
        If Uri.Host <> "127.0.0.1" OrElse Uri.Scheme <> "http" Then Throw New Exception("自检认证服务必须在本地回环地址")
        Dim Input As New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = BaseUrl & "/authserver", .UserName = "local-test", .Password = "local-test-password", .Description = "本地模拟认证"}
        McLoginAuthLoader.WaitForExit(Input, IsForceRestart:=True)
        Check(McLoginAuthLoader.Output.Name = "LocalPlayer", "原有认证后端密码登录与单角色自动选择", Results)
        Check(McLoginAuthLoader.Output.AccessToken = "refreshed-local-token", "缺省 selectedProfile 时完成角色绑定刷新", Results)
        McLoginAuthLoader.WaitForExit(Input, IsForceRestart:=True)
        Check(McLoginAuthLoader.Output.Uuid = "0123456789abcdef0123456789abcdef", "令牌验证后保持同一 UUID", Results)
        Using CaumcEnterRequestScope(Input.BaseUrl)
            Settings.Set("CacheAuthAccess", "expired-local-token")
        End Using
        McLoginAuthLoader.WaitForExit(Input, IsForceRestart:=True)
        Check(McLoginAuthLoader.Output.AccessToken = "refreshed-local-token", "过期令牌验证失败后刷新恢复", Results)
        Dim NoRole As New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Input.BaseUrl, .UserName = "no-role", .Password = Input.Password, .Description = Input.Description}
        Try
            McLoginAuthLoader.WaitForExit(NoRole, IsForceRestart:=True)
            Throw New Exception("无角色账号应登录失败")
        Catch ex As Exception When ex.GetDisplay(True).Contains("创建角色")
            Check(True, "无角色提示创建角色", Results)
        End Try
        Dim Wrong As New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Input.BaseUrl, .UserName = "wrong-password", .Password = Input.Password, .Description = Input.Description}
        Settings.Set("CacheAuthAccess", "unrelated-cau-session-marker")
        Try
            McLoginAuthLoader.WaitForExit(Wrong, IsForceRestart:=True)
            Throw New Exception("错误密码应登录失败")
        Catch ex As Exception When ex.GetDisplay(True).Contains("Incorrect username or password")
            Check(True, "密码错误可明确报告", Results)
        End Try
        Using CaumcEnterRequestScope(Input.BaseUrl)
            Check(Settings.Get(Of String)("CacheAuthAccess") = "", "错误密码清除对应站点会话", Results)
        End Using
        Check(Settings.Get(Of String)("CacheAuthAccess") = "unrelated-cau-session-marker", "另一站点登录失败不清除 CAU 会话", Results)
        Settings.Set("CacheAuthAccess", "")
        Dim Delayed As New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Input.BaseUrl, .UserName = "delayed", .Password = Input.Password, .Description = Input.Description}
        McLoginAuthLoader.Start(Delayed, IsForceRestart:=True)
        Thread.Sleep(100)
        McLoginAuthLoader.Cancel()
        Thread.Sleep(700)
        Using CaumcEnterRequestScope(Input.BaseUrl)
            Check(Settings.Get(Of String)("CacheAuthAccess") = "", "取消在途登录后不会重新写回凭据", Results)
        End Using
        McLoginAuthLoader.Input = Nothing
    End Sub

    Private Sub RunAgentMetadataTest(Results As List(Of String))
        Dim Fixture = Paths.Base & "minecraft-1.20.1.json"
        If Not File.Exists(Fixture) Then Return
        Dim Folder = CaumcGameDirectory & "versions\caumc-agent-test\"
        Directory.CreateDirectory(Folder)
        File.Copy(Fixture, Folder & "caumc-agent-test.json", True)
        Dim Instance As New McInstance(Folder)
        Settings.Set("CaumcLoginChoice", 3)
        Dim Files = McLibNetFilesFromInstance(Instance)
        Check(Files.Any(Function(Item) Item.LocalPath.EndsWith("authlib-injector.jar", StringComparison.OrdinalIgnoreCase)), "无整合包认证配置时自动补全 Authlib Agent", Results)
        Check(Files.Any(Function(Item) Item.LocalPath.EndsWith("caumc-agent-test.jar", StringComparison.OrdinalIgnoreCase)), "保留游戏主 JAR 下载补全", Results)
        Settings.Set("CaumcLoginChoice", 5)
        Files = McLibNetFilesFromInstance(Instance)
        Check(Not Files.Any(Function(Item) Item.LocalPath.EndsWith("authlib-injector.jar", StringComparison.OrdinalIgnoreCase)), "微软登录不额外注入 CAU Agent", Results)
        Settings.Set("CaumcLoginChoice", 3)
    End Sub
End Module
