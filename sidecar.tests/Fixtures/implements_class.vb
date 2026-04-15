Public Interface IWorker
    Sub DoWork()
End Interface

Public Class Worker
    Implements IWorker

    Public Sub DoWork() Implements IWorker.DoWork
        Dim x As Integer = 1
    End Sub
End Class
