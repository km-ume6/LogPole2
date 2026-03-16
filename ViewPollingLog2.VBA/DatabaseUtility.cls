VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "DatabaseUtility"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private conn As ADODB.Connection

Public ConnectionString As String

' 接続を開く
Public Sub OpenConnection()
    Set conn = New ADODB.Connection
    conn.Open ConnectionString
End Sub

' 接続を閉じる
Public Sub CloseConnection()
    If Not conn Is Nothing Then
        If conn.State = adStateOpen Then conn.Close
        Set conn = Nothing
    End If
End Sub

' パラメータ付きSELECTクエリを実行してRecordsetを返す
Public Function ExecuteSelectQuery(ByVal sql As String, ByVal params As Collection) As ADODB.Recordset
    Dim cmd As ADODB.Command
    Set cmd = New ADODB.Command
    With cmd
        .ActiveConnection = conn
        .CommandText = sql
        .CommandType = adCmdText

        Debug.Print "【SQL実行】" & vbCrLf & sql
        Debug.Print "【パラメータ一覧】"

        Dim p As Variant
        For Each p In params
            Dim param As ADODB.Parameter
            Set param = CreateTypedParameter(cmd, p)
            .Parameters.Append param
            Debug.Print "  値: " & p & " | 型: " & GetVBATypeName(p) & " | サイズ: " & param.size
        Next
    End With

    Set ExecuteSelectQuery = cmd.Execute
End Function

' パラメータ付きINSERT/UPDATEクエリを実行（戻り値：影響を受けた行数）
Public Function ExecuteNonQuery(ByVal sql As String, ByVal params As Collection) As Long
    Dim cmd As ADODB.Command
    Dim recordsAffected As Long                  ' 影響を受けた行数を格納するための変数

    Set cmd = New ADODB.Command

    With cmd
        Set .ActiveConnection = conn             ' conn が有効な ADODB.Connection オブジェクトであることを確認
        .CommandText = sql
        .CommandType = adCmdText

        Debug.Print "【SQL実行】" & vbCrLf & sql
        Debug.Print "【パラメータ一覧】"

        Dim p As Variant
        For Each p In params
            Dim param As ADODB.Parameter
            ' CreateTypedParameter が新しい ADODB.Parameter オブジェクトを作成・返すと仮定
            Set param = CreateTypedParameter(cmd, p)
            .Parameters.Append param
            ' p の値を文字列として Debug.Print に表示
            Debug.Print "  値: " & CStr(p) & " | 型: " & GetVBATypeName(p) & " | サイズ: " & param.size
        Next p                                   ' For Each ループの Next p を明示的に
    End With

    ' コマンドを実行し、影響を受けた行数を recordsAffected に格納
    cmd.Execute recordsAffected

    ' 影響を受けた行数を関数の戻り値に設定
    ExecuteNonQuery = recordsAffected

    ' オブジェクトのクリーンアップ
    Set cmd = Nothing
End Function

' 型とサイズを自動判定してパラメータを作成
Private Function CreateTypedParameter(cmd As ADODB.Command, Value As Variant) As ADODB.Parameter
    Dim param As ADODB.Parameter
    Dim dataType As DataTypeEnum
    Dim size As Long

    Select Case VarType(Value)
    Case vbInteger, vbLong
        dataType = adInteger
        size = 4
    Case vbDouble, vbSingle
        dataType = adDouble
        size = 8
    Case vbString
        'dataType = adVarChar
        dataType = adVarWChar
        size = IIf(Len(Value) > 0, Len(Value), 50)
    Case vbDate
        dataType = adDate
        size = 8
    Case vbBoolean
        dataType = adBoolean
        size = 1
    Case Else
        dataType = adVariant
        size = 255
    End Select
    Set param = cmd.CreateParameter(, dataType, adParamInput, size, Value)
    Set CreateTypedParameter = param
End Function

Private Function GetVBATypeName(Value As Variant) As String
    Select Case VarType(Value)
    Case vbEmpty: GetVBATypeName = "Empty"
    Case vbNull: GetVBATypeName = "Null"
    Case vbInteger: GetVBATypeName = "Integer"
    Case vbLong: GetVBATypeName = "Long"
    Case vbSingle: GetVBATypeName = "Single"
    Case vbDouble: GetVBATypeName = "Double"
    Case vbCurrency: GetVBATypeName = "Currency"
    Case vbDate: GetVBATypeName = "Date"
    Case vbString: GetVBATypeName = "String"
    Case vbObject: GetVBATypeName = "Object"
    Case vbBoolean: GetVBATypeName = "Boolean"
    Case vbVariant: GetVBATypeName = "Variant"
    Case vbDataObject: GetVBATypeName = "DataObject"
    Case vbDecimal: GetVBATypeName = "Decimal"
    Case vbByte: GetVBATypeName = "Byte"
    Case Else: GetVBATypeName = "Unknown"
    End Select
End Function

Sub DumpRecordset(rs As ADODB.Recordset)
    Dim i As Integer
    Dim rowOutput As String

    If rs.EOF Then
        Debug.Print "レコードセットは空です。"
        Exit Sub
    End If

    ' フィールド名の表示
    rowOutput = vbNullString
    For i = 0 To rs.Fields.Count - 1
        rowOutput = rowOutput & rs.Fields(i).Name & vbTab
    Next i
    Debug.Print rowOutput
    
    ' データの表示
    Do Until rs.EOF
        rowOutput = ""
        For i = 0 To rs.Fields.Count - 1
            rowOutput = rowOutput & Nz(rs.Fields(i).Value) & vbTab
        Next i
        Debug.Print rowOutput
        rs.MoveNext
    Loop
End Sub

' Null値を空文字に変換する関数
Private Function Nz(Value As Variant) As String
    If IsNull(Value) Then
        Nz = ""
    Else
        Nz = CStr(Value)
    End If
End Function

Private Sub Class_Initialize()
    ConnectionString = "Provider=SQLOLEDB;Data Source=192.168.11.15;Initial Catalog=Polling;User ID=Centrair;Password=363635m2;"
End Sub

Private Sub Class_Terminate()
    Call CloseConnection
End Sub

