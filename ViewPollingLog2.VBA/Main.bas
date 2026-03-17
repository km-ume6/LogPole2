Attribute VB_Name = "Main"
Option Explicit
Option Private Module

'Public DlItems As Dictionary
Public DaysList As Collection

'---------------------------------------------------------------
' 機械名一覧をデータベースから取得し、Excelテーブル「MachineNames」に設定するサブルーチンです。
'
' 1. 名前付き範囲「MachineName」をクリアします。
' 2. DatabaseUtilityオブジェクトを使用してデータベース接続を開きます。
' 3. 「Logging」テーブルから重複しない機械名を取得します。
' 4. Excelテーブル「MachineNames」が存在する場合、既存データをクリアし、
'    取得した機械名を1行ずつ追加します。
' 5. 最後にデータベース接続を閉じます。
'
' 引数: なし
' 戻り値: なし
'---------------------------------------------------------------
Sub SetMachineNames()
    Range("MachineName") = ""
    
    With New DatabaseUtility
        .OpenConnection
        
        Dim params As New Collection
        Dim rs As ADODB.Recordset
        
        Set rs = .ExecuteSelectQuery("SELECT MachineName FROM Logging2 GROUP BY MachineName ORDER BY MachineName", params)
        
        Dim tbl As ListObject: Set tbl = GetListObject("MachineNames")
        If Not tbl Is Nothing Then
            ClearData tbl
            
            ' レコードセットを配列に変換
            If Not rs.EOF Then
                Dim dataArray As Variant
                dataArray = rs.GetRows() ' 全データを2次元配列で取得
                
                ' 配列を転置（行と列を入れ替え）してテーブルに適した形にする
                Dim rowCount As Long: rowCount = UBound(dataArray, 2) + 1
                Dim tableData() As Variant
                ReDim tableData(0 To rowCount - 1, 0 To 0) ' 行数 x 1列（機械名のみ）
                
                Dim i As Long
                For i = 0 To rowCount - 1
                    tableData(i, 0) = dataArray(0, i)
                Next i
                
                ' テーブルのサイズを調整してから一括でデータを設定
                If rowCount > 0 Then
                    tbl.Resize tbl.Range.Resize(rowCount + 1, 1) ' ヘッダー行+データ行
                    tbl.DataBodyRange.Value = tableData
                End If
            End If
        End If
        rs.Close
        .CloseConnection
    End With
End Sub

Sub SetDateRange(machineName As String)
    InitializeMacro
    
    If machineName = "" Then
        Range("BeginDT") = ""
        Range("EndDT") = ""
    Else
        With New DatabaseUtility
            .OpenConnection
            
            Dim params As New Collection
            params.Add machineName
            'params.Add 273
            
            Dim rs As ADODB.Recordset
            
            'Set rs = .ExecuteSelectQuery("SELECT MIN([DateTime]), MAX([DateTime]) FROM Logging WHERE MachineName = ? AND Temp > ?", params)
            Set rs = .ExecuteSelectQuery("SELECT MIN([DateTime]), MAX([DateTime]) FROM Logging2 WHERE MachineName = ?", params)
            
            If IsNull(rs(0)) Or IsNull(rs(1)) Then
                Range("BeginDT") = ""
                Range("EndDT") = ""
            Else
                Dim beginDate As Date: beginDate = rs(0)
                Dim endDate As Date: endDate = rs(1)
                
                Range("BeginDT") = beginDate
                Range("EndDT") = endDate
                
                If DateDiff("d", beginDate, endDate) > 7 Then
                    Range("BeginChart") = DateAdd("d", -7, endDate)
                Else
                    Range("BeginChart") = beginDate
                End If
    
                Range("EndChart") = endDate
            End If
            
            rs.Close
        
            .CloseConnection
        End With
    End If
End Sub

Sub QueryDatabase()
    'Call QueryDatabase1
    Call QueryDatabase2
End Sub

Sub QueryDatabase2()
    If Range("MachineName") = "" Or IsValidDateTime(Range("BeginChart")) <> True Or IsValidDateTime(Range("EndChart")) <> True Then
        Exit Sub
    End If
    
    With New DatabaseUtility
        .OpenConnection
        
        Dim params As New Collection
        params.Add CStr(Range("MachineName"))
        
        Dim columns As New Collection
        Dim sqlPart As String, s As String, s_sub As String
        
        columns.Add "DateTime"
        columns.Add "温度@" & Range("MachineName")
        
        ' [Only Temp]以外のUnitNameがあれば[Only Temp]を除くことができる！
        Dim FlagDeleteOnlyTemp As Boolean: FlagDeleteOnlyTemp = False
        Dim rs As ADODB.Recordset
        Set rs = .ExecuteSelectQuery("SELECT UnitName FROM Logging2 WHERE MachineName = ? GROUP BY UnitName ORDER BY UnitName", params)
        Do Until rs.EOF
            If Not IsNull(rs(0)) Then
                If rs(0) <> "Only Temp" Then
                    FlagDeleteOnlyTemp = True
                    Exit Do
                End If
            End If
            rs.MoveNext
        Loop
        
        sqlPart = " MAX(Temp) AS [温度],"
        Set rs = .ExecuteSelectQuery("SELECT UnitName FROM Logging2 WHERE MachineName = ? GROUP BY UnitName ORDER BY UnitName", params)
        ClearCollection params
        
        Do Until rs.EOF
            If Not IsNull(rs(0)) Then
                If Not (FlagDeleteOnlyTemp And rs(0) = "Only Temp") Then
                    s_sub = rs(0)
                    
                    s = "電圧@" & s_sub
                    columns.Add s
                    params.Add CStr(s_sub)
                    sqlPart = sqlPart & " SUM(CASE WHEN UnitName = ? THEN Volt ELSE 0 END) AS [" & s & "],"
                    
                    s = "電流@" & s_sub
                    columns.Add s
                    params.Add CStr(s_sub)
                    sqlPart = sqlPart & " SUM(CASE WHEN UnitName = ? THEN Amp ELSE 0 END) AS [" & s & "],"
                End If
            End If
            
            rs.MoveNext
        Loop
        
        Dim tbl As ListObject: Set tbl = GetListObject("LoggingData2")
        If tbl Is Nothing Then Exit Sub
        
        ' 列名を設定
        Dim i As Integer
        For i = 1 To tbl.ListColumns.Count
            tbl.HeaderRowRange.Cells(1, i).Value = ""
        Next i
        For i = 1 To columns.Count
            tbl.HeaderRowRange.Cells(1, i).Value = columns(i)
        Next i
        
        Dim sql As String
        sql = "SELECT [DateTime]," & RemoveTrailingComma(sqlPart) & " FROM Logging2 WHERE MachineName = ? AND [DateTime] >= ? AND [DateTime] <= ? GROUP BY [DateTime] ORDER BY [DateTime]"
        params.Add Range("MachineName").text: params.Add CDate(Range("BeginChart").text): params.Add CDate(Range("EndChart").text & ":59")

        Set rs = .ExecuteSelectQuery(sql, params)
        
        ClearData tbl
        
        ' レコードセットを配列に変換して一括処理（高速化）
        If Not rs.EOF Then
            Dim dataArray As Variant
            dataArray = rs.GetRows() ' 全データを2次元配列で取得
            
            ' 配列を転置（行と列を入れ替え）してテーブルに適した形にする
            Dim rowCount As Long: rowCount = UBound(dataArray, 2) + 1
            Dim colCount As Long: colCount = UBound(dataArray, 1) + 1
            Dim tableData() As Variant
            ReDim tableData(0 To rowCount - 1, 0 To colCount - 1)
            
            Dim r As Long, c As Long
            For r = 0 To rowCount - 1
                For c = 0 To colCount - 1
                    ' -273未満の値をフィルタリング（温度以外の列のみ）
                    If Not (c > 0 And IsNumeric(dataArray(c, r)) And dataArray(c, r) < -273) Then
                        tableData(r, c) = dataArray(c, r)
                    Else
                        tableData(r, c) = Empty
                    End If
                Next c
            Next r
            
            ' テーブルのサイズを調整してから一括でデータを設定
            If rowCount > 0 Then
                tbl.Resize tbl.Range.Resize(rowCount + 1, colCount) ' ヘッダー行+データ行
                tbl.DataBodyRange.Value = tableData
            End If
        End If
        
        rs.Close
        .CloseConnection
    End With

    SetChartSourceToTable
End Sub

Sub QueryDatabase1()
    If Range("MachineName") = "" Or IsValidDateTime(Range("BeginDT")) <> True Or IsValidDateTime(Range("EndDT")) <> True Then
        Exit Sub
    End If
    
    With New DatabaseUtility
        .OpenConnection
        
        Dim params As New Collection
        params.Add Range("MachineName")
        
        Dim rs As ADODB.Recordset
        
        Set rs = .ExecuteSelectQuery("SELECT UnitName FROM Logging2 WHERE MachineName = ? GROUP BY UnitName ORDER BY UnitName", params)
        
        Dim columns As New Collection
        Dim sqlPart As String, s As String
        
        ClearCollection params
        columns.Add "DateTime"
        
        sqlPart = ""
        Do Until rs.EOF
            s = "電圧@" & CStr(rs(0)):
            columns.Add s
            params.Add CStr(rs(0))
            sqlPart = sqlPart & " SUM(CASE WHEN UnitName = ? THEN Volt ELSE 0 END) AS [" & s & "],"
            
            s = "電流@" & CStr(rs(0))
            columns.Add s
            params.Add CStr(rs(0))
            sqlPart = sqlPart & " SUM(CASE WHEN UnitName = ? THEN Amp ELSE 0 END) AS [" & s & "],"
            
            rs.MoveNext
        Loop
        
        Dim tbl As ListObject: Set tbl = GetListObject("LoggingData")
        If tbl Is Nothing Then Exit Sub
        
        ' 列名を設定
        Dim i As Integer
        For i = 1 To tbl.ListColumns.Count
            tbl.HeaderRowRange.Cells(1, i).Value = ""
        Next i
        For i = 1 To columns.Count
            tbl.HeaderRowRange.Cells(1, i).Value = columns(i)
        Next i
        
        Dim sql As String
        sql = "SELECT [DateTime]," & RemoveTrailingComma(sqlPart) & " FROM Logging2 WHERE MachineName = ? AND [DateTime] >= ? AND [DateTime] <= ? GROUP BY [DateTime] ORDER BY [DateTime]"
        params.Add Range("MachineName").text: params.Add CDate(Range("BeginChart").text): params.Add CDate(Range("EndChart").text)

        Set rs = .ExecuteSelectQuery(sql, params)
        
        ClearData tbl
        
        ' デバッグ: レコード数を確認
        Debug.Print "QueryDatabase1: レコードセット確認開始"
        
        ' 一時的に元の方法でデータ追加（問題切り分けのため）
        Dim newRow As ListRow
        Dim recordCount As Long: recordCount = 0
        Do Until rs.EOF
            Set newRow = tbl.ListRows.Add
            For i = 0 To rs.Fields.Count - 1
                newRow.Range(1, 1 + i).Value = rs.Fields(i).Value
            Next i
            recordCount = recordCount + 1
            rs.MoveNext
        Loop
        
        Debug.Print "QueryDatabase1: 追加されたレコード数 = " & recordCount
        
        rs.Close
        .CloseConnection
    End With

    SetChartSourceToTable
End Sub

Sub SetChartSourceToTable()
    'Call SetChartSourceToTable1
    Call SetChartSourceToTable2("LoggingData2", "MyChart")
End Sub

Sub SetChartSourceToTable2(tableName As String, chartName As String)
    Dim wsData As Worksheet
    Dim loLoggingData As ListObject
    Dim myChartObject As ChartObject
    Dim wsChart As Worksheet

    On Error GoTo ErrorHandler
    Set loLoggingData = GetListObject(tableName)
    
    If loLoggingData.DataBodyRange Is Nothing Then Exit Sub
    On Error GoTo 0

    If loLoggingData Is Nothing Then
        MsgBox "指定されたテーブル '" & tableName & "' がシート '" & wsData.Name & "' に見つかりません。", vbExclamation
        Exit Sub
    End If

    Call SetMarkerSizeForAllSeries(chartName, 3)

    Set myChartObject = GetChartObjectByName(chartName)

    If myChartObject Is Nothing Then
        MsgBox "指定されたグラフ '" & chartName & "' がシート '" & wsChart.Name & "' に見つかりません。", vbExclamation
        Exit Sub
    End If

    With myChartObject.Chart
        .SetSourceData Source:=loLoggingData.Range, PlotBy:=xlColumns
        
        ' X軸の最小値・最大値をデータ範囲に合わせる（データが存在する場合のみ）
        If Not loLoggingData.DataBodyRange Is Nothing And loLoggingData.DataBodyRange.Rows.Count > 0 Then
            Dim xValuesArr As Variant: xValuesArr = loLoggingData.ListColumns(1).DataBodyRange.Value
            With .Axes(xlCategory)
                .MinimumScale = CDbl(xValuesArr(LBound(xValuesArr), 1))
                .MaximumScale = CDbl(xValuesArr(UBound(xValuesArr), 1))
                .TickLabels.NumberFormat = "'yy/mm/dd hh:mm"
            End With
        End If
        
        Dim ax As Axis
        Set ax = .Axes(xlValue, xlPrimary)
        ax.MinimumScale = 0
        Set ax = .Axes(xlValue, xlSecondary)
        ax.MinimumScale = 0
        
        Dim ser As Series
        For Each ser In .SeriesCollection
            If InStr(ser.Name, "電流") = 0 Then
                ser.AxisGroup = xlPrimary
            Else
                ser.AxisGroup = xlSecondary
            End If
            
        Next ser
    End With

    Exit Sub

ErrorHandler:
    MsgBox "エラーが発生しました: " & Err.Description, vbCritical
    Set loLoggingData = Nothing
    Set myChartObject = Nothing
End Sub

Sub SetChartSourceToTable1()
    Dim wsData As Worksheet
    Dim loLoggingData As ListObject
    Dim myChartObject As ChartObject
    Dim wsChart As Worksheet
    Dim chartName As String
    chartName = "グラフ1"

    On Error GoTo ErrorHandler
    Set loLoggingData = GetListObject("LoggingData")
    On Error GoTo 0

    If loLoggingData Is Nothing Then
        MsgBox "指定されたテーブル 'LoggingData' がシート '" & wsData.Name & "' に見つかりません。", vbExclamation
        Exit Sub
    End If

    Set myChartObject = GetChartObjectByName("MyChart")

    If myChartObject Is Nothing Then
        MsgBox "指定されたグラフ '" & chartName & "' がシート '" & wsChart.Name & "' に見つかりません。", vbExclamation
        Exit Sub
    End If

    With myChartObject.Chart
        .SetSourceData Source:=loLoggingData.Range, PlotBy:=xlColumns
        
        ' X軸の最小値・最大値をデータ範囲に合わせる（データが存在する場合のみ）
        If Not loLoggingData.DataBodyRange Is Nothing And loLoggingData.DataBodyRange.Rows.Count > 0 Then
            Dim xValuesArr As Variant: xValuesArr = loLoggingData.ListColumns(1).DataBodyRange.Value
            With .Axes(xlCategory)
                .MinimumScale = CDbl(xValuesArr(LBound(xValuesArr), 1))
                .MaximumScale = CDbl(xValuesArr(UBound(xValuesArr), 1))
                .TickLabels.NumberFormat = "'yy/mm/dd hh:mm"
            End With
        End If
    End With

    Exit Sub

ErrorHandler:
    MsgBox "エラーが発生しました: " & Err.Description, vbCritical
    Set loLoggingData = Nothing
    Set myChartObject = Nothing
End Sub

Sub InitializeMacro()
'    Set DlItems = New Dictionary
'    With DlItems
'        .Add "過去１日", -1#
'        .Add "過去２日", -2#
'        .Add "過去３日", -3#
'        .Add "過去１週間", -7
'        .Add "過去２週間", -14#
'        .Add "過去３週間", -21#
'
'        Sheet1.ComboBox1.Clear
'        Dim v As Variant
'        For Each v In DlItems
'            Sheet1.ComboBox1.AddItem v
'        Next v
'    End With
    Set DaysList = New Collection
    With DaysList
        .Add -1
        .Add -2
        .Add -3
        .Add -7
        .Add -14
        .Add -21
    End With
End Sub

Sub ComboBoxChanged()
    Dim rgStart As Range: Set rgStart = Sheet1.Range("BeginChart")
    Dim rgEnd As Range:  Set rgEnd = Sheet1.Range("EndChart")
    
    If DaysList Is Nothing Then InitializeMacro
    
    If IsDate(rgEnd) And Not DaysList Is Nothing Then
        rgStart = rgEnd + DaysList(Sheet1.Range("DaysAgo"))
    End If
End Sub

'---------------------------------------------------------------
' グラフの全シリーズに指定サイズのマーカーを適用するサブルーチンです。
'
' 引数:
'   chartName (String): グラフ名
'   markerSize (Integer): マーカーサイズ（1～40推奨）
'
' 戻り値: なし
'---------------------------------------------------------------
Sub SetMarkerSizeForAllSeries(chartName As String, markerSize As Integer)
    On Error GoTo ErrorHandler
    
    Dim myChartObject As ChartObject
    Dim ser As Series
    
    Set myChartObject = GetChartObjectByName(chartName)
    
    If myChartObject Is Nothing Then
        MsgBox "指定されたグラフ '" & chartName & "' が見つかりません。", vbExclamation
        Exit Sub
    End If
    
    With myChartObject.Chart
        For Each ser In .SeriesCollection
            With ser
                .markerSize = markerSize
            End With
        Next ser
    End With
    
    Exit Sub

ErrorHandler:
    MsgBox "エラーが発生しました: " & Err.Description, vbCritical
End Sub

