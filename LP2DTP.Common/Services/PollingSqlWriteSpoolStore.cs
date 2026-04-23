using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LP2DTP.Common.Services
{
    internal sealed class PollingSqlWriteSpoolStore
    {
        private const string FileName = "pending-sql-writes.jsonl";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly object _sync = new();
        private readonly string _filePath = ApplicationDataPath.GetFilePath(FileName);

        public IReadOnlyList<PollingDataReceivedEventArgs> Load()
        {
            lock (_sync)
            {
                var items = new List<PollingDataReceivedEventArgs>();
                if (!File.Exists(_filePath))
                {
                    return items;
                }

                foreach (var line in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var item = JsonSerializer.Deserialize<PollingDataReceivedEventArgs>(line, JsonOptions);
                        if (item != null)
                        {
                            items.Add(item);
                        }
                    }
                    catch
                    {
                    }
                }

                return items;
            }
        }

        public void Append(PollingDataReceivedEventArgs item)
        {
            ArgumentNullException.ThrowIfNull(item);

            lock (_sync)
            {
                ApplicationDataPath.EnsureDirectoryExists();
                var json = JsonSerializer.Serialize(item, JsonOptions);
                File.AppendAllText(_filePath, json + Environment.NewLine);
            }
        }

        public void SaveSnapshot(IEnumerable<PollingDataReceivedEventArgs> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            lock (_sync)
            {
                ApplicationDataPath.EnsureDirectoryExists();

                var tempPath = _filePath + ".tmp";
                using (var writer = new StreamWriter(tempPath, append: false))
                {
                    foreach (var item in items)
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        var json = JsonSerializer.Serialize(item, JsonOptions);
                        writer.WriteLine(json);
                    }
                }

                File.Copy(tempPath, _filePath, overwrite: true);
                File.Delete(tempPath);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
        }
    }
}
