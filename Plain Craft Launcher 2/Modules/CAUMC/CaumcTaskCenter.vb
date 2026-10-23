Imports PCLCS.CAUMC
Imports System.Text.RegularExpressions

Public Module CaumcTaskCenter
    Private ReadOnly Gate As New Object()
    Private Journal As TaskJournal
    Private ReadOnly Running As New Dictionary(Of String, LoaderBase)()
    Private ReadOnly Ids As New ConcurrentDictionary(Of Integer, String)()
    Private ReadOnly Checkpoints As New ConcurrentDictionary(Of String, DownloadCheckpoint)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly ReusableFiles As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly Restoring As New ConcurrentDictionary(Of String, CancellationTokenSource)()
    Private ReadOnly Suppression As New AsyncLocal(Of Integer)()
    Private ReadOnly SuppressedOwner As New AsyncLocal(Of String)()
    Private ReadOnly OwnedChildren As New ConcurrentDictionary(Of String, ConcurrentQueue(Of LoaderBase))()
    Private Timer As System.Threading.Timer
    Private Saving As Integer
    Private Exiting As Boolean
    Private Tray As System.Windows.Forms.NotifyIcon
    Friend Property FailurePresenter As Action(Of TaskRecord) = AddressOf PresentFailure
    Private ReadOnly Property DataFolder As String
        Get
            Return Paths.Base & "PCL\Tasks\"
        End Get
    End Property
    Private ReadOnly Property CacheFolder As String
        Get
            Return DataFolder & "Downloads\"
        End Get
    End Property

    Public Sub Initialize()
        SyncLock Gate
            If Journal IsNot Nothing Then Return
            Journal = New TaskJournal(DataFolder & "tasks.json")
            DownloadCheckpoint.Sender = AddressOf CaumcSendAsync
            Timer = New System.Threading.Timer(Sub() Tick(), Nothing, 1000, 1000)
        End SyncLock
    End Sub

    Public Function Snapshot() As List(Of TaskRecord)
        Initialize()
        Return Journal.Summaries().OrderByDescending(Function(Item) Item.Created).ToList()
    End Function

    Public Sub NotifyPending()
        Initialize()
        If Journal.Snapshot().Any(Function(Item) Item.NeedsAttention) Then
            RunInUi(Sub() Hint("有未完成的任务，可进入顶部「任务」选择继续", HintType.Blue))
        End If
    End Sub

    Public Function Suppress(Optional Owner As LoaderBase = Nothing) As IDisposable
        Dim Previous = SuppressedOwner.Value
        If Owner IsNot Nothing Then SuppressedOwner.Value = Owner.RecoveryId
        Suppression.Value += 1
        Return New TrackingScope(Previous)
    End Function
    Private Class TrackingScope
        Implements IDisposable
        Private ReadOnly Previous As String
        Public Sub New(Value As String)
            Previous = Value
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Suppression.Value -= 1
            SuppressedOwner.Value = Previous
        End Sub
    End Class

    Public Sub Track(Loader As LoaderBase, Optional ReuseId As String = Nothing)
        If Not Loader.Show Then Return
        If Suppression.Value > 0 Then
            Loader.RecoverySilent = True
            If Not String.IsNullOrEmpty(SuppressedOwner.Value) Then
                Loader.RecoveryId = SuppressedOwner.Value
                Ids(Loader.Uuid) = SuppressedOwner.Value
                OwnedChildren.GetOrAdd(SuppressedOwner.Value, Function(Key) New ConcurrentQueue(Of LoaderBase)()).Enqueue(Loader)
            End If
            Return
        End If
        Initialize()
        Dim Id = If(ReuseId, Loader.RecoveryId)
        If String.IsNullOrEmpty(Id) AndAlso Ids.TryGetValue(Loader.Uuid, Id) Then Return
        If String.IsNullOrEmpty(Id) Then Id = Guid.NewGuid().ToString("N")
        Dim Record = If(Journal.Find(Id), New TaskRecord With {.Id = Id, .Name = Loader.Name})
        Record.Name = Loader.Name
        Record.Recipe = If(Loader.RecoveryRecipe, InferRecipe(Loader))
        Record.Kind = KindText(Record.Recipe, Loader.Name)
        Record.State = "running" : Record.Error = ""
        Record.Updated = DateTimeOffset.Now
        Loader.RecoveryId = Id
        SyncLock Gate
            Running(Id) = Loader
            Ids(Loader.Uuid) = Id
        End SyncLock
        Journal.Put(Record)
        AddHandler Loader.OnStateChangedUi, Sub(Item, NewState, OldState) Observe(Item)
        Observe(Loader)
        Save()
    End Sub

    Public Sub Recipe(Loader As LoaderBase, Value As JObject)
        SyncLock Gate
            SetRecipe(Loader, Value)
        End SyncLock
    End Sub
    Private Sub SetRecipe(Loader As LoaderBase, Value As JObject)
        Loader.RecoveryRecipe = Value
        Dim Id As String = Nothing
        If Ids.TryGetValue(Loader.Uuid, Id) Then
            Dim Item = Journal.Find(Id)
            If Item IsNot Nothing Then
                Item.Recipe = Value : Item.Kind = KindText(Value, Loader.Name)
                Journal.Put(Item) : Save()
            End If
        End If
    End Sub

    Public Function LocalPack(Loader As LoaderCombo(Of String), Archive As String, Folder As String) As LoaderCombo(Of String)
        If Suppression.Value = 0 AndAlso Loader IsNot Nothing Then
            Recipe(Loader, New JObject From {{"type", "local-pack"}, {"archive", Archive}, {"folder", Folder}, {"version", Loader.Input}})
            AddHandler Loader.OnStateChangedUi, Sub(Item, State, Previous)
                If State <> LoadState.Finished Then Return
                Try
                    Dim VersionPath = ResolvePackVersion(Loader.Input, Archive)
                    If IsVersionPath(VersionPath) Then
                        Directory.CreateDirectory(Path.Combine(VersionPath, "PCL"))
                        File.WriteAllText(Path.Combine(VersionPath, "PCL", "CAUMC-source.json"), New JObject From {{"name", Path.GetFileName(Archive)}}.ToString())
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "保存整合包来源失败")
                End Try
            End Sub
        End If
        Return Loader
    End Function

    Private Function InferRecipe(Loader As LoaderBase) As JObject
        Dim Parts = Leaves(Loader).ToList()
        If Parts.Count > 0 AndAlso Parts.All(Function(Item) TypeOf Item Is LoaderDownload) Then
            Return New JObject From {{"type", "files"}}
        End If
        Return New JObject From {{"type", "native"}, {"folder", McFolderSelected}}
    End Function
    Private Function KindText(Value As JObject, Name As String) As String
        Select Case CStr(Value("type"))
            Case "guide-pack" : Return "模组服整合包"
            Case "local-pack" : Return "整合包导入"
            Case "pcl-import" : Return "旧 PCL 导入"
            Case "install" : Return "游戏安装"
            Case "repair" : Return "依赖下载"
            Case "files" : Return "文件下载"
            Case "save" : Return "文件下载"
            Case "resource-pack" : Return "整合包安装"
            Case "export" : Return "整合包导出"
            Case "mod-update" : Return "Mod 更新"
            Case "modify" : Return "版本修改"
            Case Else : Return If(Name.Contains("导出"), "整合包导出", If(Name.Contains("Mod"), "Mod 管理", "安装与下载"))
        End Select
    End Function

    Private Function Leaves(Loader As LoaderBase) As IEnumerable(Of LoaderBase)
        Dim Field = Loader.GetType().GetField("Loaders")
        If Field Is Nothing Then Return {Loader}
        Dim Children = TryCast(Field.GetValue(Loader), IEnumerable(Of LoaderBase))
        If Children Is Nothing Then Return {Loader}
        Return Children.SelectMany(Function(Child) Leaves(Child)).ToList()
    End Function

    Private Function Spec(File As NetFile) As DownloadSpec
        Return New DownloadSpec With {.Path = File.LocalPath, .Urls = File.Sources.Select(Function(Source) Source.Url).ToArray(),
            .ActualSize = If(File.Check?.ActualSize, -1), .MinSize = If(File.Check?.MinSize, -1), .Hash = File.Check?.Hash,
            .IsJson = If(File.Check?.IsJson, False), .BrowserHeaders = File.SimulateBrowserHeaders}
    End Function

    Public Function Capture(File As NetFile, Response As HttpResponseMessage) As DownloadCheckpoint
        If Exiting Then Return Nothing
        Try
            If Not File.Tasks.Any(Function(Loader) Ids.ContainsKey(If(Loader.RealParent, Loader).Uuid)) Then Return Nothing
            Dim Cache = Checkpoints.GetOrAdd(File.LocalPath, Function(Target) DownloadCheckpoint.Open(CacheFolder, Target))
            Cache.Begin(File.FileSize, DownloadCheckpoint.Validator(Response), File.Check?.Hash)
            Return Cache
        Catch ex As Exception
            Logger.Warn(ex, "建立下载恢复缓存失败")
            Return Nothing
        End Try
    End Function

    Public Sub ReuseDownload(File As NetFile)
        Dim Candidate As String = Nothing
        If String.IsNullOrEmpty(File.Check?.Hash) OrElse Not ReusableFiles.TryGetValue(File.Check.Hash, Candidate) Then Return
        If PathUtils.IsSame(Candidate, File.LocalPath) OrElse File.Check.Check(Candidate) IsNot Nothing Then Return
        Directory.CreateDirectory(Path.GetDirectoryName(File.LocalPath))
        System.IO.File.Copy(Candidate, File.LocalPath, True)
    End Sub

    Public Sub DownloadFinished(File As NetFile)
        If String.IsNullOrEmpty(File.LocalPath) OrElse String.IsNullOrEmpty(PathTemp) Then Return
        If PclDataImport.ContainsPath(PathTemp, File.LocalPath) OrElse PclDataImport.ContainsPath(DataFolder & "Packages", File.LocalPath) Then Return
        Dim Cache As DownloadCheckpoint = Nothing
        If Checkpoints.TryRemove(File.LocalPath, Cache) Then
            Try
                Cache.Delete()
            Catch ex As IOException
                Logger.Warn(ex, "清理已完成文件的恢复缓存失败")
            End Try
        End If
    End Sub

    Private Sub Observe(Loader As LoaderBase)
        Try
            SyncLock Gate
                ObserveCore(Loader)
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "更新任务记录失败")
        End Try
    End Sub
    Private Sub ObserveCore(Loader As LoaderBase)
        Dim Id As String = Nothing
        If Not Ids.TryGetValue(Loader.Uuid, Id) Then Return
        Dim Item = Journal.Find(Id)
        If Item Is Nothing OrElse {"paused", "interrupted", "succeeded", "canceled", "failed"}.Contains(Item.State) Then Return
        Dim Previous = Item.State
        Dim Parts = Leaves(Loader).ToList()
        Dim Children As ConcurrentQueue(Of LoaderBase) = Nothing
        If OwnedChildren.TryGetValue(Id, Children) Then Parts.AddRange(Children.SelectMany(Function(Child) Leaves(Child)))
        Dim Stage = Parts.LastOrDefault(Function(Part) Part.State = LoadState.Loading)
        Item.Stage = If(Stage?.Name, Item.Stage)
        If Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting Then
            Item.Progress = Math.Max(0, Math.Min(1, Loader.Progress))
        End If
        Dim Downloads = Item.Downloads.ToDictionary(Function(Entry) Entry.Path, StringComparer.OrdinalIgnoreCase)
        For Each Part In Parts.OfType(Of LoaderDownload)()
            For Each File In Part.Files.ToArray()
                If File.State = NetState.Finished AndAlso CStr(Item.Recipe("type")) <> "files" AndAlso
                    Not String.Equals(File.LocalPath, CStr(Item.Recipe("archive")), StringComparison.OrdinalIgnoreCase) AndAlso
                    Not PclDataImport.ContainsPath(PathTemp, File.LocalPath) AndAlso Not PclDataImport.ContainsPath(DataFolder & "Packages", File.LocalPath) Then
                    Downloads.Remove(File.LocalPath)
                Else
                    Downloads(File.LocalPath) = Spec(File)
                End If
            Next
        Next
        Item.Downloads = Downloads.Values.ToList()
        Select Case Loader.State
            Case LoadState.Finished
                Item.State = "succeeded" : Item.Progress = 1 : Item.Stage = "已完成"
                If {"local-pack", "resource-pack"}.Contains(CStr(Item.Recipe("type"))) Then
                    Try
                        Dim Input = If(CStr(Item.Recipe("version")), TryCast(Loader.GetType().GetField("Input")?.GetValue(Loader), String))
                        Dim Candidate = ResolvePackVersion(Input, CStr(Item.Recipe("archive")))
                        If IsVersionPath(Candidate) Then
                            Item.Recipe("version") = Candidate
                            Directory.CreateDirectory(Path.Combine(Candidate, "PCL"))
                            Dim Name = If(CStr(Item.Recipe("sourceName")), Path.GetFileName(CStr(Item.Recipe("archive"))))
                            File.WriteAllText(Path.Combine(Candidate, "PCL", "CAUMC-source.json"), New JObject From {{"name", Name}}.ToString())
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, "保存整合包来源失败")
                    End Try
                End If
            Case LoadState.Failed
                Item.State = "failed" : Item.Error = If(Loader.Error?.GetDisplay(True), "任务失败，请查看日志。")
            Case LoadState.Canceled
                Item.State = "canceled" : Item.Stage = "已取消"
        End Select
        Item.Updated = DateTimeOffset.Now
        Journal.Put(Item)
        If Item.State <> "running" Then
            Save()
            If Item.State = "succeeded" OrElse Item.State = "canceled" Then
                Cleanup(Item)
                Item.Downloads.Clear() : Journal.Put(Item) : Save()
            End If
            If Item.State = "failed" AndAlso Previous <> "failed" AndAlso Not Exiting Then
                RunInUi(Sub() FailurePresenter.Invoke(Item))
            End If
        End If
    End Sub

    Private Sub PresentFailure(Item As TaskRecord)
        If Tray IsNot Nothing AndAlso Not FrmMain.IsVisible Then RestoreWindow()
        MyMsgBox(Item.Error, Item.Name & "失败", "知道了", "查看任务", IsWarn:=True,
            Button2Action:=Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
    End Sub

    Friend Sub ReloadForTests()
        If Not Environment.GetCommandLineArgs().Contains("--caumc-selftest") Then Throw New InvalidOperationException()
        Tick()
        SyncLock Gate
            Journal = New TaskJournal(DataFolder & "tasks.json")
            Running.Clear() : Ids.Clear() : Checkpoints.Clear()
        End SyncLock
    End Sub
    Friend Sub CheckpointNow()
        Tick()
    End Sub
    Friend Sub SeedForTests(Item As TaskRecord)
        If Not Environment.GetCommandLineArgs().Contains("--caumc-selftest") Then Throw New InvalidOperationException()
        Initialize() : Journal.Put(Item) : Save()
    End Sub

    Private Sub Tick()
        If Interlocked.Exchange(Saving, 1) <> 0 OrElse Exiting Then Return
        Try
            Dim Loaders As List(Of LoaderBase)
            SyncLock Gate
                Loaders = Running.Values.ToList()
            End SyncLock
            For Each Loader In Loaders
                Observe(Loader)
            Next
            For Each Cache As DownloadCheckpoint In Checkpoints.Values
                Cache.Flush()
            Next
            Save()
        Catch ex As Exception
            Logger.Warn(ex, "保存任务记录失败")
        Finally
            Interlocked.Exchange(Saving, 0)
        End Try
    End Sub
    Private Sub Save()
        Try
            Journal?.Save()
        Catch ex As Exception
            Logger.Warn(ex, "写入任务记录失败")
        End Try
    End Sub

    Public Function IsTracked(Loader As LoaderBase) As Boolean
        Return Loader.RecoverySilent OrElse Ids.ContainsKey(Loader.Uuid)
    End Function
    Public Function HasPendingPack(Address As Uri) As Boolean
        Return Snapshot().Any(Function(Item) (Item.State = "running" OrElse Item.NeedsAttention) AndAlso
            CStr(Item.Recipe("type")) = "guide-pack" AndAlso CStr(Item.Recipe("url")) = GuidePackUpdates.Key(Address))
    End Function
    Public Function HasRunning() As Boolean
        Return Not Restoring.IsEmpty OrElse LoaderTaskbar.Any(Function(Loader) Loader.Show AndAlso Loader.State = LoadState.Loading)
    End Function
    Public Function CanPause(Item As TaskRecord) As Boolean
        If Item.State <> "running" Then Return False
        If Restoring.ContainsKey(Item.Id) Then Return Item.Stage.StartsWith("恢复下载")
        Dim Loader As LoaderBase = Nothing
        SyncLock Gate
            Running.TryGetValue(Item.Id, Loader)
        End SyncLock
        If Loader Is Nothing Then Return False
        Dim Parts = Leaves(Loader).Where(Function(Part) Part.State = LoadState.Loading).ToList()
        Return {"files", "guide-pack", "save"}.Contains(CStr(Item.Recipe("type"))) AndAlso
            Parts.Count > 0 AndAlso Parts.All(Function(Part) TypeOf Part Is LoaderDownload)
    End Function
    Public Sub StopTask(Id As String, Pause As Boolean)
        SyncLock Gate
            StopTaskCore(Id, Pause)
        End SyncLock
    End Sub
    Private Sub StopTaskCore(Id As String, Pause As Boolean)
        CheckpointNow()
        Dim Item = Journal.Find(Id)
        If Item Is Nothing Then Return
        If Pause AndAlso Not CanPause(Item) Then Return
        Item.State = If(Pause, "paused", "canceled")
        Item.Stage = If(Pause, "已暂停", "已取消")
        Journal.Put(Item) : Save()
        Dim Token As CancellationTokenSource = Nothing
        If Restoring.TryGetValue(Id, Token) Then Token.Cancel()
        Dim Loader As LoaderBase = Nothing
        SyncLock Gate
            Running.TryGetValue(Id, Loader)
        End SyncLock
        For Each Cache As DownloadCheckpoint In Checkpoints.Values
            Cache.Flush()
        Next
        If Loader IsNot Nothing Then RunInThread(Sub() Loader.Cancel())
        If Not Pause Then
            System.Threading.Tasks.Task.Run(Async Function()
                Await System.Threading.Tasks.Task.Delay(1200)
                If Journal.Find(Id)?.State = "canceled" Then
                    Cleanup(Item)
                    Item.Downloads.Clear() : Journal.Put(Item) : Save()
                End If
            End Function)
        End If
    End Sub

    Public Function CanResume(Item As TaskRecord) As Boolean
        Dim Loader As LoaderBase = Nothing
        SyncLock Gate
            Running.TryGetValue(Item.Id, Loader)
        End SyncLock
        If Loader IsNot Nothing AndAlso Loader.State = LoadState.Loading Then Return False
        Return Item.NeedsAttention AndAlso Not Restoring.ContainsKey(Item.Id) AndAlso
            CStr(Item.Recipe("type")) <> "native"
    End Function
    Public Sub ResumeTask(Id As String)
        Dim Item = Journal.Find(Id)
        If Item Is Nothing OrElse Not CanResume(Item) Then Return
        Dim Token As New CancellationTokenSource()
        If Not Restoring.TryAdd(Id, Token) Then Return
        Dim PreviousError = Item.Error
        SyncLock Gate
            Dim Previous As LoaderBase = Nothing
            If Running.TryGetValue(Id, Previous) Then
                Running.Remove(Id)
                Dim RemovedId As String = Nothing
                Ids.TryRemove(Previous.Uuid, RemovedId)
            End If
            Dim Children As ConcurrentQueue(Of LoaderBase) = Nothing
            If OwnedChildren.TryRemove(Id, Children) Then
                For Each Child In Children
                    Dim RemovedId As String = Nothing
                    Ids.TryRemove(Child.Uuid, RemovedId)
                Next
            End If
            Item.State = "running" : Item.Stage = "恢复下载进度" : Item.Error = ""
            Journal.Put(Item) : Save()
        End SyncLock
        System.Threading.Tasks.Task.Run(Async Function()
            Try
                PreparePackRetry(Item, PreviousError)
                Using Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(45)}
                    For Each Download In Item.Downloads
                        Token.Token.ThrowIfCancellationRequested()
                        Dim Cache = Checkpoints.GetOrAdd(Download.Path, Function(Target) DownloadCheckpoint.Open(CacheFolder, Target))
                        Dim LastReport = DateTime.UtcNow
                        If Await Cache.RestoreAsync(Http, Download, Token.Token, Sub(Progress)
                            If (DateTime.UtcNow - LastReport).TotalMilliseconds < 500 Then Return
                            LastReport = DateTime.UtcNow
                            ReportRecovery(Id, "恢复下载 · " & Path.GetFileName(Download.Path) & " · " & Math.Floor(Progress * 100) & "%")
                        End Sub).ConfigureAwait(False) AndAlso Not String.IsNullOrEmpty(Download.Hash) Then ReusableFiles(Download.Hash) = Download.Path
                    Next
                End Using
                Token.Token.ThrowIfCancellationRequested()
                ReportRecovery(Id, "准备恢复任务")
                RunInUiWait(Sub() Restart(Item, Token.Token))
            Catch ex As OperationCanceledException
                ' StopTask or exit has already persisted the requested state.
            Catch ex As Exception
                Item = Journal.Find(Id)
                If Item IsNot Nothing AndAlso Item.State = "running" Then
                    Item.State = "failed" : Item.Error = ex.GetDisplay(True)
                    Journal.Put(Item) : Save()
                    RunInUi(Sub() FailurePresenter.Invoke(Item))
                End If
            Finally
                Dim Removed As CancellationTokenSource = Nothing
                Restoring.TryRemove(Id, Removed)
                Token.Dispose()
            End Try
        End Function)
    End Sub

    Private Sub ReportRecovery(Id As String, Stage As String)
        SyncLock Gate
            Dim Record = Journal.Find(Id)
            If Record?.State <> "running" Then Return
            Record.Stage = Stage
            Journal.Put(Record)
        End SyncLock
    End Sub

    Private Sub Restart(Item As TaskRecord, Token As CancellationToken)
        Token.ThrowIfCancellationRequested()
        Dim Loader As LoaderBase = Nothing
        Dim Value = Item.Recipe
        For Each Previous In LoaderTaskbar.ToList().Where(Function(Entry) Entry.Name = Item.Name AndAlso Entry.State <> LoadState.Loading)
            LoaderTaskbar.Remove(Previous)
        Next
        Using Suppress()
            Select Case CStr(Value("type"))
                Case "guide-pack"
                    Dim Folder = CStr(Value("folder"))
                    If Not Directory.Exists(Folder) Then Throw New DirectoryNotFoundException("原游戏目录已不存在：" & Folder)
                    McFolderSelected = Folder
                    Loader = DownloadGuidePack(New Uri(CStr(Value("url"))), CStr(Value("name")), CStr(Value("archive")))
                Case "files"
                    Dim Files = Item.Downloads.Select(Function(Download) New NetFile(Download.Urls, Download.Path, Download.Checker(), Download.BrowserHeaders)).ToList()
                    Dim Combo As New LoaderCombo(Of String)(Item.Name, {New LoaderDownload("下载文件", Files)})
                    Combo.Start() : LoaderTaskbarAdd(Combo) : Loader = Combo
                Case "install"
                    Dim Request = ReadInstallRequest(Value("request"))
                    McFolderSelected = CStr(Value("folder"))
                    Loader = CaumcRestartInstall(Request, Item.Name)
                    Value("request") = JObject.FromObject(Request)
                Case "local-pack"
                    McFolderSelected = CStr(Value("folder"))
                    Loader = ModpackInstall(CStr(Value("archive")), Automatic:=True)
                Case "pcl-import"
                    Loader = CaumcImportSources(Value("sources").ToObject(Of List(Of String))(), Value)
                Case "repair"
                    Loader = CaumcRestartRepair(CStr(Value("version")), Item.Name)
                Case "save"
                    Loader = RestartSavedDownload(Value)
                Case "export"
                    Loader = PageInstanceExport.StartExportTask(CType(Value("request"), JObject))
                Case "modify"
                    Dim Before = LoaderTaskbar.ToList()
                    If McModificationStart(ReadInstallRequest(Value("request")), New McInstance(CStr(Value("version")))) Then
                        Loader = LoaderTaskbar.ToList().FirstOrDefault(Function(Entry) Not Before.Contains(Entry))
                    End If
                Case "resource-pack"
                    Loader = RestartResourcePack(Item)
                Case "mod-update"
                    Loader = RestartModUpdate(Item)
                Case Else
                    Throw New InvalidOperationException("请返回原操作重新执行此任务。")
            End Select
        End Using
        If Loader Is Nothing Then Throw New IOException("未能恢复任务，请查看日志。")
        If Token.IsCancellationRequested Then
            Loader.Cancel()
            Token.ThrowIfCancellationRequested()
        End If
        Loader.RecoveryRecipe = Value
        Track(Loader, Item.Id)
    End Sub

    Private Function IsVersionPath(Target As String) As Boolean
        If String.IsNullOrWhiteSpace(Target) OrElse Not Directory.Exists(Target) Then Return False
        Return File.Exists(Path.Combine(Target, Path.GetFileName(Target.TrimEnd("\"c)) & ".json"))
    End Function

    Private Function ResolvePackVersion(Target As String, ArchivePath As String) As String
        If IsVersionPath(Target) Then Return Target
        If String.IsNullOrEmpty(Target) OrElse Not Directory.Exists(Target) OrElse Not File.Exists(ArchivePath) Then Return Nothing
        Using Archive = FileUtils.OpenZip(ArchivePath)
            For Each Entry In Archive.Entries
                If Not Regex.IsMatch(Entry.FullName, "(?:^|/)versions/([^/]+)/\1\.json$") Then Continue For
                Dim Candidate = Path.Combine(Target, Path.GetDirectoryName(Entry.FullName.Replace("/"c, "\"c)))
                If PclDataImport.ContainsPath(Target, Candidate) AndAlso IsVersionPath(Candidate) Then Return Candidate
            Next
        End Using
        Return Nothing
    End Function

    Private Sub PreparePackRetry(Item As TaskRecord, PreviousError As String)
        Dim Type = CStr(Item.Recipe("type")), Archive = CStr(Item.Recipe("archive"))
        If Not {"guide-pack", "resource-pack"}.Contains(Type) OrElse Not File.Exists(Archive) Then Return
        Dim Invalid = PreviousError.Contains("InvalidDataException") OrElse PreviousError.Contains("损坏")
        Try
            Using Zip = FileUtils.OpenZip(Archive)
                If Zip.Entries.Count = 0 Then Invalid = True
            End Using
        Catch ex As InvalidDataException
            Invalid = True
        End Try
        If Not Invalid Then Return
        Dim Owned = PclDataImport.ContainsPath(DataFolder & "Packages", Archive) OrElse
            (Type = "resource-pack" AndAlso CBool(If(Item.Recipe("temporary"), False)) AndAlso Path.GetFileNameWithoutExtension(Archive) = "原始整合包")
        If Not Owned Then Return
        File.Delete(Archive)
        Dim Cache As DownloadCheckpoint = Nothing
        If Not Checkpoints.TryRemove(Archive, Cache) Then Cache = DownloadCheckpoint.Open(CacheFolder, Archive)
        Cache.Delete()
    End Sub

    Friend Function ReadInstallRequest(Value As JToken) As McInstallRequest
        Dim Copy = JObject.Parse(Value.ToString())
        Dim Components = TryCast(Copy("Components"), JObject)
        If Components Is Nothing Then Throw New IOException("安装任务缺少组件信息，请重新选择游戏版本。")
        Copy.Remove("Components")
        Dim Forge = Copy("ForgeEntry"), NeoForge = Copy("NeoForgeEntry")
        Copy.Remove("ForgeEntry") : Copy.Remove("NeoForgeEntry")
        Dim Request = Copy.ToObject(Of McInstallRequest)()
        If Forge IsNot Nothing AndAlso Forge.Type <> JTokenType.Null Then Request.ForgeEntry = CType(ReadForgelike(Forge, False), DlForgeVersionEntry)
        If NeoForge IsNot Nothing AndAlso NeoForge.Type <> JTokenType.Null Then Request.NeoForgeEntry = CType(ReadForgelike(NeoForge, True), DlNeoForgeListEntry)
        Request.Components = New McComponents(CStr(Components("MinecraftVersion")),
            Components("ModLoaderVersions").ToObject(Of Dictionary(Of ModLoaders, String))(),
            TryCast(Components("OptiFineEntry"), JObject), CStr(Components("FabricApiVersion")), CStr(Components("OptiFabricVersion")))
        Return Request
    End Function

    Private Function RestartResourcePack(Item As TaskRecord) As LoaderCombo(Of String)
        Dim Value = Item.Recipe
        Dim Archive = CStr(Value("archive"))
        Dim Download = Item.Downloads.FirstOrDefault(Function(Entry) PathUtils.IsSame(Entry.Path, Archive))
        If Download Is Nothing Then Throw New IOException("未找到整合包的下载记录，请重新选择该整合包。")
        McFolderSelected = CStr(Value("folder"))
        Dim Loader As New LoaderCombo(Of String)(Item.Name, {
            New LoaderDownload("下载整合包文件", {New NetFile(Download.Urls, Archive, Download.Checker(), Download.BrowserHeaders)}) With {.ProgressWeight = 10},
            New LoaderTask(Of Integer, Integer)("安装整合包", Sub(Task)
                Dim Installation As LoaderCombo(Of String)
                Using Suppress(Task.RealParent)
                    Installation = ModpackInstall(Archive, Automatic:=True)
                End Using
                WaitForInstallation(Task, Installation)
            End Sub) With {.ProgressWeight = 8}
        })
        Loader.RecoveryId = Item.Id
        Loader.RecoveryRecipe = Value
        Loader.Start()
        LoaderTaskbarAdd(Loader)
        Return Loader
    End Function

    Friend Function ReadForgelike(Value As JToken, Neo As Boolean) As DlForgelikeEntry
        Dim Copy = JObject.Parse(Value.ToString())
        Dim Entry As DlForgelikeEntry
        If Neo Then
            Entry = New DlNeoForgeListEntry(CStr(Copy("ApiName")))
        Else
            Dim Name = CStr(Copy("VersionName")), FileVersion = CStr(Copy("FileVersion"))
            Dim Branch = If(FileVersion IsNot Nothing AndAlso FileVersion.Length > Name.Length, FileVersion.Substring(Name.Length + 1), Nothing)
            Entry = New DlForgeVersionEntry(Name, Branch, CStr(Copy("Inherit")))
        End If
        Copy.Remove("Version")
        Newtonsoft.Json.JsonSerializer.CreateDefault().Populate(Copy.CreateReader(), Entry)
        Return Entry
    End Function

    Private Function RestartSavedDownload(Value As JObject) As LoaderBase
        Dim Before = LoaderTaskbar.ToList()
        Dim Target = CStr(Value("target"))
        Select Case CStr(Value("action"))
            Case "optifine" : McDownloadOptiFineSave(Value("info").ToObject(Of DlOptiFineListEntry)(), Target)
            Case "liteloader" : McDownloadLiteLoaderSave(Value("info").ToObject(Of DlLiteLoaderListEntry)(), Target)
            Case "forgelike"
                Dim Info = ReadForgelike(Value("info"), CBool(Value("neo")))
                McDownloadForgelikeSave(Info, Target)
            Case "fabric" : McDownloadFabricLoaderSave(CType(Value("info"), JObject), Target)
            Case "mc-server", "mc-jar"
                Dim Entry As New MyListItem With {.Title = CStr(Value("info")("id")), .Tag = Value("info")}
                If CStr(Value("action")) = "mc-server" Then
                    McDownloadMenuSaveServerCore(Entry, Nothing, Target)
                Else
                    McDownloadMenuSaveCore(Entry, Nothing, Target)
                End If
        End Select
        Return LoaderTaskbar.ToList().FirstOrDefault(Function(Entry) Not Before.Contains(Entry))
    End Function

    Public Sub WaitForInstallation(Task As LoaderTask(Of Integer, Integer), Installation As LoaderCombo(Of String))
        If Installation Is Nothing Then Throw New IOException("未能开始整合包安装")
        Try
            While Installation.State = LoadState.Loading OrElse Installation.State = LoadState.Waiting
                Task.CreateCancellationToken().ThrowIfCancellationRequested()
                Task.Progress = Installation.Progress
                Thread.Sleep(30)
            End While
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            If Installation.State = LoadState.Canceled Then Throw New OperationCanceledException()
            If Installation.State <> LoadState.Finished Then Throw New IOException("整合包安装失败", Installation.Error)
            Dim Parent = Task.RealParent
            If Parent?.RecoveryRecipe IsNot Nothing Then
                Dim Value = Parent.RecoveryRecipe
                Value("version") = Installation.Input
                Recipe(Parent, Value)
            End If
        Catch ex As OperationCanceledException
            Installation.Cancel()
            Throw
        End Try
    End Sub

    Public Function FileHash(Target As String) As String
        Using Stream = File.OpenRead(Target), Hash = System.Security.Cryptography.SHA256.Create()
            Return BitConverter.ToString(Hash.ComputeHash(Stream)).Replace("-", "")
        End Using
    End Function

    Private Function RestartModUpdate(Item As TaskRecord) As LoaderCombo(Of String)
        Dim Value = Item.Recipe
        Dim Copies = Value("copies").ToObject(Of Dictionary(Of String, String))()
        Dim Originals = Value("originals").ToObject(Of Dictionary(Of String, String))()
        Dim Mods = CStr(Value("mods"))
        If PageInstanceMod.UpdatingInstanceModFolders.Contains(Mods) Then Throw New IOException("此版本的 Mod 正在更新")
        Dim Files = Item.Downloads.Select(Function(Download) New NetFile(Download.Urls, Download.Path, Download.Checker(), Download.BrowserHeaders)).ToList()
        Dim Loader As New LoaderCombo(Of String)(Item.Name, {
            New LoaderDownload("下载新版 Mod 文件", Files) With {.ProgressWeight = 8},
            New LoaderTask(Of String, Integer)("替换 Mod 文件", Sub(Task)
                Task.CreateCancellationToken().ThrowIfCancellationRequested()
                If Not Directory.Exists(Mods) Then Throw New DirectoryNotFoundException(Mods)
                For Each Original In Originals
                    If File.Exists(Original.Key) AndAlso FileHash(Original.Key) <> Original.Value Then
                        Dim AlreadyApplied = Copies.Any(Function(Copy) PathUtils.IsSame(Copy.Value, Original.Key) AndAlso File.Exists(Copy.Key) AndAlso FileHash(Copy.Key) = FileHash(Original.Key))
                        If Not AlreadyApplied Then Throw New IOException("原 Mod 在退出后发生变化，请重新检查更新：" & Original.Key)
                    End If
                Next
                For Each Copy In Copies
                    If Not File.Exists(Copy.Key) Then Throw New FileNotFoundException("新版 Mod 文件不存在", Copy.Key)
                    If File.Exists(Copy.Value) AndAlso Not Originals.ContainsKey(Copy.Value) AndAlso FileHash(Copy.Value) <> FileHash(Copy.Key) Then
                        Throw New IOException("目标文件已存在，请检查后重试：" & Copy.Value)
                    End If
                Next
                Dim Backup = DataFolder & "Backups\" & Item.Id & "\"
                Directory.CreateDirectory(Backup)
                ' Keep the old files until every replacement has been copied successfully.
                For Each Original In Originals
                    If File.Exists(Original.Key) Then File.Copy(Original.Key, Backup & Path.GetFileName(Original.Key), True)
                Next
                For Each Copy In Copies
                    Directory.CreateDirectory(Path.GetDirectoryName(Copy.Value))
                    File.Copy(Copy.Key, Copy.Value, True)
                Next
                For Each Original In Originals
                    If Not Copies.Values.Any(Function(Target) PathUtils.IsSame(Target, Original.Key)) AndAlso File.Exists(Original.Key) Then File.Delete(Original.Key)
                Next
            End Sub) With {.ProgressWeight = 2}
        })
        PageInstanceMod.UpdatingInstanceModFolders.Add(Mods)
        AddHandler Loader.OnStateChangedUi, Sub(Task, State, Previous)
            If State = LoadState.Finished OrElse State = LoadState.Canceled OrElse State = LoadState.Failed Then PageInstanceMod.UpdatingInstanceModFolders.Remove(Mods)
        End Sub
        Loader.Start()
        LoaderTaskbarAdd(Loader)
        Return Loader
    End Function

    Public Sub PreserveBeforeExit()
        SyncLock Gate
            PreserveCore()
        End SyncLock
    End Sub
    Private Sub PreserveCore()
        If Exiting Then Return
        Initialize()
        Tick()
        Exiting = True
        For Each Item In Journal.Snapshot().Where(Function(Entry) Entry.State = "running")
            Item.State = "interrupted" : Journal.Put(Item)
        Next
        For Each Cache As DownloadCheckpoint In Checkpoints.Values
            Cache.Flush()
        Next
        Save()
        For Each Token In Restoring.Values
            Token.Cancel()
        Next
        Tray?.Dispose()
    End Sub

    Private Sub Cleanup(Item As TaskRecord)
        Try
            For Each Download In Item.Downloads
                If Journal.Snapshot().Any(Function(Other) Other.Id <> Item.Id AndAlso (Other.State = "running" OrElse Other.NeedsAttention) AndAlso
                    Other.Downloads.Any(Function(File) PathUtils.IsSame(File.Path, Download.Path))) Then Continue For
                Dim Cache As DownloadCheckpoint = Nothing
                If Not Checkpoints.TryRemove(Download.Path, Cache) Then Cache = DownloadCheckpoint.Open(CacheFolder, Download.Path)
                Cache.Delete()
            Next
            Dim Archive = CStr(Item.Recipe("archive"))
            If Not String.IsNullOrEmpty(Archive) AndAlso PclDataImport.ContainsPath(DataFolder & "Packages", Archive) AndAlso File.Exists(Archive) Then File.Delete(Archive)
            If CBool(If(Item.Recipe("temporary"), False)) AndAlso Not String.IsNullOrEmpty(Archive) AndAlso
                Path.GetFileNameWithoutExtension(Archive) = "原始整合包" AndAlso File.Exists(Archive) Then File.Delete(Archive)
            If Item.State = "succeeded" AndAlso CStr(Item.Recipe("type")) = "mod-update" Then
                Dim Backup = DataFolder & "Backups\" & Item.Id
                If Directory.Exists(Backup) AndAlso PclDataImport.ContainsPath(DataFolder & "Backups", Backup) Then Directory.Delete(Backup, True)
            End If
            If Item.State = "canceled" AndAlso CStr(Item.Recipe("type")) = "pcl-import" Then
                For Each Entry In TryCast(Item.Recipe("keys"), JObject).Properties()
                    Dim Key As Guid
                    If Not Guid.TryParseExact(CStr(Entry.Value), "N", Key) Then Continue For
                    Dim Staging = Path.Combine(Paths.Base, "ImportedGames", ".import-" & Key.ToString("N"))
                    If Directory.Exists(Staging) AndAlso PclDataImport.ContainsPath(Paths.Base & "ImportedGames", Staging) Then Directory.Delete(Staging, True)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "清理任务临时文件失败")
        End Try
    End Sub

    Public Sub ShowDiagnostics(Item As TaskRecord)
        Dim Text = Item.Name & vbCrLf & TaskJournal.StateText(Item.State) & " · " & Item.Stage & vbCrLf & vbCrLf & Item.Error
        MyMsgBox(Text, "任务诊断", "知道了", "导出日志", "打开日志目录",
            Button2Action:=Sub() ExportLog(Item), Button3Action:=Sub() OpenExplorer(Paths.Base & "PCL\"))
    End Sub
    Public Sub ExportLog(Item As TaskRecord)
        Dim Target = Dialogs.SaveFile("导出任务日志", "PCL-任务日志.txt", filter:={("txt", "文本文件")})
        If Target Is Nothing Then Return
        Dim Text = Item.Name & vbCrLf & TaskJournal.StateText(Item.State) & " · " & Item.Stage & vbCrLf & Item.Error & vbCrLf
        Dim LogFile = Paths.Base & "PCL\Log1.txt"
        If File.Exists(LogFile) Then Text &= vbCrLf & File.ReadAllText(LogFile)
        File.WriteAllText(Target, Text, New UTF8Encoding(False))
        Hint("任务日志已导出", HintType.Green)
    End Sub

    Public Sub KeepInBackground()
        If Tray Is Nothing Then
            Tray = New System.Windows.Forms.NotifyIcon With {.Text = "PCL for CAUMC · 后台任务", .Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.GetCommandLineArgs()(0)), .Visible = True}
            Dim Menu As New System.Windows.Forms.ContextMenuStrip()
            Menu.Items.Add("打开启动器", Nothing, Sub() RestoreWindow())
            Menu.Items.Add("退出", Nothing, Sub() RunInUi(Sub()
                RestoreWindow()
                FrmMain.EndProgram(True)
            End Sub))
            Tray.ContextMenuStrip = Menu
            AddHandler Tray.DoubleClick, Sub() RestoreWindow()
        End If
        FrmMain.Hide()
    End Sub
    Private Sub RestoreWindow()
        RunInUi(Sub()
                    FrmMain.Show() : FrmMain.WindowState = WindowState.Normal : FrmMain.Activate()
                    Tray?.Dispose() : Tray = Nothing
                End Sub)
    End Sub
End Module
