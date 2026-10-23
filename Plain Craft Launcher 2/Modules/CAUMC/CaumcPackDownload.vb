Imports PCLCS.CAUMC

Public Module CaumcPackDownload
    Private ReadOnly Active As New ConcurrentDictionary(Of String, LoaderCombo(Of String))()
    Private ReadOnly ImportedLock As New Object()
    Private StartupStarted As Integer
    Friend Property StartupCheck As System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask
    Friend Property ExistingVersionPicker As Func(Of IReadOnlyList(Of String), String) = AddressOf PickExistingVersion

    Friend Function GuidePackImported(Address As Uri) As Boolean
        SyncLock ImportedLock
            Dim Binding = TryCast(ReadBindings()(GuidePackUpdates.Key(Address)), JObject)
            If Binding IsNot Nothing Then Return VersionExists(ExpandVersionPath(CStr(Binding("path"))))
            Return ReadImportedPacks().Contains(GuidePackUpdates.Key(Address))
        End SyncLock
    End Function

    Private Function ReadBindings() As JObject
        Try
            Return JObject.Parse(Settings.Get(Of String)("CaumcGuidePackBindings"))
        Catch ex As Exception
            Logger.Warn(ex, "读取模组服版本关联失败")
            Return New JObject()
        End Try
    End Function

    Private Function ExpandVersionPath(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return ""
        Return If(Value.StartsWith("$"), Paths.Base & Value.Substring(1), Value)
    End Function

    Private Function VersionExists(VersionPath As String) As Boolean
        If String.IsNullOrWhiteSpace(VersionPath) Then Return False
        Dim Name = Path.GetFileName(VersionPath.TrimEnd("\"c, "/"c))
        Return File.Exists(Path.Combine(VersionPath, Name & ".json"))
    End Function

    Private Function CompactVersionPath(VersionPath As String) As String
        Dim FullPath = Path.GetFullPath(VersionPath).TrimEnd("\"c)
        Return If(PclDataImport.ContainsPath(Paths.Base, FullPath), "$" & FullPath.Substring(Paths.Base.Length), FullPath)
    End Function

    Private Sub RememberBinding(Address As Uri, VersionPath As String, Signature As GuidePackSignature)
        If Not VersionExists(VersionPath) Then Throw New IOException("安装完成后未找到游戏版本，请查看下载任务。")
        Dim StoredPath = CompactVersionPath(VersionPath)
        SyncLock ImportedLock
            Dim Bindings = ReadBindings()
            Bindings(GuidePackUpdates.Key(Address)) = New JObject From {
                {"path", StoredPath}, {"fingerprint", If(Signature?.Fingerprint, "")}}
            Settings.Set("CaumcGuidePackBindings", Bindings.ToString(Newtonsoft.Json.Formatting.None))
            MarkGuidePackImported(Address)
        End SyncLock
    End Sub

    Friend Sub SelectGuidePack(Address As Uri)
        Dim VersionPath As String
        SyncLock ImportedLock
            VersionPath = ExpandVersionPath(CStr(TryCast(ReadBindings()(GuidePackUpdates.Key(Address)), JObject)?("path")))
        End SyncLock
        If Not VersionExists(VersionPath) Then Return
        Dim GameFolder = Directory.GetParent(Directory.GetParent(VersionPath).FullName).FullName & "\"
        RunInUiWait(Sub()
                        McFolderSelected = GameFolder
                        McInstanceSelected = New McInstance(VersionPath)
                        McFolderListLoader.Start(IsForceRestart:=True)
                        McInstanceListLoader.Start(GameFolder, IsForceRestart:=True)
                        Hint("整合包已就绪 · " & McInstanceSelected.Name & "，点击启动游戏即可", HintType.Green)
                    End Sub)
    End Sub

    Private Function KnownGameFolders() As IEnumerable(Of String)
        Dim Folders As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {CaumcGameDirectory, McFolderSelected}
        For Each Folder In McFolderList.ToArray()
            Folders.Add(Folder.Location)
        Next
        For Each Entry In Settings.Get(Of String)("LaunchFolders").Split("|"c)
            If Entry.Contains(">") Then Folders.Add(Entry.AfterFirst(">").Replace("$", Paths.Base))
        Next
        Return Folders.Where(Function(Folder) Path.IsPathRooted(Folder) AndAlso Directory.Exists(Folder)).ToList()
    End Function

    Private Function FindExisting(Signature As GuidePackSignature, Token As CancellationToken) As String
        If Signature Is Nothing Then Return Nothing
        Dim Candidates As New List(Of String)()
        Dim DefaultIsolation = Settings.Get(Of Integer)("LaunchArgumentIndieV2")
        For Each Folder In KnownGameFolders()
            Token.ThrowIfCancellationRequested()
            Dim VersionPath = Path.Combine(Folder, "versions", Signature.VersionName)
            Dim DefaultIsolated = DefaultIsolation = 1 OrElse DefaultIsolation = 3 OrElse DefaultIsolation = 4
            If DefaultIsolation = 2 AndAlso VersionExists(VersionPath) Then
                Try
                    Dim Json = JObject.Parse(File.ReadAllText(Path.Combine(VersionPath, Signature.VersionName & ".json")))
                    DefaultIsolated = CStr(Json("type")) <> "release"
                Catch ex As Exception
                    Continue For
                End Try
            End If
            If GuidePackIdentity.Matches(Signature, VersionPath, Folder, DefaultIsolated, Token) AndAlso
                Not Candidates.Any(Function(Item) PathUtils.IsSame(Item, VersionPath)) Then Candidates.Add(VersionPath)
        Next
        If Candidates.Count = 0 Then Return Nothing
        If Candidates.Count = 1 Then Return Candidates(0)
        Dim Choices As JObject
        SyncLock ImportedLock
            Try
                Choices = JObject.Parse(Settings.Get(Of String)("CaumcGuidePackChoices"))
            Catch ex As Newtonsoft.Json.JsonException
                Choices = New JObject()
            End Try
        End SyncLock
        Dim SavedPath = ExpandVersionPath(CStr(Choices(Signature.Fingerprint)))
        Dim Saved = Candidates.FirstOrDefault(Function(Item) PathUtils.IsSame(Item, SavedPath))
        If Saved IsNot Nothing Then Return Saved
        Dim Choice = ExistingVersionPicker(Candidates)
        If Choice Is Nothing OrElse Not Candidates.Contains(Choice) Then Throw New OperationCanceledException()
        Token.ThrowIfCancellationRequested()
        SyncLock ImportedLock
            Choices(Signature.Fingerprint) = CompactVersionPath(Choice)
            Settings.Set("CaumcGuidePackChoices", Choices.ToString(Newtonsoft.Json.Formatting.None))
        End SyncLock
        Return Choice
    End Function

    Private Function PickExistingVersion(Candidates As IReadOnlyList(Of String)) As String
        Dim Choice As Integer? = Nothing
        RunInUiWait(Sub()
                        Dim Options As New List(Of IMyRadio)()
                        For Each Item In Candidates
                            Dim Radio As New MyRadioBox With {.Text = Path.GetFileName(Item) & vbCrLf & Directory.GetParent(Directory.GetParent(Item).FullName).FullName,
                                .ToolTip = Item, .Margin = New Thickness(0, 0, 0, 8)}
                            Radio.LabText.TextWrapping = TextWrapping.Wrap
                            Radio.LabText.MaxWidth = 420
                            Options.Add(Radio)
                        Next
                        Choice = MyMsgBoxSelect(Options, "选择已有的模组服版本", "使用此版本", "取消")
                    End Sub)
        Return If(Choice.HasValue, Candidates(Choice.GetValueOrDefault()), Nothing)
    End Function

    Private Function ReadImportedPacks() As HashSet(Of String)
        Try
            Return New HashSet(Of String)(JArray.Parse(Settings.Get(Of String)("CaumcImportedGuidePacks")).Values(Of String)(), StringComparer.Ordinal)
        Catch ex As Exception
            Logger.Warn(ex, "读取已导入的模组服整合包记录失败")
            Return New HashSet(Of String)(StringComparer.Ordinal)
        End Try
    End Function

    Friend Sub MarkGuidePackImported(Address As Uri)
        SyncLock ImportedLock
            Dim Imported = ReadImportedPacks()
            If Imported.Add(GuidePackUpdates.Key(Address)) Then Settings.Set("CaumcImportedGuidePacks", New JArray(Imported.OrderBy(Function(Key) Key)).ToString(Newtonsoft.Json.Formatting.None))
        End SyncLock
    End Sub

    Public Sub CheckGuidePacksAtStartup(OldImport As LoaderCombo(Of String))
        If Interlocked.Exchange(StartupStarted, 1) <> 0 Then Return
        StartupCheck = System.Threading.Tasks.Task.Run(
            Async Function()
                Try
                    Using Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(15)}
                        Await CheckGuidePacksAsync(OldImport, Http,
                            Function(Update) System.Threading.Tasks.Task.FromResult(MyMsgBox(
                                "检索到 CAUMC Mod 服有新整合包 " & Update.FileName & " 发布，是否自动下载并导入？",
                                "模组服整合包更新", "是，导入", "否，暂不", ForceWait:=True) = 1)).ConfigureAwait(False)
                    End Using
                Catch ex As Exception
                    Logger.Warn(ex, "启动时检查模组服整合包失败，下次启动将重试")
                End Try
            End Function)
    End Sub

    Friend Async Function CheckGuidePacksAsync(OldImport As LoaderCombo(Of String), Http As HttpClient,
        Ask As Func(Of GuidePackUpdate, System.Threading.Tasks.Task(Of Boolean))) As System.Threading.Tasks.Task
        ' The old import can change the selected game folder; let it settle first.
        If OldImport IsNot Nothing Then Await WaitForPackTask(OldImport).ConfigureAwait(False)
        Dim Guide = GuideCatalog.ForPost(5)
        Dim Service As New ArticleClient(Http, Paths.Base & "PCL\Cache\pack-update-article.json", Guide.Source)
        Dim Snapshot = Await Service.RefreshAsync().ConfigureAwait(False)
        Await GuidePackUpdates.CheckAsync(Snapshot, Http, AddressOf GuidePackImported, Ask,
            AddressOf DownloadUpdateAsync, AddressOf MarkGuidePackImported, selectPreferred:=AddressOf SelectGuidePack).ConfigureAwait(False)
    End Function

    Private Async Function DownloadUpdateAsync(Update As GuidePackUpdate) As System.Threading.Tasks.Task(Of Boolean)
        Try
            Dim Loader As LoaderCombo(Of String) = Nothing
            RunInUiWait(Sub() Loader = DownloadGuidePack(Update.Address, Update.FileName))
            If Loader Is Nothing Then Return False
            Return Await WaitForPackTask(Loader).ConfigureAwait(False)
        Catch ex As Exception
            Logger.Warn(ex, "自动导入模组服整合包失败，下次启动将重试")
            Return False
        End Try
    End Function

    Private Async Function WaitForPackTask(Loader As LoaderCombo(Of String)) As System.Threading.Tasks.Task(Of Boolean)
        While Loader.State = LoadState.Waiting OrElse Loader.State = LoadState.Loading
            Await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(False)
        End While
        Return Loader.State = LoadState.Finished
    End Function

    Public Function DownloadGuidePack(Address As Uri, Optional DisplayName As String = Nothing) As LoaderCombo(Of String)
        If Not PclDataImport.IsPackLink(5, Address) Then Return Nothing
        Dim Folder = McFolderSelected
        Dim Key = Folder & "|" & GuidePackUpdates.Key(Address)
        If Active.ContainsKey(Key) Then
            Hint("这个整合包正在下载或导入，可在下载任务中查看进度", HintType.Blue)
            Dim Existing As LoaderCombo(Of String) = Nothing
            Active.TryGetValue(Key, Existing)
            Return Existing
        End If
        Dim FileName = If(DisplayName, Path.GetFileName(Uri.UnescapeDataString(Address.AbsolutePath)))
        Dim DownloadPath = RequestTaskTempFolder() & "modpack.zip"
        Dim Loader As New LoaderCombo(Of String)("下载并导入 " & FileName, {
            New LoaderDownload("下载整合包", New List(Of NetFile) From {
                New NetFile(New String() {Address.AbsoluteUri}, DownloadPath, New FileChecker With {.MinSize = 22})
            }) With {.ProgressWeight = 8},
            New LoaderTask(Of String, Integer)("自动导入整合包",
                Sub(Task As LoaderTask(Of String, Integer))
                    Task.CreateCancellationToken().ThrowIfCancellationRequested()
                    If Not PathUtils.IsSame(McFolderSelected, Folder) Then Throw New IOException("游戏下载目录已改变，请重新点击整合包链接。")
                    Dim Signature As GuidePackSignature
                    Using Archive = FileUtils.OpenZip(DownloadPath)
                        Signature = GuidePackIdentity.Read(Archive, Task.CreateCancellationToken())
                    End Using
                    Dim Existing = FindExisting(Signature, Task.CreateCancellationToken())
                    If Existing IsNot Nothing Then
                        Task.CreateCancellationToken().ThrowIfCancellationRequested()
                        RememberBinding(Address, Existing, Signature)
                        SelectGuidePack(Address)
                        Return
                    End If
                    Dim Installation = ModpackInstall(DownloadPath, Automatic:=True, PreferredVersion:=Signature?.VersionName)
                    Try
                        Do While Installation.State = LoadState.Loading OrElse Installation.State = LoadState.Waiting
                            Task.CreateCancellationToken().ThrowIfCancellationRequested()
                            Task.Progress = Installation.Progress
                            Thread.Sleep(30)
                        Loop
                        Task.CreateCancellationToken().ThrowIfCancellationRequested()
                        If Installation.State = LoadState.Canceled Then Throw New OperationCanceledException()
                        If Installation.State <> LoadState.Finished Then Throw New IOException("整合包导入失败", Installation.Error)
                        Dim InstalledPath = Installation.Input
                        If Signature IsNot Nothing AndAlso Not VersionExists(InstalledPath) Then InstalledPath = Path.Combine(Installation.Input, Signature.ArchiveRoot.Replace("/"c, "\"c), "versions", Signature.VersionName)
                        If Signature Is Nothing AndAlso Not VersionExists(InstalledPath) AndAlso PclDataImport.ContainsPath(Installation.Input, McFolderSelected) Then
                            InstalledPath = Path.Combine(McFolderSelected, "versions", ReadIni(McFolderSelected & "PCL.ini", "Version"))
                        End If
                        RememberBinding(Address, InstalledPath, Signature)
                        SelectGuidePack(Address)
                    Catch ex As OperationCanceledException
                        Installation.Cancel()
                        Throw
                    End Try
                End Sub) With {.ProgressWeight = 12}
        })
        Loader.OnStateChanged = Sub(Item)
                                    If Item.State = LoadState.Loading Then Return
                                    Dim Removed As LoaderCombo(Of String) = Nothing
                                    Active.TryRemove(Key, Removed)
                                    CaumcTaskState(Item)
                                End Sub
        If Not Active.TryAdd(Key, Loader) Then Return Nothing
        Loader.Start(Address.AbsoluteUri)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Hint("开始下载整合包，下载完成后自动导入", HintType.Blue)
        Return Loader
    End Function
End Module
