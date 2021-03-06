﻿Imports System.Collections.Concurrent
Imports System.ComponentModel
Imports System.IO
Imports System.Threading
Imports WinForms = System.Windows.Forms

Class MainWindow
    Implements IDisposable

#Region "fields"

    Private db As New Database
    Private WithEvents SystrayIcon As New WinForms.NotifyIcon
    Private WithEvents CloseMenu As New WinForms.MenuItem
    Private WithEvents LibraryWatcher As FileSystemWatcher

    Private ManuallyClosing As Boolean = False
    Private ReadyString As String = "Ready."

    Private TaskQueue As New ConcurrentQueue(Of Func(Of Task))
    Private CancelWatching As New CancellationTokenSource

    Private LaunchTab As TabItem = Nothing

#End Region

#Region "helper methods"

    Private Sub ToggleMenus(MenuState As Boolean)
        LoadFileMenu.IsEnabled = MenuState
        LoadFileIntoDBMenu.IsEnabled = MenuState
        LoadDirMenu.IsEnabled = MenuState
        LoadDirIntoDBMenu.IsEnabled = MenuState
        ImportDirMenu.IsEnabled = MenuState
        WatchDirMenu.IsEnabled = MenuState
    End Sub

    Private Async Function ImportDir(dir As String) As Task
        Dim ImportCount As UInteger = 0
        Dim ProgressCount As UInteger = 0

        StatusBarText.Text = "Loading tracks from library..."
        Me.Cursor = Cursors.Wait
        ToggleMenus(False)
        Await Task.Yield()

        Dim TrackFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        Dim OverallCount = TrackFiles.Count

        For Each FileName In TrackFiles
            ProgressCount += 1
            StatusBarText.Text = $"Loading tracks from library... ({ProgressCount} of {OverallCount})"

            If IsMusicFile(FileName) Then
                Dim thisTrack = Track.Load(FileName)

                If thisTrack Is Nothing Then
                    Await Task.Yield()
                    Continue For
                End If

                Dim compareResult = Await db.CompareTrackToDB(thisTrack)

                If compareResult Is Nothing OrElse compareResult = False Then
                    ' either the track isn't in the database at all, or the database is out of sync
                    ' either way, save the track into the database
                    Await db.SaveTrack(thisTrack)
                    ImportCount += 1
                End If
            End If
        Next

        ' because this is mainly a DB maintenance option, there's nothing to really display
        trackInfoGrid.ItemsSource = New List(Of TagRow)

        Await RefreshQueries()

        Dim CompleteText = $"Imported {ImportCount} (of {ProgressCount}) tracks to database."
        MessageBox.Show(CompleteText)
        StatusBarText.Text = CompleteText

        Me.Cursor = Cursors.Arrow
        ToggleMenus(True)
    End Function

    Private Function IsMusicFile(FileName As String) As Boolean
        Return FileName.EndsWith(".mp3") OrElse FileName.EndsWith(".m4a")
    End Function

    Private Async Function RefreshQueries(Optional CancellationToken As CancellationToken = Nothing) As Task
        Dim NotifyTabs As New List(Of TabItem)

        Dim DiscCorruptionResults = Await db.DiskCorruption(CancellationToken)
        If DiscCorruptionResults.Count > DiscCorruptionGrid.Items.Count And Not Me.IsVisible Then
            NotifyTabs.Add(DiscCorruptionTab)
        End If
        DiscCorruptionGrid.ItemsSource = DiscCorruptionResults

        Dim TrackCountResults = Await db.MissingTrackCount(CancellationToken)
        If TrackCountResults.Count > MissingTrackCountGrid.Items.Count And Not Me.IsVisible Then
            NotifyTabs.Add(MissingTrackCountTab)
        End If
        MissingTrackCountGrid.ItemsSource = TrackCountResults

        Dim ReplayGainResults = Await db.CorruptedReplayGain(CancellationToken)
        If ReplayGainResults.Count > CorruptReplayGainGrid.Items.Count And Not Me.IsVisible Then
            NotifyTabs.Add(CorruptReplayGainTab)
        End If
        CorruptReplayGainGrid.ItemsSource = ReplayGainResults

        If NotifyTabs.Count > 0 Then
            Dim NotifyMessage = ""

            For Each ActiveTab In NotifyTabs
                If NotifyMessage <> "" Then
                    NotifyMessage += "\n"
                End If

                If ActiveTab Is DiscCorruptionTab Then
                    NotifyMessage += "The disc tags on some albums are corrupted."
                ElseIf ActiveTab Is MissingTrackCountTab Then
                    NotifyMessage += "Some tracks are missing Track Count tags."
                ElseIf ActiveTab Is CorruptReplayGainTab Then
                    NotifyMessage += "The ReplayGain tags on some albums are corrupted."
                End If
            Next

            LaunchTab = NotifyTabs.First()
            SystrayIcon.ShowBalloonTip(10000,
                                       "Music Library Changed",
                                       NotifyMessage,
                                       WinForms.ToolTipIcon.Warning)
        End If
    End Function

    Private Async Function LoadFile(SaveToDB As Boolean) As Task
        Using pickFile As New WinForms.OpenFileDialog With {
                    .Filter = "Music files|*.mp3;*.m4a"
                }
            StatusBarText.Text = "Please select a track."
            If pickFile.ShowDialog() = WinForms.DialogResult.OK Then
                Me.Cursor = Cursors.Wait
                ToggleMenus(False)
                StatusBarText.Text = "Loading track information..."

                Dim fileName = pickFile.FileName
                Dim track = Await db.LoadTrackOrUseDisk(fileName, SaveToDB)

                trackInfoGrid.ItemsSource = track.AsRows()

                Me.Cursor = Cursors.Arrow
                ToggleMenus(True)
            End If

            StatusBarText.Text = ReadyString
        End Using
    End Function

    Private Async Function LoadDirectory(SaveToDB As Boolean) As Task
        Using pickDir As New WinForms.FolderBrowserDialog
            StatusBarText.Text = "Please select a directory to display."
            If pickDir.ShowDialog() = WinForms.DialogResult.OK Then
                StatusBarText.Text = "Loading track information from directory..."
                Me.Cursor = Cursors.Wait
                ToggleMenus(False)

                Dim dir = pickDir.SelectedPath
                Dim showTrack As Track = Nothing

                For Each FileName In Directory.EnumerateFiles(dir)
                    If IsMusicFile(FileName) Then
                        Dim thisTrack = Await db.LoadTrackOrUseDisk(FileName, SaveToDB)
                        If showTrack Is Nothing Then
                            showTrack = thisTrack
                        Else
                            showTrack.CombineWith(thisTrack)
                        End If
                    End If
                Next

                If showTrack IsNot Nothing Then
                    trackInfoGrid.ItemsSource = showTrack.AsRows()
                    StatusBarText.Text = ReadyString
                Else
                    trackInfoGrid.ItemsSource = New List(Of TagRow)
                    StatusBarText.Text = "No tracks in selected folder."
                End If

                Me.Cursor = Cursors.Arrow
                ToggleMenus(True)
            Else
                StatusBarText.Text = ReadyString
            End If
        End Using
    End Function

#End Region

#Region "button handlers"

    Private Async Sub LoadFileIntoDBMenu_Click(sender As Object, e As RoutedEventArgs) Handles LoadFileIntoDBMenu.Click
        Await LoadFile(True)
    End Sub

    Private Async Sub LoadFileMenu_Click(sender As Object, e As RoutedEventArgs) Handles LoadFileMenu.Click
        Await LoadFile(False)
    End Sub

    Private Async Sub LoadDirIntoDBMenu_Click(sender As Object, e As RoutedEventArgs) Handles LoadDirIntoDBMenu.Click
        Await LoadDirectory(True)
    End Sub

    Private Async Sub LoadDirMenu_Click(sender As Object, e As RoutedEventArgs) Handles LoadDirMenu.Click
        Await LoadDirectory(False)
    End Sub

    Private Async Sub ImportDirMenu_Click(sender As Object, e As RoutedEventArgs) Handles ImportDirMenu.Click
        StatusBarText.Text = "Please select a directory to import into the database."
        Using pickDir As New WinForms.FolderBrowserDialog
            If pickDir.ShowDialog() = WinForms.DialogResult.OK Then
                Await Me.ImportDir(pickDir.SelectedPath)
            Else
                StatusBarText.Text = ReadyString
            End If
        End Using
    End Sub

    Private Async Sub WatchDirMenu_Click(sender As Object, e As RoutedEventArgs) Handles WatchDirMenu.Click
        WatchDirMenu.IsEnabled = False

        If LibraryWatcher Is Nothing Then
            StatusBarText.Text = "Please select a directory to import and monitor."
            Using PickDir As New WinForms.FolderBrowserDialog
                If PickDir.ShowDialog() = WinForms.DialogResult.OK Then
                    Await Me.ImportDir(PickDir.SelectedPath)

                    WatchDirMenu.IsEnabled = False

                    Me.LibraryWatcher = New FileSystemWatcher(PickDir.SelectedPath) With {
                        .NotifyFilter = NotifyFilters.LastWrite Or NotifyFilters.FileName Or NotifyFilters.DirectoryName Or NotifyFilters.CreationTime,
                        .IncludeSubdirectories = True,
                        .EnableRaisingEvents = True
                    }

                    ReadyString = "Watching library directory."
                    StatusBarText.Text = ReadyString

                    WatchDirMenu.Header = "Cancel Active Watch"
                    WatchDirMenu.IsEnabled = True

                    While Not CancelWatching.IsCancellationRequested
                        Try
                            Dim HadEvents = False
                            Dim NewEvent As Func(Of Task) = Nothing
                            While TaskQueue.TryDequeue(NewEvent)
                                HadEvents = True
                                Try
                                    Await NewEvent()
                                Catch ex As FileNotReadyException
                                    ' iTunes still has a file lock. try it again later.
                                    TaskQueue.Enqueue(
                                        Async Function()
                                            Await Task.Delay(100, CancelWatching.Token)
                                            Try
                                                Await NewEvent()
                                            Catch SecondEx As FileNotReadyException
                                                ' rather than retry this again forever, throw the exception out
                                                Throw New Exception(SecondEx.Message)
                                            End Try
                                        End Function
                                    )
                                End Try
                            End While

                            If HadEvents Then
                                Await Me.RefreshQueries(CancelWatching.Token)
                            End If

                            Await Task.Delay(500, CancelWatching.Token)
                        Catch ex As TaskCanceledException
                            Exit While
                        End Try
                    End While
                Else
                    StatusBarText.Text = ReadyString
                End If
            End Using
        Else
            LibraryWatcher.EnableRaisingEvents = False
            LibraryWatcher.Dispose()
            LibraryWatcher = Nothing

            CancelWatching.Cancel()
            CancelWatching.Dispose()
            CancelWatching = New CancellationTokenSource

            WatchDirMenu.Header = "Import and Watch"

            ReadyString = "Ready."
            StatusBarText.Text = ReadyString

            WatchDirMenu.IsEnabled = True
        End If
    End Sub

#End Region

#Region "main window events"

    Private Async Sub MainWindow_Initialized(sender As Object, e As EventArgs) Handles Me.Initialized
        SystrayIcon.Icon = My.Resources.TagExperimentsIcon
        SystrayIcon.Text = "TagExperiments"

        CloseMenu.Text = "E&xit"
        SystrayIcon.ContextMenu = New WinForms.ContextMenu
        SystrayIcon.ContextMenu.MenuItems.Add(CloseMenu)

        SystrayIcon.Visible = True

        Await RefreshQueries()
    End Sub

    Private Sub MainWindow_Closed(sender As Object, e As EventArgs) Handles Me.Closed
        ' clean up the icon so it doesn't erroneously display after quitting
        SystrayIcon.Visible = False
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If Not ManuallyClosing Then
            ' instead of closing the window, minimize to systray
            e.Cancel = True
            Me.WindowState = WindowState.Minimized
            Me.Hide()
        End If
    End Sub

#End Region

#Region "systray events"

    Private Sub SystrayIcon_DoubleClick(sender As Object, e As EventArgs) Handles SystrayIcon.DoubleClick
        Me.Show()
        Me.WindowState = WindowState.Normal
    End Sub

    Private Sub CloseMenu_Click(sender As Object, e As EventArgs) Handles CloseMenu.Click, ExitMenuItem.Click
        ManuallyClosing = True
        Me.Close()
    End Sub

    Private Sub SystrayIcon_BalloonTipClicked(sender As Object, e As EventArgs) Handles SystrayIcon.BalloonTipClicked
        If Not Me.IsVisible Then
            Me.Show()
        End If
        If Me.WindowState <> WindowState.Normal Then
            Me.WindowState = WindowState.Normal
        End If
        If LaunchTab IsNot Nothing Then
            LaunchTab.IsSelected = True
            LaunchTab = Nothing
        End If
    End Sub

#End Region

#Region "filesystem watcher events"

    Private Sub DispatchEvent(f As Func(Of Task))
        TaskQueue.Enqueue(f)
    End Sub

    Private Sub LibraryWatcher_Changed(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Changed
        DispatchEvent(Function() ChangedEvent(e.FullPath))
    End Sub

    Private Async Function ChangedEvent(FullPath As String) As Task
        If IsMusicFile(FullPath) Then
            Debug.Print("file changed: {0}", FullPath)
            Await db.PushTrackToDB(FullPath, CancelWatching.Token)
        End If
    End Function

    Private Sub LibraryWatcher_Renamed(sender As Object, e As RenamedEventArgs) Handles LibraryWatcher.Renamed
        DispatchEvent(Function() RenamedEvent(e.OldFullPath, e.FullPath))
    End Sub

    Private Async Function RenamedEvent(OldName As String, NewName As String) As Task
        If Directory.Exists(NewName) Then
            Await RenamedDir(OldName, NewName)
        Else
            Await RenamedFile(OldName, NewName)
        End If
    End Function

    Private Sub LibraryWatcher_Created(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Created
        DispatchEvent(Function() CreatedEvent(e.FullPath))
    End Sub

    Private Async Function CreatedEvent(FullPath As String) As Task
        If IsMusicFile(FullPath) Then
            Debug.Print("file created: {0}", FullPath)
            Await db.PushTrackToDB(FullPath, CancelWatching.Token)
        End If
    End Function

    Private Sub LibraryWatcher_Deleted(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Deleted
        DispatchEvent(Function() DeletedEvent(e.FullPath))
    End Sub

    Private Async Function DeletedEvent(FullPath As String) As Task
        If IsMusicFile(FullPath) Then
            Debug.Print("file deleted: {0}", FullPath)
            Await db.DeleteTrack(FullPath, CancelWatching.Token)
        End If
    End Function

    Private Async Function RenamedDir(OldName As String, NewName As String) As Task
        Debug.Print("dir renamed: {0} -> {1}", OldName, NewName)
        Dim BasePath = OldName
        For Each SubFile In Directory.EnumerateFiles(NewName)
            Dim OldPath = Path.Combine(BasePath, Path.GetFileName(SubFile))
            Await RenamedFile(OldPath, SubFile)
        Next
        For Each SubDir In Directory.EnumerateDirectories(NewName)
            Dim OldPath = Path.Combine(BasePath, Path.GetFileName(SubDir))
            Await RenamedDir(OldPath, SubDir)
        Next
    End Function

    Private Async Function RenamedFile(OldName As String, NewName As String) As Task
        If IsMusicFile(OldName) Then
            Debug.Print("file renamed: {0} -> {1}", OldName, NewName)
            Await db.RenameTrackFile(OldName, NewName, CancelWatching.Token)
        End If
    End Function

#End Region

#Region "IDisposable Support"

    Private HasBeenDisposed As Boolean ' To detect redundant calls

    ' IDisposable
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not HasBeenDisposed Then
            If disposing Then
                db.Dispose()
                SystrayIcon.Dispose()
                CloseMenu.Dispose()

                If LibraryWatcher IsNot Nothing Then
                    LibraryWatcher.Dispose()
                End If

                If CancelWatching IsNot Nothing Then
                    CancelWatching.Dispose()
                End If
            End If
        End If
        HasBeenDisposed = True
    End Sub

    ' This code added by Visual Basic to correctly implement the disposable pattern.
    Public Sub Dispose() Implements IDisposable.Dispose
        ' Do not change this code.  Put cleanup code in Dispose(disposing As Boolean) above.
        Dispose(True)
    End Sub

#End Region

End Class