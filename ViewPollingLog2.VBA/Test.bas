Attribute VB_Name = "Test"
Option Explicit
Option Private Module

Sub TestParameterizedQuery()
    Dim db As New DatabaseUtility
    db.OpenConnection
    
    ' SELECT例
    Dim params As New Collection
    params.Add "イ"                               ' 例：UserID
    Dim rs As ADODB.Recordset
    Set rs = db.ExecuteSelectQuery("SELECT * FROM Logging2 WHERE MachineName = ?", params)
    'Set rs = db.ExecuteSelectQuery("SELECT * FROM Logging WHERE MachineName = 'イ'", params)

    Do Until rs.EOF
        Debug.Print rs("Volt")
        rs.MoveNext
    Loop
    rs.Close

    ' UPDATE例
    'Dim updateParams As New Collection
    'updateParams.Add "新しい名前"
    'updateParams.Add 1001

    'Dim affectedRows As Long
    'affectedRows = db.ExecuteNonQuery("UPDATE Users SET UserName = ? WHERE UserID = ?", updateParams)
    'Debug.Print "更新された行数: " & affectedRows

    db.CloseConnection
End Sub

Sub GetDateTime()
    With New DatabaseUtility
        .OpenConnection
        
        Dim params As New Collection
        params.Add "イ"
        
        Dim rs As ADODB.Recordset
        
        Set rs = .ExecuteSelectQuery("SELECT MIN([DateTime]), MAX([DateTime]) FROM Logging2 WHERE MachineName = ?", params)
        
        .DumpRecordset rs
        
        rs.Close
    
        .CloseConnection
    End With
End Sub

Sub GetMachineList()
    With New DatabaseUtility
        .OpenConnection
        
        Dim params As New Collection
        Dim rs As ADODB.Recordset
        
        Set rs = .ExecuteSelectQuery("SELECT MachineName FROM Logging2 GROUP BY MachineName", params)
        .DumpRecordset rs
        rs.Close
    
        .CloseConnection
    End With
End Sub

Sub ModifyMachineNamesTable()
    Dim ws As Worksheet
    Dim tbl As ListObject
    Dim found As Boolean
    found = False

    ' ワークブック内のすべてのシートをループ
    For Each ws In ThisWorkbook.Worksheets
        For Each tbl In ws.ListObjects
            If tbl.Name = "MachineNames" Then
                found = True
                
                ' --- 全件削除 ---
                If Not tbl.DataBodyRange Is Nothing Then
                    tbl.DataBodyRange.Rows.Delete
                End If
                
                ' --- 1件追加 ---
                Dim newRow As ListRow
                Set newRow = tbl.ListRows.Add
                newRow.Range(1, 1).Value = "Machine_A" ' 1列目に値を入力（必要に応じて調整）

                Exit For
            End If
        Next tbl
        If found Then Exit For
    Next ws

    If Not found Then
        MsgBox "テーブル 'MachineNames' が見つかりませんでした。", vbExclamation
    End If
End Sub

Sub WriteViewerLog()
    LogWorkbookOpen
End Sub
