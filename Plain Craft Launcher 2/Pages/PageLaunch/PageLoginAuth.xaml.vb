Public Class PageLoginAuth
    Private IsFirstLoad As Boolean = True
    Friend Property IsLoginPending As Boolean
    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        If Not IsLoginPending Then BtnLogin.Text = If(CaumcIsCau(), "登录 CAU 皮肤站", "登录")
        '记住密码
        CheckRemember.Checked = Settings.Get(Of Boolean)("LoginRemember")
        If KeepInput AndAlso Not IsFirstLoad Then '避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            '保留输入，只刷新下拉框列表
            Dim Input As String = ComboName.Text
            ComboName.ItemsSource = If(Settings.Get(Of String)("LoginAuthEmail") = "", Nothing, Settings.Get(Of String)("LoginAuthEmail").Split("¨"))
            ComboName.Text = Input
        Else
            '不保留输入，刷新列表后自动选择第一项
            If Settings.Get(Of String)("LoginAuthEmail") = "" Then
                ComboName.ItemsSource = Nothing
                ComboName.Text = ""
                TextPass.Password = ""
            Else
                ComboName.ItemsSource = Settings.Get(Of String)("LoginAuthEmail").Split("¨")
                ComboName.Text = Settings.Get(Of String)("LoginAuthEmail").BeforeFirst("¨")
                If Settings.Get(Of Boolean)("LoginRemember") Then TextPass.Password = Settings.Get(Of String)("LoginAuthPass").BeforeFirst("¨").Trim
            End If
        End If
        IsFirstLoad = False
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim Server As String = CaumcAuthRoot() & "/authserver"
        If FrmLoginAuth Is Nothing Then
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = "", .Password = "", .Description = If(CaumcIsCau(), "CAU 皮肤站", "Authlib-Injector"), .Type = McLoginType.Auth}
        Else
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = FrmLoginAuth.ComboName.Text.Replace("¨", "").Trim, .Password = FrmLoginAuth.TextPass.Password.Replace("¨", "").Trim, .Description = If(CaumcIsCau(), "CAU 皮肤站", "Authlib-Injector"), .Type = McLoginType.Auth}
        End If
    End Function
    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginServer) As String
        If LoginData.UserName = "" Then Return "账号不能为空！"
        If LoginData.Password = "" Then Return "密码不能为空！"
        Return ""
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function

    Private Async Sub BtnLogin_Click(sender As Object, e As EventArgs) Handles BtnLogin.Click
        Await LoginAsync()
    End Sub

    Friend Async Function LoginAsync() As System.Threading.Tasks.Task
        If IsLoginPending Then
            McLoginAuthLoader.Cancel()
            TextStatus.Text = "正在取消登录……"
            Return
        End If
        If McLaunchLoader.State = LoadState.Loading OrElse McLoginAuthLoader.State = LoadState.Loading Then
            TextStatus.Text = "正在启动或登录中，请稍后再试。"
            Return
        End If
        Dim Input = GetLoginData()
        Dim Invalid = IsVaild(Input)
        If Invalid <> "" Then
            TextStatus.Text = Invalid
            Return
        End If
        Dim Scope = CaumcAuthScope
        IsLoginPending = True
        ComboName.IsEnabled = False
        TextPass.IsEnabled = False
        CheckRemember.IsEnabled = False
        BtnLogin.Text = "取消登录"
        TextStatus.Text = "正在验证账号……"
        Try
            McLoginAuthLoader.Start(Input, IsForceRestart:=True)
            Await System.Threading.Tasks.Task.Run(Sub() McLoginAuthLoader.WaitForExit(Input))
            If Scope = CaumcAuthScope AndAlso Settings.Get(Of McLoginType)("LoginType") = McLoginType.Auth AndAlso
               Settings.Get(Of String)("CacheAuthAccess") <> "" Then
                TextStatus.Text = "登录成功。"
                FrmLaunchLeft.RefreshPage(False, True)
            End If
        Catch ex As Exception
            If ex.IsCanceled Then
                TextStatus.Text = "登录已取消。"
            Else
                TextStatus.Text = "登录失败：" & ex.Message.TrimStart("$"c)
            End If
        Finally
            IsLoginPending = False
            ComboName.IsEnabled = True
            TextPass.IsEnabled = True
            CheckRemember.IsEnabled = True
            BtnLogin.Text = If(CaumcIsCau(), "登录 CAU 皮肤站", "登录")
        End Try
    End Function

    '保存输入信息
    Private Sub ComboName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboName.TextChanged
        If sender.Text = "" Then TextPass.Password = ""
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")  '迫使其不进行 Validate
    End Sub
    Private Sub TextPass_PasswordChanged(sender As Object, e As RoutedEventArgs) Handles TextPass.PasswordChanged
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")
    End Sub
    Private Sub ComboName_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboName.SelectionChanged
        If sender.SelectedIndex = -1 OrElse Not Settings.Get(Of Boolean)("LoginRemember") Then
            TextPass.Password = ""
        Else
            TextPass.Password = Settings.Get(Of String)("LoginAuthPass").ToString.Split("¨")(sender.SelectedIndex).Trim
        End If
    End Sub
    Private Sub CheckBoxChange(sender As MyCheckBox, e As Object) Handles CheckRemember.Change
        If AniControlEnabled = 0 Then
            Settings.Set(sender.Tag, sender.Checked)
            CaumcUpdateRemember()
        End If
    End Sub

    '链接处理
    Private Sub ComboName_TextChanged() Handles ComboName.TextChanged
        BtnLink.Content = If(ComboName.Text = "", "注册账号", "找回密码")
    End Sub
    Private Sub Btn_Click(sender As Object, e As EventArgs) Handles BtnLink.Click
        If BtnLink.Content = "注册账号" Then
            OpenWebsite(CaumcRegisterAddress())
        Else
            Dim Website As String = CaumcRegisterAddress()
            OpenWebsite(Website.Replace("/auth/register", "/auth/forgot"))
        End If
    End Sub
    '切换注册按钮可见性
    Private Sub ReloadRegisterButton() Handles Me.Loaded
        Dim Address As String = CaumcRegisterAddress()
        BtnLink.Visibility = If(String.IsNullOrEmpty(New ValidateHttp().Validate(Address)), Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class
