Imports PCLCS.CAUMC
Imports System.Threading
Imports System.Windows.Documents

Public NotInheritable Class CaumcHomepage
    Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(15)}
    Private Shared Pending As CancellationTokenSource
    Private Shared Generation As Integer
    Private Shared SelectedPost As Integer = 2
    Friend Shared Property CurrentLoad As System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask

    Public Shared Sub Show(Target As StackPanel)
        SelectGuide(Target, SelectedPost)
    End Sub

    Friend Shared Function SelectGuide(Target As StackPanel, PostId As Integer, Optional Http As HttpClient = Nothing) As System.Threading.Tasks.Task
        Dim Guide = GuideCatalog.ForPost(PostId)
        SelectedPost = Guide.PostId
        CurrentLoad = LoadGuideAsync(Target, Guide, If(Http, Client))
        Return CurrentLoad
    End Function

    Friend Shared Function TryNavigateGuide(Target As StackPanel, Address As Uri, Optional Http As HttpClient = Nothing) As Boolean
        Dim PostId = GuideCatalog.ResolvePostId(Address.AbsoluteUri, GuideCatalog.ForPost(SelectedPost).Source)
        If Not PostId.HasValue Then Return False
        SelectGuide(Target, PostId.Value, Http)
        Dim Parent As DependencyObject = Target
        While Parent IsNot Nothing
            If TypeOf Parent Is ScrollViewer Then
                DirectCast(Parent, ScrollViewer).ScrollToTop()
                Exit While
            End If
            Parent = VisualTreeHelper.GetParent(Parent)
        End While
        Return True
    End Function

    Private Shared Async Function LoadGuideAsync(Target As StackPanel, Guide As GuideDefinition, Http As HttpClient) As System.Threading.Tasks.Task
        Dim ThisGeneration = Interlocked.Increment(Generation)
        If Pending IsNot Nothing Then
            Pending.Cancel()
            Pending.Dispose()
        End If
        Pending = New CancellationTokenSource()
        Dim Token = Pending.Token
        Dim Navigate As Action(Of Uri) =
            Sub(Address)
                If Not TryNavigateGuide(Target, Address, Http) Then OpenWebsite(Address.AbsoluteUri)
            End Sub
        Dim Service As New ArticleClient(Http, Paths.Base & "PCLForCAUMC\Cache\" & Guide.CacheFileName, Guide.Source)
        Dim Cached = Service.ReadCache()
        Dim Status = BuildPage(Target, Cached, Guide.PostId, Navigate)
        Status.Text = If(Cached Is Nothing, "正在加载" & Guide.Title & "……", "正在检查更新；缓存检查时间：" & Cached.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
        Try
            Dim Latest = Await Service.RefreshAsync(Token)
            If ThisGeneration <> Generation Then Return
            Status = BuildPage(Target, Latest, Guide.PostId, Navigate)
            Status.Text = "已更新 · " & Latest.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        Catch ex As OperationCanceledException
            If ThisGeneration <> Generation Then Return
            Status.Text = "连接超时。" & If(Cached Is Nothing, "暂无缓存，请点击刷新重试。", "正在显示缓存，检查时间：" & Cached.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
        Catch ex As Exception
            If ThisGeneration <> Generation Then Return
            Status.Text = "刷新失败。" & If(Cached Is Nothing, "暂无缓存，可重试或在浏览器中查看原文。", "正在显示缓存，检查时间：" & Cached.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
            Logger.Warn(ex, "获取 CAUMC " & Guide.Title & "失败，已保留有效缓存")
        End Try
    End Function

    Friend Shared Function BuildPage(Target As StackPanel, Snapshot As ArticleSnapshot, Optional PostId As Integer = 2, Optional Navigate As Action(Of Uri) = Nothing) As TextBlock
        Dim Guide = GuideCatalog.ForPost(PostId)
        Dim Navigation As Action(Of Uri) = If(Navigate,
            Sub(Address)
                If Not TryNavigateGuide(Target, Address) Then OpenWebsite(Address.AbsoluteUri)
            End Sub)
        Target.Children.Clear()
        Dim Tabs As New Grid With {.Name = "CaumcGuideTabs", .Margin = New Thickness(0, 0, 0, 12)}
        For Index = 0 To GuideCatalog.All.Count - 1
            Tabs.ColumnDefinitions.Add(New ColumnDefinition())
            Dim TabGuide = GuideCatalog.All(Index)
            Dim Tab As New MyButton With {.Text = TabGuide.Title, .Tag = TabGuide.PostId, .Height = 36,
                .Margin = New Thickness(If(Index = 0, 0, 4), 0, If(Index = GuideCatalog.All.Count - 1, 0, 4), 0),
                .ColorType = If(TabGuide.PostId = Guide.PostId, MyButton.ColorState.Highlight, MyButton.ColorState.Normal)}
            Tab.SetValue(Grid.ColumnProperty, Index)
            AddHandler Tab.Click, Sub() Navigation(TabGuide.Source)
            Tabs.Children.Add(Tab)
        Next
        Target.Children.Add(Tabs)
        If Not Settings.Get(Of Boolean)("CaumcGuideDismissed") Then AddGuide(Target)
        ' Images change the article height asynchronously. A fixed-height card animation
        ' can clip later images and prevent the outer scroller from reaching them.
        Dim Card As New MyCard With {.Title = CaumcName, .Margin = New Thickness(0, 0, 0, 15), .UseAnimation = False, .GlassMaterial = "Navigation"}
        Dim Body As New StackPanel With {.Margin = New Thickness(18, 38, 18, 16)}
        Dim Actions As New WrapPanel With {.Margin = New Thickness(0, 0, 0, 12)}
        AddButton(Actions, "CAU 皮肤站", Sub() OpenWebsite(CaumcSkinSite))
        AddButton(Actions, "导入整合包", Sub() ModpackInstall())
        AddButton(Actions, "使用引导", Sub()
                                          Settings.Set("CaumcGuideDismissed", False)
                                          If Not Target.Children.OfType(Of MyCard).Any(Function(Item) Object.Equals(Item.Tag, "CAUMCGuide")) Then AddGuide(Target)
                                      End Sub)
        AddButton(Actions, "刷新", Sub() Navigation(Guide.Source))
        Dim Original = AddButton(Actions, "浏览器查看原文", Sub() OpenWebsite(Guide.Source.AbsoluteUri))
        Original.Tag = Guide.Source.AbsoluteUri
        Body.Children.Add(Actions)
        Dim Status As New TextBlock With {.TextWrapping = TextWrapping.Wrap, .FontSize = 11, .Margin = New Thickness(0, 0, 0, 10)}
        Status.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        Body.Children.Add(Status)
        If Snapshot IsNot Nothing Then
            Dim Document As New FlowDocument With {.PagePadding = New Thickness(0), .FontSize = 13.5, .LineHeight = 22}
            Document.SetResourceReference(FlowDocument.ForegroundProperty, "ColorBrush1")
            AppendBlocks(Document.Blocks, Snapshot.Nodes, Guide.Source, Navigation)
            Dim Reader As New RichTextBox With {.Document = Document, .IsReadOnly = True, .IsDocumentEnabled = True,
                .BorderThickness = New Thickness(0), .Background = Brushes.Transparent, .Padding = New Thickness(0),
                .VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                .IsUndoEnabled = False}
            AddHandler Reader.RequestBringIntoView,
                Sub(Sender, Args)
                    Dim Link = TryCast(Args.TargetObject, Hyperlink)
                    If Link Is Nothing Then Return
                    ' WPF forwards a content-element focus request without its text bounds. In this
                    ' outer scroller that reveals the whole reader's top, moving the link before
                    ' mouse-up. Reveal only the link, also preserving keyboard navigation.
                    Args.Handled = True
                    Dim Bounds = Link.ContentStart.GetCharacterRect(LogicalDirection.Forward)
                    Bounds.Union(Link.ContentEnd.GetCharacterRect(LogicalDirection.Backward))
                    If Not Bounds.IsEmpty Then Reader.BringIntoView(Bounds)
                End Sub
            Body.Children.Add(Reader)
        End If
        Card.Children.Add(Body)
        Target.Children.Add(Card)
        Return Status
    End Function

    Private Shared Sub AddGuide(Target As StackPanel)
        Dim Card As New MyCard With {.Title = "开始游玩 CAUMC", .Tag = "CAUMCGuide", .Margin = New Thickness(0, 0, 0, 15), .GlassMaterial = "Navigation"}
        Dim Body As New StackPanel With {.Margin = New Thickness(18, 38, 18, 16)}
        For Each Instruction In {
            "1. 登录账号：左侧默认选择 CAU，输入皮肤站账号和密码，点击「登录 CAU 皮肤站」。还没有账号或角色时，请先前往 CAU 皮肤站注册并创建角色。",
            "2. 导入整合包：从社区提供的渠道获取整合包，拖入启动器窗口，或点击下方「导入整合包」，按原有流程完成导入。",
            "3. 进入服务器：选中导入的游戏版本并启动 Minecraft，在游戏的「多人游戏」中进入 CAUMC。",
            "游戏下载、版本管理、Mod、设置和诊断仍可通过启动器原有入口使用。"}
            Dim Text As New TextBlock With {.Text = Instruction, .TextWrapping = TextWrapping.Wrap, .LineHeight = 22,
                .Margin = New Thickness(0, 0, 0, 8)}
            Text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Body.Children.Add(Text)
        Next
        Dim Actions As New WrapPanel()
        AddButton(Actions, "CAU 皮肤站", Sub() OpenWebsite(CaumcSkinSite))
        AddButton(Actions, "导入整合包", Sub() ModpackInstall())
        AddButton(Actions, "收起引导", Sub()
                                          Settings.Set("CaumcGuideDismissed", True)
                                          Target.Children.Remove(Card)
                                      End Sub)
        Body.Children.Add(Actions)
        Card.Children.Add(Body)
        Target.Children.Insert(1, Card)
    End Sub

    Private Shared Function AddButton(Panel As WrapPanel, Label As String, Action As Action) As MyButton
        Dim Button As New MyButton With {.Text = Label, .TextPadding = New Thickness(12, 0, 12, 0), .Height = 32, .Margin = New Thickness(0, 0, 8, 6)}
        AddHandler Button.Click, Sub() Action()
        Panel.Children.Add(Button)
        Return Button
    End Function

    Private Shared Sub AppendBlocks(Blocks As BlockCollection, Nodes As IEnumerable(Of ArticleNode), Source As Uri, Navigate As Action(Of Uri))
        Dim PendingParagraph As Paragraph = Nothing
        For Each Node In Nodes
            If Node.Kind = "text" AndAlso String.IsNullOrWhiteSpace(Node.Text) AndAlso PendingParagraph Is Nothing Then Continue For
            If {"text", "link", "bold", "italic", "code", "break", "image"}.Contains(Node.Kind) Then
                If PendingParagraph Is Nothing Then
                    PendingParagraph = New Paragraph With {.Margin = New Thickness(0, 0, 0, 10)}
                    Blocks.Add(PendingParagraph)
                End If
                AppendInline(PendingParagraph.Inlines, Node, Source, Navigate)
                Continue For
            End If
            PendingParagraph = Nothing
            Select Case Node.Kind
                Case "paragraph", "heading", "pre", "quote"
                    If Node.Kind = "quote" Then
                        Dim Section As New Section With {.Padding = New Thickness(12, 4, 0, 4), .BorderThickness = New Thickness(3, 0, 0, 0), .Margin = New Thickness(0, 4, 0, 12)}
                        Section.SetResourceReference(Section.BorderBrushProperty, "ColorBrush5")
                        AppendBlocks(Section.Blocks, Node.Children, Source, Navigate)
                        Blocks.Add(Section)
                    Else
                        Dim Para As New Paragraph With {.Margin = New Thickness(0, 0, 0, 10)}
                        If Node.Kind = "heading" Then
                            Para.FontSize = If(Node.Level = 1, 23, If(Node.Level = 2, 19, 16))
                            Para.FontWeight = FontWeights.SemiBold
                            Para.Margin = New Thickness(0, 12, 0, 10)
                        End If
                        If Node.Kind = "pre" Then
                            Para.FontFamily = New FontFamily("Consolas")
                            Para.Inlines.Add(New Run(Node.Text))
                        Else
                            For Each Child In Node.Children
                                AppendInline(Para.Inlines, Child, Source, Navigate)
                            Next
                        End If
                        Blocks.Add(Para)
                    End If
                Case "list", "orderedlist"
                    Dim List As New System.Windows.Documents.List With {.MarkerStyle = If(Node.Kind = "list", TextMarkerStyle.Disc, TextMarkerStyle.Decimal), .Margin = New Thickness(0, 0, 0, 10), .Padding = New Thickness(22, 0, 0, 0)}
                    For Each Child In Node.Children.Where(Function(Item) Item.Kind = "item")
                        Dim Item As New ListItem()
                        AppendBlocks(Item.Blocks, Child.Children, Source, Navigate)
                        If Item.Blocks.Count = 0 Then Item.Blocks.Add(New Paragraph())
                        List.ListItems.Add(Item)
                    Next
                    Blocks.Add(List)
                Case "table"
                    Dim Table As New Table With {.CellSpacing = 0, .Margin = New Thickness(0, 4, 0, 12)}
                    Dim Group As New TableRowGroup()
                    For Each Row In Descendants(Node).Where(Function(Item) Item.Kind = "row")
                        Dim RenderedRow As New TableRow()
                        For Each Cell In Row.Children.Where(Function(Item) Item.Kind = "cell")
                            Dim RenderedCell As New TableCell With {.Padding = New Thickness(6), .BorderThickness = New Thickness(0.5)}
                            RenderedCell.SetResourceReference(TableCell.BorderBrushProperty, "ColorBrush5")
                            AppendBlocks(RenderedCell.Blocks, Cell.Children, Source, Navigate)
                            If RenderedCell.Blocks.Count = 0 Then RenderedCell.Blocks.Add(New Paragraph())
                            RenderedRow.Cells.Add(RenderedCell)
                        Next
                        Group.Rows.Add(RenderedRow)
                    Next
                    Table.RowGroups.Add(Group)
                    Blocks.Add(Table)
                Case "rule"
                    Dim Para As New Paragraph With {.BorderThickness = New Thickness(0, 0, 0, 1), .Margin = New Thickness(0, 6, 0, 12)}
                    Para.SetResourceReference(Paragraph.BorderBrushProperty, "ColorBrush6")
                    Blocks.Add(Para)
                Case Else
                    AppendBlocks(Blocks, Node.Children, Source, Navigate)
            End Select
        Next
    End Sub

    Private Shared Iterator Function Descendants(Node As ArticleNode) As IEnumerable(Of ArticleNode)
        For Each Child In Node.Children
            Yield Child
            For Each Grandchild In Descendants(Child)
                Yield Grandchild
            Next
        Next
    End Function

    Private Shared Sub AppendInline(Inlines As InlineCollection, Node As ArticleNode, Source As Uri, Navigate As Action(Of Uri))
        Select Case Node.Kind
            Case "text"
                Inlines.Add(New Run(Node.Text))
            Case "break"
                Inlines.Add(New LineBreak())
            Case "code", "pre"
                Inlines.Add(New Run(Node.Text) With {.FontFamily = New FontFamily("Consolas")})
            Case "image"
                Dim Url = ArticleParser.SafeUrl(Node.Url, Source)
                If Url = "" Then Return
                Inlines.Add(New InlineUIContainer(CaumcGuideImage.Create(New Uri(Url), Node.Text)))
            Case Else
                Dim Span As Span
                Select Case Node.Kind
                    Case "bold" : Span = New Bold()
                    Case "italic" : Span = New Italic()
                    Case "link"
                        Dim Url = ArticleParser.SafeUrl(Node.Url, Source)
                        If Url = "" Then
                            Span = New Span()
                        Else
                            Dim Link As New Hyperlink With {.NavigateUri = New Uri(Url)}
                            AddHandler Link.RequestNavigate, Sub(Sender, Args)
                                                                 Args.Handled = True
                                                                 Navigate(Args.Uri)
                                                             End Sub
                            Span = Link
                        End If
                    Case Else : Span = New Span()
                End Select
                For Each Child In Node.Children
                    AppendInline(Span.Inlines, Child, Source, Navigate)
                Next
                Inlines.Add(Span)
        End Select
    End Sub
End Class
