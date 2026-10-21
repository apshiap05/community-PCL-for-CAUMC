Imports PCLCS.CAUMC

Public Module CaumcPackDownload
    Private ReadOnly Active As New ConcurrentDictionary(Of String, LoaderCombo(Of String))()
    Private ReadOnly ImportedLock As New Object()
    Private StartupStarted As Integer
    Friend Property StartupCheck As System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask

    Friend Function GuidePackImported(Address As Uri) As Boolean
        SyncLock ImportedLock
            Return ReadImportedPacks().Contains(GuidePackUpdates.Key(Address))
        End SyncLock
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
            AddressOf DownloadUpdateAsync, AddressOf MarkGuidePackImported).ConfigureAwait(False)
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
                    Dim Installation = ModpackInstall(DownloadPath, Automatic:=True)
                    Try
                        Do While Installation.State = LoadState.Loading OrElse Installation.State = LoadState.Waiting
                            Task.CreateCancellationToken().ThrowIfCancellationRequested()
                            Task.Progress = Installation.Progress
                            Thread.Sleep(30)
                        Loop
                        Task.CreateCancellationToken().ThrowIfCancellationRequested()
                        If Installation.State = LoadState.Canceled Then Throw New OperationCanceledException()
                        If Installation.State <> LoadState.Finished Then Throw New IOException("整合包导入失败", Installation.Error)
                        MarkGuidePackImported(Address)
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
