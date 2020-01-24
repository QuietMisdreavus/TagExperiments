Imports System.ComponentModel
Imports System.IO
Imports WinForms = System.Windows.Forms

Class MainWindow

#Region "fields"

    Private db As Database = New Database()
    Private WithEvents SystrayIcon As New WinForms.NotifyIcon
    Private WithEvents CloseMenu As New WinForms.MenuItem
    Private WithEvents LibraryWatcher As FileSystemWatcher

    Private ManuallyClosing As Boolean = False
    Private ReadyString As String = "Ready."

#End Region

#Region "helper methods"

    Private Sub ToggleButtons(ButtonState As Boolean)
        loadFileButton.IsEnabled = ButtonState
        loadDirButton.IsEnabled = ButtonState
        importDirButton.IsEnabled = ButtonState
    End Sub

    Private Async Function ImportDir(dir As String) As Task
        Dim ImportCount As UInteger = 0
        Dim ProgressCount As UInteger = 0

        StatusBarText.Text = "Loading tracks from library..."
        Me.Cursor = Cursors.Wait
        ToggleButtons(False)
        Await Task.Yield()

        Dim TrackFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        Dim OverallCount = TrackFiles.Count

        For Each FileName In TrackFiles
            ProgressCount += 1
            StatusBarText.Text = $"Loading tracks from library... ({ProgressCount} of {OverallCount})"

            If FileName.EndsWith(".mp3") OrElse FileName.EndsWith(".m4a") Then
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

        Dim CompleteText = $"Imported {ImportCount} (of {ProgressCount}) tracks to database."
        MessageBox.Show(CompleteText)
        StatusBarText.Text = CompleteText

        Me.Cursor = Cursors.Arrow
        ToggleButtons(True)
    End Function

    ' TODO: replace other file extension checks with this function
    Private Function IsMusicFile(FileName As String) As Boolean
        Return FileName.EndsWith(".mp3") OrElse FileName.EndsWith(".m4a")
    End Function

#End Region

#Region "button handlers"

    Private Async Sub loadFileButton_Click(sender As Object, e As RoutedEventArgs) Handles loadFileButton.Click
        Dim pickFile As New WinForms.OpenFileDialog With {
            .Filter = "Music files|*.mp3;*.m4a"
        }
        StatusBarText.Text = "Please select a track."
        Me.Cursor = Cursors.Wait
        ToggleButtons(False)
        If pickFile.ShowDialog() = WinForms.DialogResult.OK Then
            StatusBarText.Text = "Loading track information..."

            Dim fileName = pickFile.FileName
            Dim track = Await db.LoadTrackOrUseDisk(fileName)

            trackInfoGrid.ItemsSource = track.AsRows()

            Me.Cursor = Cursors.Arrow
            ToggleButtons(True)
        End If

        StatusBarText.Text = ReadyString
    End Sub

    Private Async Sub loadDirButton_Click(sender As Object, e As RoutedEventArgs) Handles loadDirButton.Click
        Dim pickDir As New WinForms.FolderBrowserDialog
        StatusBarText.Text = "Please select a directory to display."
        If pickDir.ShowDialog() = WinForms.DialogResult.OK Then
            StatusBarText.Text = "Loading track information from directory..."
            Me.Cursor = Cursors.Wait
            ToggleButtons(False)

            Dim dir = pickDir.SelectedPath
            Dim showTrack As Track = Nothing

            For Each FileName In Directory.EnumerateFiles(dir)
                If FileName.EndsWith(".mp3") OrElse FileName.EndsWith(".m4a") Then
                    Dim thisTrack = Await db.LoadTrackOrUseDisk(FileName)
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
            ToggleButtons(True)
        Else
            StatusBarText.Text = ReadyString
        End If
    End Sub

    Private Async Sub ImportDirButton_Click(sender As Object, e As RoutedEventArgs) Handles importDirButton.Click
        Dim pickDir As New WinForms.FolderBrowserDialog
        StatusBarText.Text = "Please select a directory to import into the database."
        If pickDir.ShowDialog() = WinForms.DialogResult.OK Then
            Await Me.ImportDir(pickDir.SelectedPath)
        Else
            StatusBarText.Text = ReadyString
        End If
    End Sub

    Private Sub WatchDirButton_Click(sender As Object, e As RoutedEventArgs) Handles WatchDirButton.Click
        Dim PickDir As New WinForms.FolderBrowserDialog
        StatusBarText.Text = "Please select a directory to import and monitor."
        If PickDir.ShowDialog() = WinForms.DialogResult.OK Then
            'Await Me.ImportDir(PickDir.SelectedPath)

            Me.LibraryWatcher = New FileSystemWatcher(PickDir.SelectedPath)
            Me.LibraryWatcher.NotifyFilter = NotifyFilters.LastWrite Or NotifyFilters.FileName Or NotifyFilters.DirectoryName Or NotifyFilters.CreationTime
            Me.LibraryWatcher.IncludeSubdirectories = True
            Me.LibraryWatcher.EnableRaisingEvents = True

            ReadyString = "Watching library directory."
            StatusBarText.Text = ReadyString
        Else
            StatusBarText.Text = ReadyString
        End If
    End Sub

#End Region

#Region "main window events"

    Private Sub MainWindow_Initialized(sender As Object, e As EventArgs) Handles Me.Initialized
        SystrayIcon.Icon = My.Resources.TagExperimentsIcon
        SystrayIcon.Text = "TagExperiments"

        CloseMenu.Text = "E&xit"
        SystrayIcon.ContextMenu = New WinForms.ContextMenu
        SystrayIcon.ContextMenu.MenuItems.Add(CloseMenu)

        SystrayIcon.Visible = True
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

    Private Sub CloseMenu_Click(sender As Object, e As EventArgs) Handles CloseMenu.Click
        ManuallyClosing = True
        Me.Close()
    End Sub

#End Region

#Region "filesystem watcher events"

    Private Sub LibraryWatcher_Changed(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Changed
        If IsMusicFile(e.FullPath) Then
            Debug.Print("file changed: {0}", e.FullPath)
        End If
    End Sub

    Private Sub LibraryWatcher_Renamed(sender As Object, e As RenamedEventArgs) Handles LibraryWatcher.Renamed
        If Directory.Exists(e.FullPath) Then
            RenamedDir(e.OldFullPath, e.FullPath)
        Else
            RenamedFile(e.OldFullPath, e.FullPath)
        End If
    End Sub

    Private Sub LibraryWatcher_Created(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Created
        If IsMusicFile(e.FullPath) Then
            Debug.Print("file created: {0}", e.FullPath)
        End If
    End Sub

    Private Sub LibraryWatcher_Deleted(sender As Object, e As FileSystemEventArgs) Handles LibraryWatcher.Deleted
        If IsMusicFile(e.FullPath) Then
            Debug.Print("file deleted: {0}", e.FullPath)
        End If
    End Sub

    Private Sub RenamedDir(OldName As String, NewName As String)
        Debug.Print("dir renamed: {0} -> {1}", OldName, NewName)
        Dim BasePath = OldName
        For Each SubFile In Directory.EnumerateFiles(NewName)
            Dim OldPath = Path.Combine(BasePath, Path.GetFileName(SubFile))
            RenamedFile(OldPath, SubFile)
        Next
        For Each SubDir In Directory.EnumerateDirectories(NewName)
            Dim OldPath = Path.Combine(BasePath, Path.GetFileName(SubDir))
            RenamedDir(OldPath, SubDir)
        Next
    End Sub

    Private Sub RenamedFile(OldName As String, NewName As String)
        If IsMusicFile(OldName) Then
            Debug.Print("file renamed: {0} -> {1}", OldName, NewName)
        End If
    End Sub

#End Region

End Class