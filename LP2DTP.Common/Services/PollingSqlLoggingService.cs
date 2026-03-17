using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// ポーリングデータを SQL Server に書き込むサービス。
    /// VISA（電圧・電流）と Modbus（温度）の両方に対応し、
    /// 同一タイムスタンプ・機器の行を UPSERT する。
    /// </summary>
    public sealed class PollingSqlLoggingService : IDisposable
    {
        /// <summary>通信失敗時など、実測値が得られなかった場合に使用するフォールバック値。</summary>
        private const double DefaultNullValue = -999.0;

        private readonly PollingLogService _logService = PollingLogService.Instance;

        /// <summary>レスポンス文字列から数値部分を抽出する正規表現。指数表記にも対応。</summary>
        private static readonly Regex NumericRegex = new(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        // ── 接続設定 ────────────────────────────────────────────────

        /// <summary>SQL Server のホスト名または IP アドレス。</summary>
        public string ServerName { get; } = "192.168.11.15";

        /// <summary>接続先データベース名。</summary>
        public string DatabaseName { get; } = "Polling";

        /// <summary>書き込み先テーブル名。</summary>
        public string TableName { get; } = "Logging2";

        /// <summary>SQL ログインのユーザー名。</summary>
        public string UserName { get; } = "Polling";

        /// <summary>SQL ログインのパスワード。</summary>
        public string Password { get; } = "Polling";

        /// <summary>各プロパティから組み立てた接続文字列。</summary>
        public string ConnectionString =>
            $"Server={ServerName};Database={DatabaseName};User ID={UserName};Password={Password};Encrypt=False;TrustServerCertificate=True;";

        /// <summary>
        /// true のとき DB 書き込みを実行する。false にすると書き込みをスキップできる。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        // ── 公開メソッド ─────────────────────────────────────────────

        /// <summary>
        /// ポーリングデータを 1 件 DB に書き込む。
        /// Modbus コマンドの場合は Temp を UPDATE/INSERT し、
        /// VISA コマンドの場合は Volt・Amp を UPSERT する。
        /// </summary>
        /// <param name="data">書き込むポーリングデータ。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        public async Task WriteAsync(PollingDataReceivedEventArgs data, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (!IsEnabled)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // レスポンス文字列から volt/amp/temp のいずれかを解析する。
            // 解析できない場合（空文字・非数値など）は書き込みをスキップ。
            if (!TryCreateValues(data, out var volt, out var amp, out var temp))
            {
                return;
            }

            try
            {
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                if (temp.HasValue && IsModbusCommand(data.Command))
                {
                    // Modbus の場合：同一タイムスタンプ・機器名の行の Temp だけを更新する
                    await UpdateTempForCycleAsync(connection, data, temp.Value, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // VISA の場合：Volt / Amp を UPSERT する
                await UpsertVisaCycleRowAsync(connection, data, volt, amp, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logService.Log("ERROR", "SQL", data.MachineName, $"SQL write failed: {ex.Message}");
                throw;
            }
        }

        // ── プライベートメソッド ──────────────────────────────────────

        /// <summary>
        /// VISA データ（Volt / Amp）を UPSERT する。
        /// 同一の DateTime・MachineName・UnitName・HostName の行が存在すれば UPDATE、
        /// なければ INSERT する。Temp は未取得なのでフォールバック値を挿入する。
        /// </summary>
        private async Task UpsertVisaCycleRowAsync(SqlConnection connection, PollingDataReceivedEventArgs data, double? volt, double? amp, CancellationToken cancellationToken)
        {
            var hostName = ResolveHostName(data);

            // ── UPDATE ──
            // COALESCE により、今回渡された値が NULL（未計測）の場合は既存の DB 値を保持する。
            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                $"UPDATE {TableName} " +
                "SET [Volt] = COALESCE(@Volt, [Volt]), [Amp] = COALESCE(@Amp, [Amp]) " +
                "WHERE [DateTime] = @DateTime " +
                "AND ISNULL([MachineName], '') = ISNULL(@MachineName, '') " +
                "AND ISNULL([UnitName], '') = ISNULL(@UnitName, '') " +
                "AND [HostName] = @HostName;";

            // NULL を渡すことで COALESCE が既存値を使う（DBNull = SQL NULL）
            updateCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = ToDbDouble(volt) });
            updateCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = ToDbDouble(amp) });
            AddCommonIdentityParameters(updateCommand, data, hostName);

            var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected > 0)
            {
                // 既存行を更新できたので INSERT は不要
                return;
            }

            // ── INSERT ──
            // 対象行が存在しないため新規挿入する。
            // Temp はこのサイクルで未取得のためフォールバック値を使用する。
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = BuildInsertSqlTemplate();
            AddCommonIdentityParameters(insertCommand, data, hostName);
            insertCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = volt ?? DefaultNullValue });
            insertCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = amp ?? DefaultNullValue });
            insertCommand.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Float) { Value = DefaultNullValue });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Modbus データ（Temp）を UPDATE/INSERT する。
        /// 同一の DateTime・MachineName の行が存在すれば Temp だけを UPDATE し、
        /// なければ Volt・Amp をフォールバック値で新規 INSERT する。
        /// </summary>
        private async Task UpdateTempForCycleAsync(SqlConnection connection, PollingDataReceivedEventArgs data, double temp, CancellationToken cancellationToken)
        {
            var hostName = ResolveHostName(data);

            // ── UPDATE ──
            // 同一タイムスタンプ・機器名の既存行の Temp だけを更新する。
            // UnitName は Modbus では一致条件に含めない（複数ユニットが同一行を共有する可能性があるため）。
            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                $"UPDATE {TableName} SET [Temp] = @Temp " +
                "WHERE [DateTime] = @DateTime " +
                "AND ISNULL([MachineName], '') = ISNULL(@MachineName, '');";

            updateCommand.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Float) { Value = temp });
            updateCommand.Parameters.Add(new SqlParameter("@DateTime", SqlDbType.DateTime) { Value = data.Timestamp });
            updateCommand.Parameters.Add(new SqlParameter("@MachineName", SqlDbType.NVarChar, 64)
            {
                Value = ToDbString(data.MachineName)
            });

            var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected > 0)
            {
                // 既存行を更新できたので INSERT は不要
                return;
            }

            // ── INSERT ──
            // 対象行が存在しないため新規挿入する。
            // Volt・Amp はこのサイクルで未取得のためフォールバック値を使用する。
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = BuildInsertSqlTemplate();
            AddCommonIdentityParameters(insertCommand, data, hostName);
            // Modbus の場合は UnitName に MachineName をセット
            insertCommand.Parameters["@UnitName"].Value = ToDbString(data.MachineName);
            insertCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = DefaultNullValue });
            insertCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = DefaultNullValue });
            insertCommand.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Float) { Value = temp });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>INSERT 文のテンプレートを返す。</summary>
        public string BuildInsertSqlTemplate()
        {
            return $"INSERT INTO {TableName} ([DateTime], [MachineName], [UnitName], [HostName], [Volt], [Amp], [Temp]) VALUES (@DateTime, @MachineName, @UnitName, @HostName, @Volt, @Amp, @Temp);";
        }

        /// <summary>
        /// ポーリングデータのレスポンスとコマンドを解析し、
        /// Volt / Amp / Temp のいずれか 1 つに値をセットして返す。
        /// 解析できない場合は false を返す。
        /// </summary>
        private static bool TryCreateValues(PollingDataReceivedEventArgs data, out double? volt, out double? amp, out double? temp)
        {
            volt = null;
            amp = null;
            temp = null;

            // レスポンス文字列から最初の数値を抽出する
            if (!TryParseNumeric(data.Response, out var value))
            {
                return false;
            }

            // Modbus コマンド or 温度単位が含まれる → Temp
            if (IsModbusCommand(data.Command) || HasTempUnit(data.Response))
            {
                temp = value;
                return true;
            }

            // 電流コマンド or 電流単位が含まれる → Amp
            if (IsAmpCommand(data.Command) || HasAmpUnit(data.Response))
            {
                amp = value;
                return true;
            }

            // 電圧コマンド or 電圧単位が含まれる → Volt
            if (IsVoltCommand(data.Command) || HasVoltUnit(data.Response))
            {
                volt = value;
                return true;
            }

            // いずれにも分類できない場合はスキップ
            return false;
        }

        /// <summary>
        /// 文字列から数値を正規表現で抽出し、double にパースする。
        /// InvariantCulture と CurrentCulture の両方を試みる。
        /// </summary>
        private static bool TryParseNumeric(string? text, out double value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = NumericRegex.Match(text);
            if (!match.Success)
            {
                return false;
            }

            return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(match.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>
        /// DB の HostName カラムに使う値を決定する。
        /// IpAddress → UnitName → マシン名 の優先順で返す。
        /// </summary>
        private static string ResolveHostName(PollingDataReceivedEventArgs data)
        {
            if (!string.IsNullOrWhiteSpace(data.IpAddress))
            {
                return data.IpAddress;
            }

            if (!string.IsNullOrWhiteSpace(data.UnitName))
            {
                return data.UnitName;
            }

            return Environment.MachineName;
        }

        // ── コマンド・レスポンス判定ヘルパー ─────────────────────────

        /// <summary>コマンドが Modbus FC コマンド（"FC" で始まる）かどうかを判定する。</summary>
        private static bool IsModbusCommand(string? command)
            => !string.IsNullOrWhiteSpace(command)
               && command.StartsWith("FC", StringComparison.OrdinalIgnoreCase);

        /// <summary>コマンドが電流計測コマンドかどうかを判定する。</summary>
        private static bool IsAmpCommand(string? command)
            => ContainsAny(command, "CURR", "AMP", "CURRENT");

        /// <summary>コマンドが電圧計測コマンドかどうかを判定する。</summary>
        private static bool IsVoltCommand(string? command)
            => ContainsAny(command, "VOLT", "VOLTAGE");

        /// <summary>レスポンスに電流単位が含まれるかどうかを判定する。</summary>
        private static bool HasAmpUnit(string? response)
            => ContainsAny(response, " A", "AMP");

        /// <summary>レスポンスに電圧単位が含まれるかどうかを判定する。</summary>
        private static bool HasVoltUnit(string? response)
            => ContainsAny(response, " V", "VOLT");

        /// <summary>レスポンスに温度単位が含まれるかどうかを判定する。</summary>
        private static bool HasTempUnit(string? response)
            => ContainsAny(response, "°C", " C", "CELSIUS");

        /// <summary>テキストにいずれかのトークンが含まれるかどうかを判定する（大文字小文字無視）。</summary>
        private static bool ContainsAny(string? text, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ── SQL パラメーター変換ヘルパー ─────────────────────────────

        /// <summary>
        /// null または空文字を DBNull.Value に変換する。
        /// SQL パラメーターの文字列型に使用する。
        /// </summary>
        private static object ToDbString(string? value)
            => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

        /// <summary>
        /// null を DBNull.Value に変換する。
        /// UPDATE の COALESCE と組み合わせて既存値を保持するために使用する。
        /// </summary>
        private static object ToDbDouble(double? value)
            => value.HasValue ? value.Value : DBNull.Value;

        /// <summary>
        /// INSERT / UPDATE 共通の識別パラメーター（DateTime・MachineName・UnitName・HostName）を追加する。
        /// </summary>
        private static void AddCommonIdentityParameters(SqlCommand command, PollingDataReceivedEventArgs data, string hostName)
        {
            command.Parameters.Add(new SqlParameter("@DateTime", SqlDbType.DateTime) { Value = data.Timestamp });
            command.Parameters.Add(new SqlParameter("@MachineName", SqlDbType.NVarChar, 64) { Value = ToDbString(data.MachineName) });
            command.Parameters.Add(new SqlParameter("@UnitName", SqlDbType.NVarChar, 32) { Value = ToDbString(data.UnitName) });
            command.Parameters.Add(new SqlParameter("@HostName", SqlDbType.NVarChar, 50) { Value = hostName });
        }

        public void Dispose()
        {
        }
    }
}
