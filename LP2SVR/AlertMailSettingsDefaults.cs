using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LP2SVR
{
    internal static class AlertMailSettingsDefaults
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LogPole2",
            "appsettings.mail.json");

        public static Dictionary<string, string?> EnsureWritten()
        {
            var writtenDefaults = new Dictionary<string, string?>();

            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            JsonObject root;
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var existingText = File.ReadAllText(SettingsFilePath);
                    root = JsonNode.Parse(existingText) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            var alertMail = root["AlertMail"] as JsonObject;
            if (alertMail == null)
            {
                alertMail = new JsonObject();
                root["AlertMail"] = alertMail;
            }

            Ensure(alertMail, writtenDefaults, "SmtpHost", "mw2pjm08dd.bizmw.com");
            Ensure(alertMail, writtenDefaults, "Port", 587);
            Ensure(alertMail, writtenDefaults, "UseStartTls", true);
            Ensure(alertMail, writtenDefaults, "UserName", "maruyama");
            Ensure(alertMail, writtenDefaults, "Password", "Kouji0821##");
            Ensure(alertMail, writtenDefaults, "From", "maruyama@yamajuceramics.co.jp");
            Ensure(alertMail, writtenDefaults, "To", "maruyama@yamajuceramics.co.jp");
            Ensure(alertMail, writtenDefaults, "SqlWriteFailureThresholdSeconds", 3600);
            Ensure(alertMail, writtenDefaults, "ResendIntervalSeconds", 600);

            if (!File.Exists(SettingsFilePath) || writtenDefaults.Count > 0)
            {
                var json = root.ToJsonString(JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }

            return writtenDefaults;
        }

        private static void Ensure(JsonObject section, IDictionary<string, string?> writtenDefaults, string key, string defaultValue)
        {
            var node = section[key];
            if (node is JsonValue jsonValue)
            {
                try
                {
                    var current = jsonValue.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(current))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }

            section[key] = defaultValue;
            writtenDefaults[$"AlertMail:{key}"] = defaultValue;
        }

        private static void Ensure(JsonObject section, IDictionary<string, string?> writtenDefaults, string key, int defaultValue)
        {
            if (section[key] is JsonValue)
            {
                return;
            }

            section[key] = defaultValue;
            writtenDefaults[$"AlertMail:{key}"] = defaultValue.ToString();
        }

        private static void Ensure(JsonObject section, IDictionary<string, string?> writtenDefaults, string key, bool defaultValue)
        {
            if (section[key] is JsonValue)
            {
                return;
            }

            section[key] = defaultValue;
            writtenDefaults[$"AlertMail:{key}"] = defaultValue.ToString();
        }
    }
}
