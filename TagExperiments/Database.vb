Imports System.Threading
''' <summary>
''' Handle for database operations. Maintains a connection to the tag-experiments database and
''' uses it for any calls that synchronize with it.
''' </summary>
Public NotInheritable Class Database
    Implements IDisposable

    Const connString = "Host=localhost;Username=music;Password=music;Database=tag-experiments"

#Region "database connection"

    Private _connection As Npgsql.NpgsqlConnection = Nothing

    ''' <summary>
    ''' Fetches the <see cref="Npgsql.NpgsqlConnection"/>, or creates one if one does not exist.
    ''' </summary>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns></returns>
    Private Async Function DBConn(Optional CancellationToken As CancellationToken = Nothing) As Task(Of Npgsql.NpgsqlConnection)
        If _connection Is Nothing Then
            _connection = New Npgsql.NpgsqlConnection(connString)
            Await _connection.OpenAsync(CancellationToken)
        End If

        Return _connection
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _connection.Dispose()
    End Sub

#End Region

#Region "track maintenance"

    ''' <summary>
    ''' Loads a track with the given file name from the tag-experiments database.
    ''' </summary>
    ''' <param name="FileName">Track file to load.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>
    ''' A <see cref="Track"/> if one exists in the database with the given <paramref name="FileName"/>,
    ''' or <code>Nothing</code> if not.
    ''' </returns>
    Public Async Function LoadTrack(FileName As String, Optional CancellationToken As CancellationToken = Nothing) As Task(Of Track)
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("
                    SELECT
                        filename,
                        title,
                        artist,
                        album,
                        albumartist,
                        year,
                        tracknumber,
                        tracktotal,
                        discnumber,
                        disctotal
                    FROM tracks
                    WHERE filename = @filename", conn)
            command.Parameters.AddWithValue("filename", FileName)
            Await command.PrepareAsync(CancellationToken)

            Using reader = Await command.ExecuteReaderAsync(CancellationToken)
                If reader.HasRows AndAlso Await reader.ReadAsync(CancellationToken) Then
                    Dim ret As New Track With {
                            .Filename = reader.GetString(0),
                            .Year = reader.GetInt32(5),
                            .TrackNumber = reader.GetInt32(6),
                            .TrackCount = reader.GetInt32(7),
                            .DiscNumber = reader.GetInt32(8),
                            .DiscCount = reader.GetInt32(9),
                            .SyncedToDB = True
                    }

                    If Not reader.IsDBNull(1) Then
                        ret.Title = reader.GetString(1)
                    End If

                    If Not reader.IsDBNull(2) Then
                        ret.Artist = reader.GetString(2)
                    End If

                    If Not reader.IsDBNull(3) Then
                        ret.Album = reader.GetString(3)
                    End If

                    If Not reader.IsDBNull(4) Then
                        ret.AlbumArtist = reader.GetString(4)
                    End If

                    Return ret
                Else
                    Return Nothing
                End If
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Loads a track from the tag-experiments database, falling back to loading from disk if no
    ''' database record matches the given <paramref name="FileName"/>.
    ''' </summary>
    ''' <param name="FileName">Track file to load.</param>
    ''' <param name="SaveToDB">
    ''' If <code>True</code>, adds track information to the database if it doesn't exist. Defaults to <code>True</code>.
    ''' </param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>Tag information for the corresponding <see cref="Track"/>.</returns>
    Public Async Function LoadTrackOrUseDisk(FileName As String,
                                             Optional SaveToDB As Boolean = True,
                                             Optional CancellationToken As CancellationToken = Nothing) As Task(Of Track)
        Dim ret = Await LoadTrack(FileName, CancellationToken)
        If ret Is Nothing Then
            ret = Track.Load(FileName)

            If SaveToDB And ret IsNot Nothing Then
                Await SaveTrack(ret, CancellationToken)
            End If
        End If

        Return ret
    End Function

    ''' <summary>
    ''' Saves the given <see cref="Track"/> to the database.
    ''' </summary>
    ''' <param name="input">Track information to save.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>Handle representing the asynchronous operation.</returns>
    Public Async Function SaveTrack(input As Track, Optional CancellationToken As CancellationToken = Nothing) As Task
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("
                INSERT INTO tracks
                (filename, title, artist, album, albumartist, year, tracknumber, tracktotal, discnumber, disctotal)
                VALUES
                (@filename, @title, @artist, @album, @albumartist, @year, @tracknumber, @tracktotal, @discnumber, @disctotal)
                ON CONFLICT (filename) DO UPDATE
                SET title = EXCLUDED.title,
                    artist = EXCLUDED.artist,
                    album = EXCLUDED.album,
                    albumartist = EXCLUDED.albumartist,
                    year = EXCLUDED.year,
                    tracknumber = EXCLUDED.tracknumber,
                    tracktotal = EXCLUDED.tracktotal,
                    discnumber = EXCLUDED.discnumber,
                    disctotal = EXCLUDED.disctotal", conn)
            command.Parameters.AddWithValue("filename", NpgsqlTypes.NpgsqlDbType.Text, input.Filename)
            command.Parameters.AddWithValue("title", NpgsqlTypes.NpgsqlDbType.Text, If(input.Title, DBNull.Value))
            command.Parameters.AddWithValue("artist", NpgsqlTypes.NpgsqlDbType.Text, If(input.Artist, DBNull.Value))
            command.Parameters.AddWithValue("album", NpgsqlTypes.NpgsqlDbType.Text, If(input.Album, DBNull.Value))
            command.Parameters.AddWithValue("albumartist", NpgsqlTypes.NpgsqlDbType.Text, If(input.AlbumArtist, DBNull.Value))
            command.Parameters.AddWithValue("year", CInt(input.Year))
            command.Parameters.AddWithValue("tracknumber", CInt(input.TrackNumber))
            command.Parameters.AddWithValue("tracktotal", CInt(input.TrackCount))
            command.Parameters.AddWithValue("discnumber", CInt(input.DiscNumber))
            command.Parameters.AddWithValue("disctotal", CInt(input.DiscCount))

            Await command.PrepareAsync(CancellationToken)

            Await command.ExecuteNonQueryAsync(CancellationToken)

            input.SyncedToDB = True
        End Using
    End Function

    ''' <summary>
    ''' Compares the given <see cref="Track"/> to see whether it differs between the disk and the database.
    ''' </summary>
    ''' <param name="input">Track information to load.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>
    ''' <code>Nothing</code> if no track information exists in the database, otherwise a value
    ''' representing whether tag information differs between the disk and database.
    ''' </returns>
    Public Async Function CompareTrackToDB(input As Track, Optional CancellationToken As CancellationToken = Nothing) As Task(Of Boolean?)
        If input Is Nothing Then
            Throw New ArgumentNullException("input")
        End If

        Dim conn = Await DBConn(CancellationToken)
        Dim compareTrack As Track

        If input.SyncedToDB Then
            ' this track reflects the database. load from disk and compare

            compareTrack = Track.Load(input.Filename)
        Else
            ' this track reflects the track on disk. load from the db and compare

            compareTrack = Await LoadTrack(input.Filename, CancellationToken)
        End If

        If compareTrack Is Nothing Then
            ' if the file in question isn't even loaded, then bail
            Return Nothing
        End If

        Return input = compareTrack
    End Function

    ''' <summary>
    ''' Compares the track at the given <paramref name="FileName"/> to the record in the database.
    ''' If the database differs from the disk, the database record is updated with information from
    ''' disk.
    ''' </summary>
    ''' <param name="FileName">Location of a track on disk to load.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>A handle representing the asynchronous operation.</returns>
    Public Async Function PushTrackToDB(FileName As String, Optional CancellationToken As CancellationToken = Nothing) As Task
        Dim DiskTrack = Track.Load(FileName)

        If DiskTrack Is Nothing Then
            ' sometimes, iTunes still has a file lock on the file when we try to load it. throw a special exception
            ' so the event handler can queue the load for later.
            Throw New FileNotReadyException($"{FileName} could not be loaded")
        End If

        Dim CompareResult = Await CompareTrackToDB(DiskTrack, CancellationToken)
        If CompareResult Is Nothing OrElse CompareResult = False Then
            Await SaveTrack(DiskTrack, CancellationToken)
        End If
    End Function

    ''' <summary>
    ''' Deletes the track record with the given <paramref name="FileName"/> from the database.
    ''' </summary>
    ''' <param name="FileName">File name of the track to delete.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>A handle to the asynchronous operation.</returns>
    Public Async Function DeleteTrack(FileName As String, Optional CancellationToken As CancellationToken = Nothing) As Task
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("DELETE FROM tracks WHERE filename = @filename", conn)
            command.Parameters.AddWithValue("filename", FileName)
            Await command.PrepareAsync(CancellationToken)

            Await command.ExecuteNonQueryAsync(CancellationToken)
        End Using
    End Function

    ''' <summary>
    ''' Updates the database record for <paramref name="OldName"/> to point to <paramref name="NewName"/> instead.
    ''' </summary>
    ''' <param name="OldName">Full path to a track with an existing record in the database.</param>
    ''' <param name="NewName">Full path to the file that <paramref name="OldName"/> has been renamed to.</param>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>A handle to the asynchronous operation.</returns>
    Public Async Function RenameTrackFile(OldName As String, NewName As String, Optional CancellationToken As CancellationToken = Nothing) As Task
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("UPDATE tracks SET filename = @newname WHERE filename = @oldname", conn)
            command.Parameters.AddWithValue("newname", NewName)
            command.Parameters.AddWithValue("oldname", OldName)
            Await command.PrepareAsync(CancellationToken)

            Await command.ExecuteNonQueryAsync(CancellationToken)
        End Using
    End Function

#End Region

#Region "queries"

    ''' <summary>
    ''' Returns the list of albums that have multiple "year" tags between the tracks.
    ''' </summary>
    ''' <param name="CancellationToken">
    ''' A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    ''' Defaults to <see cref="CancellationToken.None"/>.
    ''' </param>
    ''' <returns>A list of albums.</returns>
    Public Async Function DiskCorruption(Optional CancellationToken As CancellationToken = Nothing) As Task(Of List(Of AlbumRow))
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("
                SELECT COALESCE(albumartist, artist) AS a_artist, album, MAX(year) AS max_year
                FROM tracks
                WHERE album <> 'Singles'
                GROUP BY a_artist, album
                HAVING COUNT(distinct year) > 1
                ORDER BY a_artist, max_year, album", conn)
            Await command.PrepareAsync(CancellationToken)

            Dim ret As New List(Of AlbumRow)

            Using reader = Await command.ExecuteReaderAsync(CancellationToken)
                While Await reader.ReadAsync(CancellationToken)
                    ret.Add(New AlbumRow With {
                        .AlbumArtist = reader.GetString(0),
                        .Album = reader.GetString(1),
                        .Year = reader.GetInt32(2)
                    })
                End While
            End Using

            Return ret
        End Using
    End Function

    Public Async Function MissingTrackCount(Optional CancellationToken As CancellationToken = Nothing) As Task(Of List(Of AlbumRow))
        Dim conn = Await DBConn(CancellationToken)

        Using command As New Npgsql.NpgsqlCommand("
                SELECT COALESCE(albumartist, artist) AS a_artist, album, MAX(year) AS year
                FROM tracks
                WHERE tracktotal = 0
                AND tracknumber <> 0
                GROUP BY a_artist, album
                ORDER BY a_artist, year, album", conn)
            Await command.PrepareAsync(CancellationToken)

            Dim ret As New List(Of AlbumRow)

            Using reader = Await command.ExecuteReaderAsync(CancellationToken)
                While Await reader.ReadAsync(CancellationToken)
                    ret.Add(New AlbumRow With {
                        .AlbumArtist = reader.GetString(0),
                        .Album = reader.GetString(1),
                        .Year = reader.GetInt32(2)
                    })
                End While
            End Using

            Return ret
        End Using
    End Function

#End Region

End Class
