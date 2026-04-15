Imports System.Data

Public Class SimpleClass
    Inherits System.Windows.Forms.Form

    Public Property Name As String

    Public Sub DoWork()
        Dim x As Integer = 1
    End Sub

    Public Function GetValue() As Integer
        Return 42
    End Function
End Class
