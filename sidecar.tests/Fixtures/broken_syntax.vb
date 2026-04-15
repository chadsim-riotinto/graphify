Imports System.Data

Public Class BrokenClass
    Public Sub ValidMethod()
        Dim x As Integer = 1
    End Sub

    Public Sub InvalidMethod()
        This is not valid VB.NET syntax at all
        Another broken line here
    End Sub

    Public Function AnotherValidMethod() As String
        Return "works"
    End Function
End Class
