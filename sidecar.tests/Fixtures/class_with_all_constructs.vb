Imports System.Data
Imports System.IO

Public Class AllConstructs
    Inherits Object

    Friend WithEvents timer1 As System.Timers.Timer

    Private _count As Integer

    Public Property Count As Integer
        Get
            Return _count
        End Get
        Set(value As Integer)
            _count = value
        End Set
    End Property

    Public ReadOnly Property IsActive As Boolean
        Get
            Return _count > 0
        End Get
    End Property

    Public Sub New()
        _count = 0
    End Sub

    Public Sub Increment()
        _count += 1
        NotifyChange()
    End Sub

    Public Function GetCount() As Integer
        Return _count
    End Function

    Private Sub NotifyChange()
        Console.WriteLine("Changed")
    End Sub

    Private Sub timer1_Tick(sender As Object, e As EventArgs) Handles timer1.Elapsed
        Increment()
    End Sub
End Class

NotInheritable Class SealedHelper
    Public Shared Sub HelperMethod()
        Dim x = 1
    End Sub
End Class
