using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace LP2DTP.Common.Services
{
    public sealed class PollingSqlLoggingService : IDisposable
    {
        private readonly PollingLogService _logService = PollingLogService.Instance;
        private static readonly Regex NumericRegex = new(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public string ServerName { get; } = "192.168.11.15";
        public string DatabaseName { get; } = "Polling";
        public string TableName { get; } = "Logging2";
        public string UserName { get; } = "Polling";
        public string Password { get; } = "Polling";

        public string ConnectionString =>
            $"Server={ServerName};Database={DatabaseName};User ID={UserName};Password={Password};Encrypt=False;TrustServerCertificate=True;";

        public bool IsEnabled { get; set; } = true;

        public async Task WriteAsync(PollingDataReceivedEventArgs data, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (!IsEnabled)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

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
                    await UpdateTempForCycleAsync(connection, data, temp.Value, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await UpsertVisaCycleRowAsync(connection, data, volt, amp, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logService.Log("ERROR", "SQL", data.MachineName, $"SQL write failed: {ex.Message}");
                throw;
            }
        }

        private async Task UpsertVisaCycleRowAsync(SqlConnection connection, PollingDataReceivedEventArgs data, double? volt, double? amp, CancellationToken cancellationToken)
        {
            var hostName = ResolveHostName(data);

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                $"UPDATE {TableName} " +
                "SET [Volt] = COALESCE(@Volt, [Volt]), [Amp] = COALESCE(@Amp, [Amp]) " +
                "WHERE [DateTime] = @DateTime " +
                "AND ISNULL([MachineName], '') = ISNULL(@MachineName, '') " +
                "AND ISNULL([UnitName], '') = ISNULL(@UnitName, '') " +
                "AND [HostName] = @HostName;";

            updateCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = ToDbDouble(volt) });
            updateCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = ToDbDouble(amp) });
            AddCommonIdentityParameters(updateCommand, data, hostName);

            var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected > 0)
            {
                return;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = BuildInsertSqlTemplate();
            AddCommonIdentityParameters(insertCommand, data, hostName);
            insertCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = ToDbDouble(volt) });
            insertCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = ToDbDouble(amp) });
            insertCommand.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Float) { Value = DBNull.Value });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task UpdateTempForCycleAsync(SqlConnection connection, PollingDataReceivedEventArgs data, double temp, CancellationToken cancellationToken)
        {
            var hostName = ResolveHostName(data);

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
                return;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = BuildInsertSqlTemplate();
            AddCommonIdentityParameters(insertCommand, data, hostName);
            insertCommand.Parameters.Add(new SqlParameter("@Volt", SqlDbType.Float) { Value = DBNull.Value });
            insertCommand.Parameters.Add(new SqlParameter("@Amp", SqlDbType.Float) { Value = DBNull.Value });
            insertCommand.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Float) { Value = temp });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public string BuildInsertSqlTemplate()
        {
            return $"INSERT INTO {TableName} ([DateTime], [MachineName], [UnitName], [HostName], [Volt], [Amp], [Temp]) VALUES (@DateTime, @MachineName, @UnitName, @HostName, @Volt, @Amp, @Temp);";
        }

        private static bool TryCreateValues(PollingDataReceivedEventArgs data, out double? volt, out double? amp, out double? temp)
        {
            volt = null;
            amp = null;
            temp = null;

            if (!TryParseNumeric(data.Response, out var value))
            {
                return false;
            }

            if (IsModbusCommand(data.Command) || HasTempUnit(data.Response))
            {
                temp = value;
                return true;
            }

            if (IsAmpCommand(data.Command) || HasAmpUnit(data.Response))
            {
                amp = value;
                return true;
            }

            if (IsVoltCommand(data.Command) || HasVoltUnit(data.Response))
            {
                volt = value;
                return true;
            }

            return false;
        }

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

        private static bool IsModbusCommand(string? command)
            => !string.IsNullOrWhiteSpace(command)
               && command.StartsWith("FC", StringComparison.OrdinalIgnoreCase);

        private static bool IsAmpCommand(string? command)
            => ContainsAny(command, "CURR", "AMP", "CURRENT");

        private static bool IsVoltCommand(string? command)
            => ContainsAny(command, "VOLT", "VOLTAGE");

        private static bool HasAmpUnit(string? response)
            => ContainsAny(response, " A", "AMP");

        private static bool HasVoltUnit(string? response)
            => ContainsAny(response, " V", "VOLT");

        private static bool HasTempUnit(string? response)
            => ContainsAny(response, "°C", " C", "CELSIUS");

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

        private static object ToDbString(string? value)
            => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

        private static object ToDbDouble(double? value)
            => value.HasValue ? value.Value : DBNull.Value;

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
