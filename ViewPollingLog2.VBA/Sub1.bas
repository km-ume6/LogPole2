Attribute VB_Name = "Sub1"
Option Explicit

Sub •Û‘¶ŠúŠÔXV()
    Dim rg As Range: Set rg = Range("MachineName")
    If rg <> "" Then
        SetDateRange (rg.text)
    End If
End Sub

Sub Test()
    Debug.Print Sheet1.Range("DaysAgo")
End Sub

