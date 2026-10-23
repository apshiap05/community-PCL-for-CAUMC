Public Class PageSpeedLeft
    Private ReadOnly Watch As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
    Public Sub New()
        InitializeComponent()
        LabProgress.Text = "—" : LabFile.Text = "0" : LabThread.Text = "0"
        If Not ModeDebug Then
            For Index = 12 To 15
                RowDefinitions(Index).Height = New GridLength(0)
            Next
        End If
        AddHandler Watch.Tick, Sub() Watcher()
    End Sub
    Private Sub Page_Loaded() Handles Me.Loaded
        Watcher() : Watch.Start()
    End Sub
    Private Sub Page_Unloaded() Handles Me.Unloaded
        Watch.Stop()
    End Sub
    Private Sub Watcher()
        If FrmMain Is Nothing OrElse FrmMain.PageCurrent <> FormMain.PageType.TaskManager Then Return
        Dim Tasks = CaumcTaskCenter.Snapshot().Where(Function(Item) Item.State = "running").ToList()
        LabProgress.Text = If(Tasks.Count = 0, "—", Math.Floor(Tasks.Average(Function(Item) Item.Progress) * 100).ToString() & " %")
        LabSpeed.Text = StringUtils.FormatByteSize(NetManager.Speed) & "/s"
        LabFile.Text = Math.Max(0, NetManager.FileRemain).ToString()
        LabThread.Text = NetTaskThreadCount & " / " & NetTaskThreadLimit
        FrmSpeedRight?.RefreshTasks()
    End Sub
    Public Sub TaskRefresh(Loader As LoaderBase)
        RunInUi(Sub() FrmSpeedRight?.RefreshTasks())
    End Sub
    Public Sub TaskRemove(Loader As Object)
        ' The durable record remains available after native loaders leave the taskbar.
    End Sub
End Class
