Imports System.Runtime.Serialization

''' <summary>
''' Thrown from <see cref="Database.PushTrackToDB(String, System.Threading.CancellationToken)"/>
''' when a file could not be loaded from disk.
''' </summary>
Public Class FileNotReadyException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, innerException As Exception)
        MyBase.New(message, innerException)
    End Sub

    Protected Sub New(info As SerializationInfo, context As StreamingContext)
        MyBase.New(info, context)
    End Sub
End Class
