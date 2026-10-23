Imports PCLCS.CAUMC

Public Class PageSpeedRight
    Private Filter As String = "all"
    Private ReadOnly Rows As New Dictionary(Of String, TaskRow)()
    Private Class TaskRow
        Public Card As MyCard
        Public Stage As TextBlock
        Public Progress As ProgressBar
        Public Message As TextBlock
        Public Actions As WrapPanel
        Public Signature As String
    End Class
    Private Sub Init() Handles Me.Loaded
        RefreshTasks()
    End Sub
    Private Sub ChangeFilter(Sender As Object, Args As EventArgs) Handles FilterAll.Click, FilterActive.Click, FilterPending.Click, FilterHistory.Click
        Filter = If(Sender Is FilterActive, "active", If(Sender Is FilterPending, "pending", If(Sender Is FilterHistory, "history", "all")))
        For Each Button In {FilterAll, FilterActive, FilterPending, FilterHistory}
            Button.ColorType = If(Button Is Sender, MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        Next
        RefreshTasks()
    End Sub
    Public Sub RefreshTasks()
        Dim Items = CaumcTaskCenter.Snapshot()
        Dim VisibleCount = 0
        For Each Item In Items
            Dim Row As TaskRow = Nothing
            If Not Rows.TryGetValue(Item.Id, Row) Then
                Row = New TaskRow With {.Card = New MyCard With {.Title = Item.Name, .Margin = New Thickness(0, 0, 0, 15)},
                    .Stage = New TextBlock With {.TextWrapping = TextWrapping.Wrap},
                    .Progress = New ProgressBar With {.Height = 4, .Maximum = 1, .Margin = New Thickness(0, 12, 0, 10)},
                    .Message = New TextBlock With {.TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 0, 0, 12)},
                    .Actions = New WrapPanel()}
                Dim Body As New StackPanel With {.Margin = New Thickness(18, 40, 18, 16)}
                Body.Children.Add(Row.Stage) : Body.Children.Add(Row.Progress) : Body.Children.Add(Row.Message) : Body.Children.Add(Row.Actions)
                Row.Card.Children.Add(Body)
                Rows.Add(Item.Id, Row) : PanMain.Children.Add(Row.Card)
            End If
            Dim Visible = Filter = "all" OrElse (Filter = "active" AndAlso Item.State = "running") OrElse
                (Filter = "pending" AndAlso Item.NeedsAttention) OrElse (Filter = "history" AndAlso {"succeeded", "canceled"}.Contains(Item.State))
            Row.Card.Visibility = If(Visible, Visibility.Visible, Visibility.Collapsed)
            If Visible Then VisibleCount += 1
            Row.Stage.Text = Item.Kind & " · " & TaskJournal.StateText(Item.State) & " · " & Item.Stage
            Row.Progress.Value = Item.Progress
            Row.Message.Text = If(Item.State = "failed", Item.Error, If(Item.NeedsAttention,
                If(CStr(Item.Recipe("type")) = "native", "已保留任务记录，请返回原操作重新执行。", "已保存进度，选择继续后恢复。"), Math.Floor(Item.Progress * 100) & "%"))
            Row.Message.MaxHeight = 90
            Dim CanPause = CaumcTaskCenter.CanPause(Item)
            Dim Signature = Item.State & "|" & CanPause.ToString() & "|" & CaumcTaskCenter.CanResume(Item).ToString()
            If Row.Signature = Signature Then Continue For
            Row.Signature = Signature
            Row.Actions.Children.Clear()
            If CanPause Then AddAction(Row, "暂停", Sub() CaumcTaskCenter.StopTask(Item.Id, True))
            If CaumcTaskCenter.CanResume(Item) Then AddAction(Row, If(Item.State = "failed", "重试", "继续"), Sub() CaumcTaskCenter.ResumeTask(Item.Id))
            If Item.State = "running" OrElse Item.NeedsAttention Then AddAction(Row, "取消", Sub() CaumcTaskCenter.StopTask(Item.Id, False))
            If Item.State = "failed" OrElse CStr(Item.Recipe("type")) = "native" Then AddAction(Row, "诊断与日志", Sub() CaumcTaskCenter.ShowDiagnostics(Item))
            If Item.State = "succeeded" Then AddAction(Row, "导出日志", Sub() CaumcTaskCenter.ExportLog(Item))
            If Item.State = "succeeded" AndAlso CStr(Item.Recipe("type")) = "launcher-update" AndAlso
                LauncherUpdates.VersionOf(CStr(Item.Recipe("version"))) > CaumcLauncherUpdate.CurrentVersion AndAlso File.Exists(CStr(Item.Recipe("archive"))) Then
                AddAction(Row, "安装更新", AddressOf CaumcLauncherUpdate.InstallReady)
                Row.Message.Text = "新版已下载，等待确认重启安装。"
            End If
        Next
        EmptyCard.Visibility = If(VisibleCount = 0, Visibility.Visible, Visibility.Collapsed)
        EmptyText.Text = If(Items.Count = 0, "还没有任务", "这里暂时没有任务")
    End Sub
    Private Shared Sub AddAction(Row As TaskRow, Text As String, Action As Action)
        Dim Button As New MyButton With {.Text = Text, .Height = 32, .MinWidth = 82, .Margin = New Thickness(0, 0, 8, 0)}
        AddHandler Button.Click, Sub() Action()
        Row.Actions.Children.Add(Button)
    End Sub
End Class
