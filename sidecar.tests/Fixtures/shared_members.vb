Public Class SharedExample
    Private Shared _count As Integer

    Public Shared Sub SharedMethod()
        _count += 1
    End Sub

    Public Shared Function SharedFunction() As Integer
        Return _count
    End Function

    Public Shared ReadOnly Property SharedProp As Integer
        Get
            Return _count
        End Get
    End Property

    Public Sub InstanceMethod()
        _count = 0
    End Sub
End Class
