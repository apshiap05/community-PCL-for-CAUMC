Imports System.Security.Cryptography

' Game/configuration layout stays compatible with upstream PCL.
Public Module CaumcConfig
    Public Const CaumcName As String = "PCL for CAUMC"
    Public Const CaumcSkinSite As String = "https://skin.caumc.cn/"
    Public Const CaumcApiRoot As String = "https://skin.caumc.cn/api/yggdrasil"
    Public Const CaumcArticle As String = "https://doc.caumc.cn/?post=2"
    Private ActiveRoot As String = CaumcApiRoot
    <ThreadStatic> Private RequestRoot As String
    Private ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("PCL for CAUMC/settings/v1")

    Public ReadOnly Property CaumcGameDirectory As String
        Get
            Return Paths.Base & ".minecraft\"
        End Get
    End Property

    Public Sub CaumcInitialize()
        Directory.CreateDirectory(Paths.Base & "PCL")
        Directory.CreateDirectory(CaumcGameDirectory & "versions")
        Dim Selected = Settings.Get(Of String)("LaunchFolderSelect").Replace("$", Paths.Base)
        If String.IsNullOrWhiteSpace(Selected) OrElse Not Directory.Exists(Selected) Then Settings.Set("LaunchFolderSelect", "$.minecraft\")
        Dim PreviousGame = Paths.Base & "CAUMC\.minecraft\"
        If Directory.Exists(PreviousGame & "versions") Then CaumcRegisterGameFolder(PreviousGame, "CAUMC 1.0 游戏")
        Settings.Set("CaumcInitialized", True)
    End Sub

    Public Function CaumcAuthRoot() As String
        If McInstanceSelected IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", McInstanceSelected) = 4 Then
            Return Settings.Get(Of String)("VersionServerAuthServer", McInstanceSelected).TrimEnd("/"c)
        End If
        Return CaumcApiRoot
    End Function

    Public Function CaumcIsCau() As Boolean
        Return String.Equals(CaumcAuthRoot(), CaumcApiRoot, StringComparison.OrdinalIgnoreCase)
    End Function

    Public Function CaumcNeedsAuthlib(Instance As McInstance) As Boolean
        Dim Mode = Settings.Get(Of Integer)("VersionServerLogin", Instance)
        Return Mode = 4 OrElse (Mode = 0 AndAlso Settings.Get(Of Integer)("CaumcLoginChoice") = 3)
    End Function

    Public Function CaumcRegisterAddress() As String
        If CaumcIsCau() Then Return CaumcSkinSite & "auth/register"
        Return If(McInstanceSelected Is Nothing, Settings.Get(Of String)("CacheAuthServerRegister"), Settings.Get(Of String)("VersionServerAuthRegister", McInstanceSelected))
    End Function

    Public Sub CaumcSelectAuthScope(Instance As McInstance)
        Dim Root = If(Instance IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance) = 4,
                      Settings.Get(Of String)("VersionServerAuthServer", Instance).TrimEnd("/"c), CaumcApiRoot)
        If Root <> ActiveRoot Then
            ActiveRoot = Root
            McLoginAuthLoader.Input = Nothing
            PageLaunchLeft.SkinAuth.Input = Nothing
        End If
        Settings.Set("CacheAuthServerServer", Root)
        Settings.Set("CacheAuthServerName", If(String.Equals(Root, CaumcApiRoot, StringComparison.OrdinalIgnoreCase), "CAU 皮肤站", If(Instance Is Nothing, "第三方登录", Settings.Get(Of String)("VersionServerAuthName", Instance))))
        Settings.Set("CacheAuthServerRegister", CaumcRegisterAddress())
    End Sub

    Public Function CaumcIsAuthSetting(Key As String) As Boolean
        Return Key.StartsWith("CacheAuth", StringComparison.Ordinal) OrElse Key.StartsWith("LoginAuth", StringComparison.Ordinal)
    End Function

    Public ReadOnly Property CaumcAuthScope As String
        Get
            Using Hash = SHA256.Create()
                Return BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(If(RequestRoot, ActiveRoot)))).Replace("-", "")
            End Using
        End Get
    End Property

    Public Function CaumcEnterRequestScope(BaseUrl As String) As IDisposable
        Return New RequestScope(BaseUrl)
    End Function

    Private NotInheritable Class RequestScope
        Implements IDisposable
        Private ReadOnly Previous As String = RequestRoot
        Public Sub New(BaseUrl As String)
            If Not BaseUrl.EndsWith("/authserver", StringComparison.Ordinal) Then Throw New ArgumentException("无效的认证地址")
            RequestRoot = BaseUrl.Substring(0, BaseUrl.Length - "/authserver".Length)
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            RequestRoot = Previous
        End Sub
    End Class

    Public Function CaumcRegistryPath(Key As String) As String
        Return CaumcRegistryLocation(Key, Environment.GetCommandLineArgs().Contains("--caumc-selftest"))
    End Function

    Friend Function CaumcRegistryLocation(Key As String, Test As Boolean) As String
        Dim Encrypted = Settings.Entries IsNot Nothing AndAlso Settings.Entries.ContainsKey(Key) AndAlso Settings.Entries(Key).Encrypted
        Return "HKEY_CURRENT_USER\Software\" & If(Test, "PCLForCAUMCTest", If(CaumcIsAuthSetting(Key) OrElse Encrypted, "PCLForCAUMC", "PCL")) &
            If(CaumcIsAuthSetting(Key), "\Auth\" & CaumcAuthScope, "")
    End Function

    Public Sub CaumcUpdateRemember()
        For Each Entry In Settings.Entries.Values.Where(Function(Item) CaumcIsAuthSetting(Item.Key))
            If Entry.GetCache(Nothing) IsNot Nothing Then Entry.Save()
        Next
    End Sub

    Public Function CaumcProtect(Value As String) As String
        Return "dpapi:v1:" & Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Value), Entropy, DataProtectionScope.CurrentUser))
    End Function

    Public Function CaumcUnprotect(Value As String) As String
        If Not Value.StartsWith("dpapi:v1:", StringComparison.Ordinal) Then Throw New CryptographicException("无法识别的 CAUMC 凭据格式")
        Return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Value.Substring(9)), Entropy, DataProtectionScope.CurrentUser))
    End Function

    Public Sub CaumcLogout()
        Dim Root = ActiveRoot
        Dim Access = Settings.Get(Of String)("CacheAuthAccess")
        Dim Client = Settings.Get(Of String)("CacheAuthClient")
        If McLoginAuthLoader.State = LoadState.Loading Then McLoginAuthLoader.Cancel()
        For Each Key In {"CacheAuthAccess", "CacheAuthClient", "CacheAuthUuid", "CacheAuthName", "CacheAuthUsername", "CacheAuthPass", "LoginAuthEmail", "LoginAuthPass"}
            Settings.Set(Key, "")
        Next
        ' Capture the endpoint and tokens before clearing the local session.
        If Access <> "" Then RunInThread(
            Sub()
                Try
                    NetRequestByClientRetry(Root & "/authserver/invalidate", HttpMethod.Post,
                        Content:=New JObject(New JProperty("accessToken", Access), New JProperty("clientToken", Client)).ToString(Newtonsoft.Json.Formatting.None),
                        ContentType:="application/json")
                Catch
                    Logger.Info("服务端会话注销未完成，本地登录状态已清除")
                End Try
            End Sub)
    End Sub
End Module
