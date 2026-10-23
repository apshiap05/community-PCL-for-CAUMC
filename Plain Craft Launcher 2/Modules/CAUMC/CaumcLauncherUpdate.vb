Imports PCLCS.CAUMC
Imports System.Reflection
Imports System.Windows.Threading

Public Module CaumcLauncherUpdate
    Private Checking As Integer
    Private Installing As Integer
    Private ReadyPrompted As Boolean
    Private BootChecksDone As Boolean
    Private ReadOnly Gate As New Object()
    Private ReadyTimer As DispatcherTimer
    Private StartupResultHandled As Boolean
    Private UpdateExitPending As Boolean
    Private UpdateWorker As Process
    Friend Property UpdatePrompt As Func(Of LauncherRelease, Integer) = AddressOf PresentUpdate
    Friend Property RestartPrompt As Func(Of String, Boolean) = AddressOf PresentRestart
    Friend Property Busy As Func(Of Boolean) = AddressOf HasBusyWork
    Friend Property HelperStarter As Func(Of ProcessStartInfo, Process) = Function(Info) Process.Start(Info)
    Private ReadOnly Property StagingRoot As String
        Get
            Return If(Environment.GetCommandLineArgs().Contains("--caumc-selftest"), Paths.Base & "selftest-update-stage",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLForCAUMC", "Updates"))
        End Get
    End Property

    Friend Sub ResetForTests(Optional Ready As Boolean = False)
        If Not Environment.GetCommandLineArgs().Contains("--caumc-selftest") Then Throw New InvalidOperationException()
        ReadyTimer?.Stop()
        BootChecksDone = Ready : ReadyPrompted = False : StartupResultHandled = True
    End Sub

    Public ReadOnly Property CurrentVersion As Version
        Get
            Return LauncherUpdates.VersionOf(Assembly.GetExecutingAssembly().GetName().Version.ToString())
        End Get
    End Property
    Private ReadOnly Property UpdateFolder As String
        Get
            Return Paths.Base & "PCL\Updates\"
        End Get
    End Property

    Public Sub CheckManually()
        System.Threading.Tasks.Task.Run(Async Function() As System.Threading.Tasks.Task
            Await CheckAsync(True).ConfigureAwait(False)
        End Function)
    End Sub

    Public Async Function CheckAtStartupAsync() As System.Threading.Tasks.Task
        Await CheckAsync(False).ConfigureAwait(False)
        RunInUiWait(Sub()
            BootChecksDone = True
            TryPromptReady()
            BootChecksDone = False
        End Sub)
        While Volatile.Read(Installing) <> 0 OrElse UpdateExitPending
            Await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(False)
        End While
    End Function

    Public Sub FinishStartupChecks()
        BootChecksDone = True
        RunInUi(AddressOf StartReadyTimer)
    End Sub

    Friend Async Function CheckAsync(Manual As Boolean, Optional Client As HttpClient = Nothing) As System.Threading.Tasks.Task
        If Interlocked.CompareExchange(Checking, 1, 0) <> 0 Then
            If Manual Then RunInUi(Sub() Hint("正在检查启动器更新", HintType.Blue))
            Return
        End If
        Try
            If Not StartupResultHandled Then
                ReportResult()
                StartupResultHandled = True
            End If
            ' An accepted version remains pinned until installed or explicitly canceled.
            Dim Pending = PendingDownload()
            If Pending IsNot Nothing Then
                If Pending.State = "running" Then
                    If Manual Then RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
                ElseIf MyMsgBox("上次的启动器更新尚未完成，是否继续下载？", "继续启动器更新", "继续下载", "稍后", ForceWait:=True) = 1 Then
                    RunInUi(Sub() CaumcTaskCenter.ResumeTask(Pending.Id))
                End If
                Return
            End If
            Dim Ready = ReadReady()
            If Ready IsNot Nothing Then
                If Manual Then
                    ReadyPrompted = False
                    RunInUi(Sub()
                        If Busy.Invoke() Then Hint("新版已就绪，游戏退出且进行中的任务结束后可安装", HintType.Blue)
                        TryPromptReady()
                    End Sub)
                End If
                Return
            End If
            Dim OwnClient = Client Is Nothing
            If OwnClient Then Client = New HttpClient With {.Timeout = TimeSpan.FromSeconds(15)}
            Try
                Dim Service As New ArticleClient(Client, UpdateFolder & "article.json", LauncherUpdates.Source)
                Dim Article = Await Service.RefreshAsync().ConfigureAwait(False)
                Dim Latest = LauncherUpdates.Latest(Article, CurrentVersion)
                If Latest Is Nothing Then
                    If Manual Then MyMsgBox("当前已是最新正式版（" & LauncherUpdates.Display(CurrentVersion.ToString()) & "）。", "启动器更新", ForceWait:=True)
                    Return
                End If
                If Not Manual AndAlso Ignored(Latest.Version) Then Return
                Dim Choice = UpdatePrompt(Latest)
                If Choice = 3 Then IgnoreVersion(Latest.Version)
                If Choice = 1 Then RunInUi(Sub() StartDownload(Latest))
            Finally
                If OwnClient Then Client.Dispose()
            End Try
        Catch ex As Exception
            Logger.Warn(ex, "检查启动器更新失败")
            If Manual Then MyMsgBox(ex.GetDisplay(True), "检查启动器更新失败", IsWarn:=True, ForceWait:=True)
        Finally
            Interlocked.Exchange(Checking, 0)
        End Try
    End Function

    Private Function PresentUpdate(Release As LauncherRelease) As Integer
        Return MyMsgBox("当前版本：" & LauncherUpdates.Display(CurrentVersion.ToString()) & vbCrLf &
            "发现新版：" & LauncherUpdates.Display(Release.Version) & vbCrLf & vbCrLf & "下载完成后会再次询问是否重启安装。", "启动器更新",
            "现在更新", "稍后更新", "这个版本不再提醒", ForceWait:=True,
            LinkText:="查看发布页面", LinkUrl:=LauncherUpdates.Source.AbsoluteUri)
    End Function

    Private Function PresentRestart(Version As String) As Boolean
        Return MyMsgBox("新版 " & LauncherUpdates.Display(Version) & " 已下载完成。现在重启启动器并安装？", "更新已就绪", "重启并安装", "稍后", ForceWait:=True) = 1
    End Function

    Private Function PendingDownload() As TaskRecord
        Return CaumcTaskCenter.Snapshot().FirstOrDefault(Function(Item) CStr(Item.Recipe("type")) = "launcher-update" AndAlso
            (Item.State = "running" OrElse Item.NeedsAttention) AndAlso LauncherUpdates.VersionOf(CStr(Item.Recipe("version"))) > CurrentVersion)
    End Function

    Friend Function Ignored(Version As String) As Boolean
        SyncLock Gate
            Return ReadIgnored().Contains(LauncherUpdates.VersionOf(Version).ToString(4))
        End SyncLock
    End Function
    Private Function ReadIgnored() As HashSet(Of String)
        Try
            Return New HashSet(Of String)(JArray.Parse(Settings.Get(Of String)("CaumcIgnoredLauncherVersions")).Values(Of String)(), StringComparer.Ordinal)
        Catch ex As Newtonsoft.Json.JsonException
            Return New HashSet(Of String)(StringComparer.Ordinal)
        End Try
    End Function
    Friend Sub IgnoreVersion(Version As String)
        SyncLock Gate
            Dim Values = ReadIgnored()
            Values.Add(LauncherUpdates.VersionOf(Version).ToString(4))
            Settings.Set("CaumcIgnoredLauncherVersions", New JArray(Values.OrderBy(Function(Value) Value)).ToString(Newtonsoft.Json.Formatting.None))
        End SyncLock
    End Sub

    Public Function StartDownload(Release As LauncherRelease, Optional Archive As String = Nothing) As LoaderCombo(Of String)
        If Archive Is Nothing Then
            Dim Pending = PendingDownload()
            If Pending IsNot Nothing Then
                FrmMain.PageChange(FormMain.PageType.TaskManager)
                Return Nothing
            End If
        End If
        Dim Target = If(Archive, UpdateFolder & Guid.NewGuid().ToString("N") & ".exe")
        If Not PclDataImport.ContainsPath(UpdateFolder, Target) Then Throw New IOException("更新文件的缓存路径无效。")
        Dim Value As New JObject From {{"type", "launcher-update"}, {"version", Release.Version}, {"url", Release.Url}, {"archive", Target}}
        Dim Loader As New LoaderCombo(Of String)("启动器更新 " & LauncherUpdates.Display(Release.Version), {
            New LoaderDownload("下载启动器", New List(Of NetFile) From {New NetFile({Release.Url}, Target, New FileChecker With {.MinSize = 1024})}) With {.ProgressWeight = 9},
            New LoaderTask(Of String, Integer)("检查启动器文件", Sub(Task)
                Task.CreateCancellationToken().ThrowIfCancellationRequested()
                LauncherUpdates.ValidateExecutable(Target, Release.Version)
            End Sub) With {.ProgressWeight = 1}})
        CaumcTaskCenter.Recipe(Loader, Value)
        AddHandler Loader.OnStateChangedUi, Sub(Item, State, Previous)
            If State <> LoadState.Finished Then Return
            Settings.Set("CaumcReadyLauncherUpdate", Value.ToString(Newtonsoft.Json.Formatting.None))
            ReadyPrompted = False
            StartReadyTimer()
        End Sub
        Loader.Start("") : LoaderTaskbarAdd(Loader)
        Return Loader
    End Function

    Friend Function ReadReady() As JObject
        Try
            Dim Value = JObject.Parse(Settings.Get(Of String)("CaumcReadyLauncherUpdate"))
            Dim Archive = CStr(Value("archive"))
            If String.IsNullOrEmpty(Archive) Then Return Nothing
            If LauncherUpdates.VersionOf(CStr(Value("version"))) <= CurrentVersion Then
                If PclDataImport.ContainsPath(UpdateFolder, Archive) AndAlso File.Exists(Archive) Then File.Delete(Archive)
                Settings.Set("CaumcReadyLauncherUpdate", "{}")
                Return Nothing
            End If
            If Not PclDataImport.ContainsPath(UpdateFolder, Archive) Then Throw New IOException("更新缓存路径无效。")
            LauncherUpdates.ValidateExecutable(Archive, CStr(Value("version")))
            Return Value
        Catch ex As Exception
            Logger.Warn(ex, "读取已下载的启动器更新失败")
            Settings.Set("CaumcReadyLauncherUpdate", "{}")
            Return Nothing
        End Try
    End Function

    Private Function HasBusyWork() As Boolean
        Return HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading OrElse HasDownloadingTask() OrElse CaumcTaskCenter.HasRunning() OrElse HasRememberedGame()
    End Function

    Public Sub RememberGame(Game As Process)
        Try
            SyncLock Gate
                Dim Games = JArray.Parse(Settings.Get(Of String)("CaumcRunningMinecraft"))
                Games.Add(New JObject From {{"pid", Game.Id}, {"started", Game.StartTime.ToUniversalTime().Ticks}})
                Settings.Set("CaumcRunningMinecraft", Games.ToString(Newtonsoft.Json.Formatting.None))
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "记录 Minecraft 进程失败")
        End Try
    End Sub

    Friend Function HasRememberedGame() As Boolean
        SyncLock Gate
            Dim Alive As New JArray()
            Try
                For Each Entry In JArray.Parse(Settings.Get(Of String)("CaumcRunningMinecraft"))
                    Try
                        Using Game = Process.GetProcessById(CInt(Entry("pid")))
                            If Not Game.HasExited AndAlso Game.StartTime.ToUniversalTime().Ticks = CLng(Entry("started")) Then Alive.Add(Entry.DeepClone())
                        End Using
                    Catch ex As ArgumentException
                    Catch ex As System.ComponentModel.Win32Exception
                        Alive.Add(Entry.DeepClone())
                    End Try
                Next
                Dim Value = Alive.ToString(Newtonsoft.Json.Formatting.None)
                If Value <> Settings.Get(Of String)("CaumcRunningMinecraft") Then Settings.Set("CaumcRunningMinecraft", Value)
            Catch ex As Exception
                Logger.Warn(ex, "读取 Minecraft 进程记录失败")
            End Try
            Return Alive.Count > 0
        End SyncLock
    End Function
    Private Sub StartReadyTimer()
        If ReadyTimer Is Nothing Then
            ReadyTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
            AddHandler ReadyTimer.Tick, Sub() TryPromptReady()
        End If
        ReadyTimer.Start()
    End Sub
    Friend Sub TryPromptReady()
        If Not BootChecksDone OrElse ReadyPrompted OrElse IsProgramEnding OrElse Busy.Invoke() Then Return
        ReadyPrompted = True
        Dim Value = ReadReady()
        If Value Is Nothing Then Return
        If Not FrmMain.IsVisible Then CaumcTaskCenter.RestoreWindow()
        If RestartPrompt(CStr(Value("version"))) Then
            If Busy.Invoke() Then
                ReadyPrompted = False
                Return
            End If
            BeginInstall(Value)
        End If
    End Sub

    Public Sub InstallReady()
        ReadyPrompted = False
        If Busy.Invoke() Then
            Hint("新版已就绪，游戏退出且进行中的任务结束后将询问重启安装", HintType.Blue)
        Else
            TryPromptReady()
        End If
    End Sub

    Private Sub BeginInstall(Value As JObject)
        If Interlocked.CompareExchange(Installing, 1, 0) <> 0 Then Return
        Dim Root As String = Nothing
        Try
            Root = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(Root)
            Dim Package = Path.Combine(Root, "package.exe")
            File.Copy(CStr(Value("archive")), Package)
            Dim Self = Process.GetCurrentProcess()
            Dim Tx As New LauncherUpdateTransaction With {.Target = PathExe, .Package = Package, .Version = CStr(Value("version")),
                .Sha256 = LauncherUpdates.Hash(Package), .ParentId = Self.Id, .ParentStarted = Self.StartTime.ToUniversalTime().Ticks}
            Dim Transaction = Path.Combine(Root, "transaction.json")
            Tx.Validate(Transaction) : Tx.Save(Transaction)
            Dim Helper = Path.Combine(Root, "updater.exe")
            File.Copy(PathExe, Helper)
            Dim Elevated = False
            Dim Probe = Paths.Base & ".caumc-write-" & Guid.NewGuid().ToString("N")
            Try
                Using Stream = File.Create(Probe)
                End Using
                File.Delete(Probe)
            Catch ex As UnauthorizedAccessException
                Elevated = True
            End Try
            Dim Start As New ProcessStartInfo(Helper, "--caumc-update-helper """ & Transaction & """") With {
                .UseShellExecute = True, .WindowStyle = ProcessWindowStyle.Hidden, .WorkingDirectory = Root}
            If Elevated Then Start.Verb = "runas"
            Dim Worker = HelperStarter(Start)
            If Worker Is Nothing Then Throw New IOException("未能启动更新辅助程序，已保留下载文件。")
            UpdateWorker = Worker
            Settings.Set("CaumcLauncherUpdateTransaction", Transaction)
            ' Do not close until the worker has loaded and accepted the transaction.
            System.Threading.Tasks.Task.Run(Async Function() As System.Threading.Tasks.Task
                Try
                    For Attempt = 1 To 150
                        Await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(False)
                        If LauncherUpdateTransaction.Read(Transaction).Status = "waiting" Then
                            RunInUiWait(Sub()
                                If Busy.Invoke() Then
                                    Worker.Kill()
                                    Worker.WaitForExit(2000)
                                    CleanupSession(Root)
                                    Settings.Set("CaumcLauncherUpdateTransaction", "")
                                    Hint("已有游戏或任务开始运行，请稍后安装更新", HintType.Blue)
                                    Return
                                End If
                                Dim Accepted = LauncherUpdateTransaction.Read(Transaction)
                                Accepted.Status = "approved" : Accepted.Save(Transaction)
                                UpdateExitPending = True
                                FrmMain.EndProgram(False)
                            End Sub)
                            Return
                        End If
                        If Worker.HasExited Then Exit For
                    Next
                    If Not Worker.HasExited Then Worker.Kill()
                    Throw New IOException("更新辅助程序未能就绪，已保留下载文件。")
                Catch ex As Exception
                    If Not Worker.HasExited Then Worker.Kill()
                    Worker.WaitForExit(2000)
                    CleanupSession(Root)
                    Settings.Set("CaumcLauncherUpdateTransaction", "")
                    RunInUi(Sub() MyMsgBox(ex.Message, "安装启动器更新失败", IsWarn:=True))
                Finally
                    Interlocked.Exchange(Installing, 0)
                    Worker?.Dispose()
                End Try
            End Function)
        Catch ex As System.ComponentModel.Win32Exception When ex.NativeErrorCode = 1223
            CleanupSession(Root)
            Interlocked.Exchange(Installing, 0)
            Hint("已取消本次安装，更新文件仍保留", HintType.Blue)
        Catch ex As Exception
            CleanupSession(Root)
            Interlocked.Exchange(Installing, 0)
            MyMsgBox(ex.GetDisplay(True), "安装启动器更新失败", IsWarn:=True)
        End Try
    End Sub

    Public Sub WindowReady()
        Dim Args = Environment.GetCommandLineArgs()
        Dim Index = Array.IndexOf(Args, "--caumc-update-ready")
        If Index >= 0 AndAlso Index + 1 < Args.Length Then
            Try
                LauncherUpdateTransaction.Acknowledge(Args(Index + 1), PathExe, CurrentVersion.ToString())
            Catch ex As Exception
                Logger.Warn(ex, "写入启动器更新就绪状态失败")
            End Try
        End If
        System.Threading.Tasks.Task.Run(Async Function() As System.Threading.Tasks.Task
            ' Give the helper time to commit success and release its own executable.
            Await System.Threading.Tasks.Task.Delay(2000).ConfigureAwait(False)
            ReportResult()
        End Function)
    End Sub

    Friend Function CanExitForUpdate() As Boolean
        If Not UpdateExitPending Then Return True
        If Not Busy.Invoke() Then Return True
        CancelInstallExit()
        Return False
    End Function

    Public Sub CancelInstallExit()
        If Not UpdateExitPending Then Return
        UpdateExitPending = False
        Try
            If UpdateWorker IsNot Nothing AndAlso Not UpdateWorker.HasExited Then
                UpdateWorker.Kill()
                UpdateWorker.WaitForExit(2000)
            End If
            Dim Transaction = Settings.Get(Of String)("CaumcLauncherUpdateTransaction")
            If Not String.IsNullOrEmpty(Transaction) Then CleanupSession(Path.GetDirectoryName(Transaction))
            Settings.Set("CaumcLauncherUpdateTransaction", "")
        Catch ex As Exception
            Logger.Warn(ex, "停止更新辅助进程失败")
        End Try
    End Sub
    Private Sub ReportResult()
        Dim Transaction = Settings.Get(Of String)("CaumcLauncherUpdateTransaction")
        If String.IsNullOrEmpty(Transaction) OrElse Not File.Exists(Transaction) Then Return
        Try
            Dim Tx = LauncherUpdateTransaction.Read(Transaction)
            If Tx.Status = "succeeded" Then
                ClearReadyPackage()
                RunInUi(Sub() Hint("已更新至 " & LauncherUpdates.Display(Tx.Version), HintType.Green))
            ElseIf Tx.Status = "rolledback" OrElse Tx.Status = "failed" Then
                IgnoreVersion(Tx.Version)
                ClearReadyPackage()
                If File.Exists(Transaction & ".log") Then Logger.Info("更新辅助进程记录：" & vbCrLf & File.ReadAllText(Transaction & ".log"))
                RunInUi(Sub() MyMsgBox(Tx.Error, "启动器更新失败", IsWarn:=True))
            Else
                Return
            End If
            Settings.Set("CaumcLauncherUpdateTransaction", "")
            Dim Root = Path.GetDirectoryName(Transaction)
            CleanupSession(Root)
        Catch ex As Exception
            Logger.Warn(ex, "读取启动器更新结果失败")
        End Try
    End Sub

    Private Sub ClearReadyPackage()
        Dim Previous = JObject.Parse(Settings.Get(Of String)("CaumcReadyLauncherUpdate"))
        Settings.Set("CaumcReadyLauncherUpdate", "{}")
        Dim Archive = CStr(Previous("archive"))
        If Not String.IsNullOrEmpty(Archive) AndAlso PclDataImport.ContainsPath(UpdateFolder, Archive) AndAlso File.Exists(Archive) Then File.Delete(Archive)
    End Sub

    Private Sub CleanupSession(Root As String)
        If String.IsNullOrEmpty(Root) OrElse Not PclDataImport.ContainsPath(StagingRoot, Root) Then Return
        Try
            For Each Name In {"updater.exe", "package.exe", "transaction.json.ready", "transaction.json.log", "transaction.json.new", "transaction.json"}
                Dim Owned = Path.Combine(Root, Name)
                If File.Exists(Owned) Then File.Delete(Owned)
            Next
            If Directory.Exists(Root) AndAlso Not Directory.EnumerateFileSystemEntries(Root).Any() Then Directory.Delete(Root)
        Catch ex As IOException
            Logger.Warn(ex, "清理更新临时文件失败")
        Catch ex As UnauthorizedAccessException
            Logger.Warn(ex, "清理更新临时文件失败")
        End Try
    End Sub
End Module
