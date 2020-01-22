''' <summary>
''' Type that exists solely to have something to throw into the grid view in <see cref="MainWindow"/>.
''' </summary>
Public Structure TagRow
    Public Property Name As String
    Public Property Value As String

    Public Sub New(ByVal Name As String, ByVal Value As String)
        Me.Name = Name
        Me.Value = Value
    End Sub
End Structure