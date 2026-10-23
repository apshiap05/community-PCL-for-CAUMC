Imports PCLCS.CAUMC

Public Module CaumcMigration
    Public Sub CaumcMigrateConfiguration()
        If Environment.GetCommandLineArgs().Contains("--caumc-selftest") Then Return
        Dim OldData = Paths.Base & "PCLForCAUMC\"
        Dim Data = Paths.Base & "PCL\"
        Directory.CreateDirectory(Data)
        If ReadIni("CAUMC", "CaumcLayoutMigrated", "False") = "True" Then Return
        PclDataImport.MergeIni(OldData & "Setup.ini", Data & "Setup.ini")
        PclDataImport.MergeIni(OldData & "Setup.ini", Data & "CAUMC.ini")
        IniClearCache("Setup")
        IniClearCache("CAUMC")
        ' Old CAU sessions remain in their endpoint-scoped DPAPI store. Only
        ' missing shared settings are migrated; existing PCL values win.
        For Each Entry In Settings.Entries.Values.Where(Function(Item) Item.Source = Settings.Sources.Registry AndAlso Not CaumcIsAuthSetting(Item.Key))
            Dim Destination = CaumcRegistryPath(Entry.Key)
            If RegistryUtils.Has(Destination, Entry.Key) OrElse Not RegistryUtils.Has("HKEY_CURRENT_USER\Software\PCLForCAUMC", Entry.Key) Then Continue For
            Dim Value = CStr(RegistryUtils.TryRead("HKEY_CURRENT_USER\Software\PCLForCAUMC", Entry.Key, ""))
            Try
                If Entry.Encrypted Then Value = Settings.EncodeStoredValue(Entry.Key, Settings.DecodeStoredValue(Entry.Key, Value))
                RegistryUtils.Write(Destination, Entry.Key, Value)
            Catch ex As Exception
                Logger.Warn(ex, "未能迁移旧设置：" & Entry.Key)
            End Try
        Next
        WriteIni("CAUMC", "CaumcLayoutMigrated", "True")
        For Each Entry In Settings.Entries.Values
            Entry.ValueCache.Clear()
        Next
    End Sub

    Public Sub CaumcRegisterGameFolder(Folder As String, Name As String)
        Folder = PathUtils.AddSlashSuffix(Path.GetFullPath(Folder))
        Dim Folders = Settings.Get(Of String)("LaunchFolders").Split("|"c, True).ToList()
        If Not Folders.Any(Function(Item) Item.Contains(">") AndAlso PathUtils.IsSame(Item.AfterFirst(">"), Folder)) Then
            Folders.Add(Name.Replace(">", "").Replace("|", "") & ">" & Folder)
            Settings.Set("LaunchFolders", String.Join("|", Folders))
        End If
    End Sub

    Public Function CaumcOfferPclImport(Optional Always As Boolean = False) As LoaderCombo(Of String)
        If Not Always AndAlso Settings.Get(Of Boolean)("CaumcPclImportOffered") Then Return Nothing
        If HasDownloadingTask() OrElse McInstanceLock.IsAnyLocked Then
            Hint("请在当前下载或安装任务完成后再导入旧 PCL", HintType.Blue)
            Return Nothing
        End If
        Settings.Set("CaumcPclImportOffered", True)
        If MyMsgBox("是否导入旧 PCL 中的所有游戏？" & vbCrLf & "选择原启动器所在文件夹后，将复制游戏版本、模组、存档和游戏配置，原文件会保留。",
            "导入旧 PCL 游戏", "确认，开始导入", "取消，不导入", ForceWait:=True) <> 1 Then Return Nothing
        Dim Selected = Dialogs.SelectFolder("选择旧 PCL 启动器所在文件夹", False).FirstOrDefault()
        If String.IsNullOrWhiteSpace(Selected) Then Return Nothing
        Try
            Dim Sources = PclDataImport.Discover(Selected).ToList()
            If Sources.Count = 0 Then Throw New IOException("该目录中没有找到含 versions 的游戏文件夹。请选择旧 PCL 所在目录或 .minecraft 目录。")
            Dim Registered = CStr(RegistryUtils.TryRead(If(Environment.GetCommandLineArgs().Contains("--caumc-selftest"), CaumcRegistryPath("LaunchFolders"), "HKEY_CURRENT_USER\Software\PCL"), "LaunchFolders", ""))
            For Each Entry In Registered.Split("|"c, True)
                If Not Entry.Contains(">") Then Continue For
                Dim Folder = Entry.AfterFirst(">")
                If Not Path.IsPathRooted(Folder) OrElse Not Directory.Exists(Path.Combine(Folder, "versions")) Then Continue For
                If PclDataImport.ContainsPath(Paths.Base, Folder) Then Continue For
                If Not Sources.Any(Function(Item) PathUtils.IsSame(Item, Folder)) Then Sources.Add(Folder)
            Next
            Return CaumcImportSources(Sources)
        Catch ex As Exception
            Logger.Error(ex, "导入旧 PCL 游戏失败", LogBehavior.Alert)
        End Try
        Return Nothing
    End Function

    Public Function CaumcImportSources(Sources As List(Of String), Optional Recovery As JObject = Nothing) As LoaderCombo(Of String)
            Dim Value = If(Recovery, New JObject From {{"type", "pcl-import"}, {"sources", New JArray(Sources)}, {"completed", New JObject()}, {"keys", New JObject()}})
            If Value("completed") Is Nothing Then Value("completed") = New JObject()
            If Value("keys") Is Nothing Then Value("keys") = New JObject()
            For Each Source In Sources
                If Value("keys")(Source) Is Nothing Then Value("keys")(Source) = Guid.NewGuid().ToString("N")
            Next
            Dim Loader As LoaderCombo(Of String) = Nothing
            Loader = New LoaderCombo(Of String)("导入旧 PCL 游戏", {
                New LoaderTask(Of String, Integer)("复制游戏、模组和存档",
                    Sub(Task As LoaderTask(Of String, Integer))
                        Dim Index = 0
                        For Each Source In Sources
                            Task.CreateCancellationToken().ThrowIfCancellationRequested()
                            Dim Destination As String
                            If Value("completed")(Source) IsNot Nothing AndAlso Directory.Exists(CStr(Value("completed")(Source))) Then
                                Destination = CStr(Value("completed")(Source))
                            ElseIf PclDataImport.ContainsPath(Paths.Base, Source) Then
                                Destination = PathUtils.AddSlashSuffix(Source)
                            Else
                                Dim Progress As New ImportProgress(Task, Index, Sources.Count)
                                Destination = PclDataImport.CopyGame(Source, Paths.Base & "ImportedGames", Task.CreateCancellationToken(), Progress, CStr(Value("keys")(Source)))
                            End If
                            CaumcRegisterGameFolder(Destination, New DirectoryInfo(Source).Parent.Name)
                            Value("completed")(Source) = Destination
                            CaumcTaskCenter.Recipe(Loader, Value)
                            If Index = 0 Then McFolderSelected = Destination
                            Index += 1
                            Task.Progress = Index / Sources.Count
                        Next
                        RunInUi(Sub()
                                    McFolderListLoader.Start(1, IsForceRestart:=True)
                                    LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun)
                                    Hint("旧 PCL 游戏导入完成", HintType.Green)
                                End Sub)
                    End Sub)
            }) With {.OnStateChanged = AddressOf CaumcTaskState}
            CaumcTaskCenter.Recipe(Loader, Value)
            Loader.Start()
            LoaderTaskbarAdd(Loader)
            RunInUi(Sub() FrmMain.BtnExtraDownload.ShowRefresh())
            Return Loader
    End Function

    Public Sub CaumcTaskState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Failed
                If CaumcTaskCenter.IsTracked(Loader) Then Return
                Logger.Error(Loader.Error, Loader.Name & "失败", LogBehavior.Alert)
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消", HintType.Blue)
        End Select
    End Sub

    Private Class ImportProgress
        Implements IProgress(Of PclImportProgress)
        Private ReadOnly Task As LoaderTask(Of String, Integer)
        Private ReadOnly Index As Integer
        Private ReadOnly Count As Integer
        Public Sub New(Owner As LoaderTask(Of String, Integer), Position As Integer, Total As Integer)
            Task = Owner : Index = Position : Count = Total
        End Sub
        Public Sub Report(Value As PclImportProgress) Implements IProgress(Of PclImportProgress).Report
            Task.Progress = (Index + If(Value.TotalBytes = 0, 0, Value.CompletedBytes / Value.TotalBytes)) / Count
        End Sub
    End Class
End Module
