Imports System.IO

''' <summary>
''' Basic tag information about a music track.
''' </summary>
Public Class Track

    Public Property Filename As String
    Public Property Title As String
    Public Property Artist As String
    Public Property AlbumArtist As String
    Public Property Album As String
    Public Property Year As UInteger
    Public Property TrackNumber As UInteger
    Public Property TrackCount As UInteger
    Public Property DiscNumber As UInteger
    Public Property DiscCount As UInteger

    Public Property ReplayGainAlbumGain As String
    Public Property ReplayGainTrackGain As String

    Public Property SyncedToDB As Boolean = False

    ''' <summary>
    ''' Loads track information from the disk.
    ''' </summary>
    ''' <param name="FileName">Track file name to load.</param>
    ''' <returns>Track information loaded from disk.</returns>
    Public Shared Function Load(ByVal FileName As String) As Track
        Try
            If (File.GetAttributes(FileName) And FileAttributes.Hidden) <> 0 Then
                ' file is hidden, skip
                Return Nothing
            End If

            Using tagFile = TagLib.File.Create(FileName)
                Dim ReplayGainAlbumGain As String = Nothing
                Dim ReplayGainTrackGain As String = Nothing

                If tagFile.TagTypes.HasFlag(TagLib.TagTypes.Apple) Then
                    Dim InnerTag As TagLib.Mpeg4.AppleTag = tagFile.GetTag(TagLib.TagTypes.Apple)
                    ' the name tags for comment fields are stored with four leading null bytes, and taglib# won't trim them when looking them up
                    ReplayGainAlbumGain = InnerTag.GetDashBox(vbNullChar & vbNullChar & vbNullChar & vbNullChar & "com.apple.iTunes",
                                                              vbNullChar & vbNullChar & vbNullChar & vbNullChar & "replaygain_album_gain")
                    ReplayGainTrackGain = InnerTag.GetDashBox(vbNullChar & vbNullChar & vbNullChar & vbNullChar & "com.apple.iTunes",
                                                              vbNullChar & vbNullChar & vbNullChar & vbNullChar & "replaygain_track_gain")
                ElseIf tagFile.TagTypes.HasFlag(TagLib.TagTypes.Id3v2) Then
                    Dim InnerTag As TagLib.Id3v2.Tag = tagFile.GetTag(TagLib.TagTypes.Id3v2)
                    For Each TextTag In InnerTag.GetFrames(Of TagLib.Id3v2.UserTextInformationFrame)(TagLib.ByteVector.FromString("TXXX", TagLib.StringType.UTF8))
                        If TextTag.Description = "replaygain_album_gain" Then
                            ReplayGainAlbumGain = TextTag.Text.First()
                        ElseIf TextTag.Description = "replaygain_track_gain" Then
                            ReplayGainTrackGain = TextTag.Text.First()
                        End If
                    Next
                Else
                    Debug.Write($"track file has unknown tag type: {tagFile.TagTypes} at file {FileName}")
                End If

                Return New Track With {
                    .Filename = FileName,
                    .Title = tagFile.Tag.Title,
                    .Artist = tagFile.Tag.FirstPerformer,
                    .AlbumArtist = tagFile.Tag.FirstAlbumArtist,
                    .Album = tagFile.Tag.Album,
                    .Year = tagFile.Tag.Year,
                    .TrackNumber = tagFile.Tag.Track,
                    .TrackCount = tagFile.Tag.TrackCount,
                    .DiscNumber = tagFile.Tag.Disc,
                    .DiscCount = tagFile.Tag.DiscCount,
                    .ReplayGainAlbumGain = ReplayGainAlbumGain,
                    .ReplayGainTrackGain = ReplayGainTrackGain,
                    .SyncedToDB = False
                }
            End Using
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

#Region "display/comparison functions"

    ''' <summary>
    ''' Converts this <see cref="Track"/> into a list of <see cref="TagRow"/> fit for display in
    ''' <see cref="MainWindow.trackInfoGrid"/>. Converts default values for numeric fields to
    ''' <code>"(..)"</code>.
    ''' </summary>
    ''' <returns>A set of <see cref="TagRow"/> ready for display.</returns>
    Public Function AsRows() As List(Of TagRow)
        Dim displayYear As String
        Dim displayTrackNumber As String
        Dim displayTrackCount As String
        Dim displayDiscNumber As String
        Dim displayDiscCount As String

        If Year = 0 Then
            displayYear = "(..)"
        Else
            displayYear = CStr(Year)
        End If

        If TrackNumber = 0 Then
            displayTrackNumber = "(..)"
        Else
            displayTrackNumber = CStr(TrackNumber)
        End If

        If TrackCount = 0 Then
            displayTrackCount = "(..)"
        Else
            displayTrackCount = CStr(TrackCount)
        End If

        If DiscNumber = 0 Then
            displayDiscNumber = "(..)"
        Else
            displayDiscNumber = CStr(DiscNumber)
        End If

        If DiscCount = 0 Then
            displayDiscCount = "(..)"
        Else
            displayDiscCount = CStr(DiscCount)
        End If

        Return New List(Of TagRow) From {
            New TagRow("Artist", Artist),
            New TagRow("Album Artist", AlbumArtist),
            New TagRow("Album", Album),
            New TagRow("Year", displayYear),
            New TagRow("Title", Title),
            New TagRow("Track Number", $"{displayTrackNumber}/{displayTrackCount}"),
            New TagRow("Disc Number", $"{displayDiscNumber}/{displayDiscCount}"),
            New TagRow("ReplayGain Album Gain", ReplayGainAlbumGain),
            New TagRow("ReplayGain Track Gain", ReplayGainTrackGain)
        }
    End Function

    ''' <summary>
    ''' Compares this <see cref="Track"/> with the given one, and modifies any fields that differ
    ''' between them to display <code>"(..)"</code> when displayed with <see cref="Track.AsRows()"/>.
    ''' </summary>
    ''' <param name="other">Track information to compare with the current track.</param>
    Public Sub CombineWith(other As Track)
        If Me.Title <> other.Title Then
            Me.Title = "(..)"
        End If

        If Me.Artist <> other.Artist Then
            Me.Artist = "(..)"
        End If

        If Me.AlbumArtist <> other.AlbumArtist Then
            Me.AlbumArtist = "(..)"
        End If

        If Me.Album <> other.Album Then
            Me.Album = "(..)"
        End If

        If Me.Year <> other.Year Then
            Me.Year = 0
        End If

        If Me.TrackNumber <> other.TrackNumber Then
            Me.TrackNumber = 0
        End If

        If Me.TrackCount <> other.TrackCount Then
            Me.TrackCount = 0
        End If

        If Me.DiscNumber <> other.DiscNumber Then
            Me.DiscNumber = 0
        End If

        If Me.DiscCount <> other.DiscCount Then
            Me.DiscCount = 0
        End If

        If Me.ReplayGainAlbumGain <> other.ReplayGainAlbumGain Then
            Me.ReplayGainAlbumGain = "(..)"
        End If

        If Me.ReplayGainTrackGain <> other.ReplayGainTrackGain Then
            Me.ReplayGainTrackGain = "(..)"
        End If
    End Sub

#End Region

#Region "operators"

    Public Shared Operator =(left As Track, right As Track) As Boolean
        Return left.Title = right.Title _
            AndAlso left.Artist = right.Artist _
            AndAlso left.AlbumArtist = right.AlbumArtist _
            AndAlso left.Album = right.Album _
            AndAlso left.Year = right.Year _
            AndAlso left.TrackNumber = right.TrackNumber _
            AndAlso left.TrackCount = right.TrackCount _
            AndAlso left.DiscNumber = right.DiscNumber _
            AndAlso left.DiscCount = right.DiscCount _
            AndAlso left.ReplayGainAlbumGain = right.ReplayGainAlbumGain _
            AndAlso left.ReplayGainTrackGain = right.ReplayGainTrackGain
    End Operator

    Public Shared Operator <>(left As Track, right As Track) As Boolean
        Return Not left = right
    End Operator

#End Region

End Class
