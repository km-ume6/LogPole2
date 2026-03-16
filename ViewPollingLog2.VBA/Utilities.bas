Attribute VB_Name = "Utilities"
Option Explicit
Option Private Module

Function GetListObject(tableName As String) As ListObject
    Dim ws As Worksheet
    Dim tbl As ListObject
    Dim found As Boolean: found = False

    For Each ws In ThisWorkbook.Worksheets
        For Each tbl In ws.ListObjects
            If tbl.Name = tableName Then
                found = True
                Set GetListObject = tbl
                Exit For
            End If
        Next tbl
        If found Then Exit For
    Next ws
End Function

Sub ClearData(tbl As ListObject)
    If Not tbl.DataBodyRange Is Nothing Then
        tbl.DataBodyRange.Rows.Delete
    End If
End Sub

Function IsCellNamed(targetCell As Range, targetName As String) As Boolean
    Dim nm As Name
    Dim namedRange As Range

    IsCellNamed = False                          ' 初期値

    For Each nm In ThisWorkbook.Names
        On Error Resume Next
        Set namedRange = Range(nm.RefersTo)
        On Error GoTo 0

        If Not namedRange Is Nothing Then
            If targetCell.Address = namedRange.Address Then
                If nm.Name = targetName Or nm.Name Like "*" & targetName Then
                    IsCellNamed = True
                    Exit Function
                End If
            End If
        End If
    Next nm
End Function

Function IsValidDateTime(inputStr As String) As Boolean
    On Error GoTo InvalidDate
    Dim tempDate As Date
    tempDate = CDate(inputStr)
    IsValidDateTime = True
    Exit Function

InvalidDate:
    IsValidDateTime = False
    MsgBox inputStr & " は日時文字列として正しくないです！"
End Function

Sub AdjustTableColumnsToMatchCollection(tblName As String, columnNames As Collection)
    Dim tbl As ListObject
    Set tbl = GetListObject(tblName)
    If tbl Is Nothing Then Exit Sub
    
    Dim i As Long
    Dim currentColCount As Long
    Dim targetColCount As Long

    targetColCount = columnNames.Count
    currentColCount = tbl.ListColumns.Count

    ' 列数が多い場合：余分な列を削除
    If currentColCount > targetColCount Then
        For i = currentColCount To targetColCount + 1 Step -1
            tbl.ListColumns(i).Delete
        Next i
    End If

    ' 列数が少ない場合：不足分を追加
    If currentColCount < targetColCount Then
        For i = currentColCount + 1 To targetColCount
            tbl.ListColumns.Add
        Next i
    End If

    ' 列名を設定
    For i = 1 To targetColCount
        tbl.HeaderRowRange.Cells(1, i).Value = columnNames(i)
    Next i
End Sub

Sub ClearCollection(col As Collection)
    Do While col.Count > 0
        col.Remove 1
    Loop
End Sub

Function RemoveTrailingComma(text As String) As String
    If Right(text, 1) = "," Then
        RemoveTrailingComma = Left(text, Len(text) - 1)
    Else
        RemoveTrailingComma = text
    End If
End Function

Function GetChartObjectByName(ByVal chartName As String, Optional ByVal targetSheet As Worksheet) As ChartObject
    Dim co As ChartObject
    Dim ws As Worksheet

    ' 対象シートが指定されていない場合は、アクティブシートを使用
    If targetSheet Is Nothing Then
        ' アクティブシートがワークシートであることを確認
        If TypeOf ActiveSheet Is Worksheet Then
            Set ws = ActiveSheet
        Else
            ' アクティブシートがワークシートでない場合はエラー
            MsgBox "対象シートが指定されていないか、アクティブシートがワークシートではありません。", vbExclamation
            Set GetChartObjectByName = Nothing
            Exit Function
        End If
    Else
        Set ws = targetSheet
    End If

    ' シート内のすべてのChartObjectをループして名前をチェック
    For Each co In ws.ChartObjects
        If co.Name = chartName Then
            Set GetChartObjectByName = co        ' 名前が一致するChartObjectを返す
            Exit Function                        ' 見つかったので関数を終了
        End If
    Next co

    ' 見つからなかった場合はNothingを返す
    Set GetChartObjectByName = Nothing

End Function

