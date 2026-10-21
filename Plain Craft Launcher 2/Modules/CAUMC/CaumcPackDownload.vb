Imports PCLCS.CAUMC

Public Module CaumcPackDownload
    Private ReadOnly Active As New ConcurrentDictionary(Of String, LoaderCombo(Of String))()

    Public Function DownloadGuidePack(Address As Uri) As LoaderCombo(Of String)
        If Not PclDataImport.IsPackLink(5, Address) Then Return Nothing
        Dim Folder = McFolderSelected
        Dim Key = Folder & "|" & Address.AbsoluteUri
        If Active.ContainsKey(Key) Then
            Hint("这个整合包正在下载或导入，可在下载任务中查看进度", HintType.Blue)
            Dim Existing As LoaderCombo(Of String) = Nothing
            Active.TryGetValue(Key, Existing)
            Return Existing
        End If
        Dim FileName = Path.GetFileName(Uri.UnescapeDataString(Address.AbsolutePath))
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
