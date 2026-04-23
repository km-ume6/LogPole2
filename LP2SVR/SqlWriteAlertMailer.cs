using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LP2SVR
{
    public sealed class SqlWriteAlertMailer
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlWriteAlertMailer> _logger;

        public SqlWriteAlertMailer(IConfiguration configuration, ILogger<SqlWriteAlertMailer> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendServiceStartedAsync(string serviceName, CancellationToken cancellationToken)
        {
            var subject = $"[{serviceName}] サービス起動";
            var body =
                $"{serviceName} が起動しました。{Environment.NewLine}" +
                $"StartedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            await SendAsync(subject, body, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendServiceStoppedAsync(string serviceName, CancellationToken cancellationToken)
        {
            var subject = $"[{serviceName}] サービス終了";
            var body =
                $"{serviceName} が終了しました。{Environment.NewLine}" +
                $"StoppedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            await SendAsync(subject, body, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendFailureAsync(string serviceName, int pendingCount, int consecutiveCount, TimeSpan failureAge, CancellationToken cancellationToken)
        {
            var subject = $"[{serviceName}] SQL書き込み障害アラート";
            var body =
                $"{serviceName} でSQL書き込み失敗が継続しています。{Environment.NewLine}" +
                $"Pending: {pendingCount}{Environment.NewLine}" +
                $"ConsecutiveSqlErrors: {consecutiveCount}{Environment.NewLine}" +
                $"LastSqlErrorAgeSeconds: {(int)failureAge.TotalSeconds}{Environment.NewLine}" +
                $"DetectedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            await SendAsync(subject, body, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendRecoveryAsync(string serviceName, CancellationToken cancellationToken)
        {
            var subject = $"[{serviceName}] SQL書き込み障害復旧";
            var body =
                $"{serviceName} のSQL書き込み障害が復旧しました。{Environment.NewLine}" +
                $"RecoveredAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            await SendAsync(subject, body, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            var smtpHost = _configuration["AlertMail:SmtpHost"] ?? "mw2pjm08dd.bizmw.com";
            var smtpUser = _configuration["AlertMail:UserName"] ?? "maruyama";
            var smtpPassword = _configuration["AlertMail:Password"] ?? "Kouji0821##";
            var from = _configuration["AlertMail:From"];
            var toRaw = _configuration["AlertMail:To"];

            if (string.IsNullOrWhiteSpace(smtpHost)
                || string.IsNullOrWhiteSpace(smtpUser)
                || string.IsNullOrWhiteSpace(smtpPassword)
                || string.IsNullOrWhiteSpace(from)
                || string.IsNullOrWhiteSpace(toRaw))
            {
                _logger.LogWarning("AlertMail settings are incomplete. Skip sending mail.");
                return;
            }

            var toAddresses = toRaw
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (toAddresses.Length == 0)
            {
                _logger.LogWarning("AlertMail:To has no valid address. Skip sending mail.");
                return;
            }

            var smtpPort = Math.Clamp(ParseInt(_configuration["AlertMail:Port"], 587), 1, 65535);
            var useStartTls = ParseBool(_configuration["AlertMail:UseStartTls"], true);

            using var message = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = body
            };

            foreach (var address in toAddresses)
            {
                message.To.Add(address);
            }

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = useStartTls,
                Credentials = new NetworkCredential(smtpUser, smtpPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message).ConfigureAwait(false);
        }

        private static int ParseInt(string? value, int fallback)
        {
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string? value, bool fallback)
        {
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }
    }
}
