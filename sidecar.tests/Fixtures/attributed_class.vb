Imports System.ComponentModel

<Serializable()>
Public Class AttributedClass
    <Obsolete("Use NewMethod instead")>
    Public Sub OldMethod()
        Dim x As Integer = 1
    End Sub

    Public Sub NewMethod()
        Dim y As Integer = 2
    End Sub
End Class
