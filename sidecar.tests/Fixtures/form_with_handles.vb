Imports System.Windows.Forms

Public Class TestForm
    Inherits System.Windows.Forms.Form

    Friend WithEvents btnOk As System.Windows.Forms.Button
    Friend WithEvents btnCancel As System.Windows.Forms.Button

    Public Sub New()
        MyBase.New()
        InitializeComponent()
    End Sub

    Private Sub btnOk_Click(ByVal sender As Object, ByVal e As EventArgs) Handles btnOk.Click
        Me.Close()
    End Sub

    Private Sub btnCancel_Click(ByVal sender As Object, ByVal e As EventArgs) Handles btnCancel.Click
        Me.Close()
    End Sub

    Private Sub InitializeComponent()
        Me.btnOk = New Button()
        Me.btnCancel = New Button()
    End Sub
End Class
