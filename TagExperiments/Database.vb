''' <summary>
''' Handle for database operations. Maintains a connection to the tag-experiments database and
''' uses it for any calls that synchronize with it.
''' </summary>
Public Class Database
    Implements IDisposable

    Const connString = "Host=localhost;Username=music;Password=music;Database=tag-experiments"

#Region "database connection"

    Private _connection As Npgsql.NpgsqlConnection = Nothing

    ''' <summary>
    ''' Fetches the <see cref="Npgsql.NpgsqlConnection"/>, or creates one if one does not exist.
    ''' </summary>
    ''' <returns></returns>
    Private Async Function DBConn() As Task(Of Npgsql.NpgsqlConnection)
        If _connection Is Nothing Then
            _connection = New Npgsql.NpgsqlConnection(connString)
            Await _connection.OpenAsync
        End If

        Return _connection
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _connection.Dispose()
    End Sub

#End Region

    ''' <summary>
    ''' Loads a track with the given file name from the tag-experiments database.
    ''' </summary>
    ''' <param name="FileName">Track file to load.</param>
    ''' <returns>
    ''' A <see cref="Track"/> if one exists in the database with the given <paramref name="FileName"/>,
    ''' or <code>Nothing</code> if not.
    ''' </returns>
    Public Async Function LoadTrack(FileName As String) As Task(Of Track)
        Dim conn = Await DBConn()

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
            Await command.PrepareAsync()

            Using reader = Await command.ExecuteReaderAsync()
                If reader.HasRows AndAlso Await reader.ReadAsync Then
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
    ''' <returns>Tag information for the corresponding <see cref="Track"/>.</returns>
    Public Async Function LoadTrackOrUseDisk(FileName As String, Optional SaveToDB As Boolean = True) As Task(Of Track)
        Dim ret = Await LoadTrack(FileName)
        If ret Is Nothing Then
            ret = Track.Load(FileName)

            If SaveToDB And ret IsNot Nothing Then
                Await SaveTrack(ret)
            End If
        End If

        Return ret
    End Function

    ''' <summary>
    ''' Saves the given <see cref="Track"/> to the database.
    ''' </summary>
    ''' <param name="input">Track information to save.</param>
    ''' <returns>Handle representing the asynchronous operation.</returns>
    Public Async Function SaveTrack(input As Track) As Task
        Dim conn = Await DBConn()

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

            Await command.PrepareAsync()

            Await command.ExecuteNonQueryAsync()

            input.SyncedToDB = True
        End Using
    End Function

    ''' <summary>
    ''' Compares the given <see cref="Track"/> to see whether it differs between the disk and the database.
    ''' </summary>
    ''' <param name="input">Track information to load.</param>
    ''' <returns>
    ''' <code>Nothing</code> if no track information exists in the database, otherwise a value
    ''' representing whether tag information differs between the disk and database.
    ''' </returns>
    Public Async Function CompareTrackToDB(input As Track) As Task(Of Boolean?)
        Dim conn = Await DBConn()
        Dim compareTrack As Track

        If input.SyncedToDB Then
            ' this track reflects the database. load from disk and compare

            compareTrack = Track.Load(input.Filename)
        Else
            ' this track reflects the track on disk. load from the db and compare

            compareTrack = Await LoadTrack(input.Filename)
        End If

        If compareTrack Is Nothing Then
            ' if the file in question isn't even loaded, then bail
            Return Nothing
        End If

        Return input = compareTrack
    End Function

End Class
