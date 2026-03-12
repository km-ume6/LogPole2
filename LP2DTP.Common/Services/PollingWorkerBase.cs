using System;
using System.Threading;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    public abstract class PollingWorkerBase : IPollingWorker
    {
        private bool _isRunning;
        private bool _isEndpointAlive;
        private int _pollingIntervalSeconds = 1;
        private int _healthCheckIntervalSeconds = 5;
        private DateTime _nextPollingAtUtc = DateTime.MinValue;
        private DateTime _nextHealthCheckAtUtc = DateTime.MinValue;
        protected readonly PollingLogService LogService = PollingLogService.Instance;

        public bool IsRunning => _isRunning;

        public int PollingIntervalSeconds
        {
            get => _pollingIntervalSeconds;
            set
            {
                _pollingIntervalSeconds = Math.Clamp(value, 1, 3600);
                if (_isRunning)
                {
                    _nextPollingAtUtc = GetNextAlignedPollingTimeUtc(DateTime.UtcNow);
                }
            }
        }

        public int HealthCheckIntervalSeconds
        {
            get => _healthCheckIntervalSeconds;
            set
            {
                _healthCheckIntervalSeconds = Math.Clamp(value, 1, 3600);
                if (_isRunning)
                {
                    _nextHealthCheckAtUtc = DateTime.UtcNow;
                }
            }
        }

        public abstract event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public abstract event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        public Task StartAsync()
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _isRunning = true;
            _isEndpointAlive = true;
            _nextHealthCheckAtUtc = DateTime.UtcNow;
            _nextPollingAtUtc = GetNextAlignedPollingTimeUtc(DateTime.UtcNow);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            try
            {
                await EnsureDisconnectedAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public bool IsHealthCheckDue(DateTime utcNow)
        {
            return utcNow >= _nextHealthCheckAtUtc;
        }

        public bool IsPollingDue(DateTime utcNow)
        {
            return utcNow >= _nextPollingAtUtc;
        }

        public async Task<bool> ExecuteHealthCheckPhaseAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                return false;
            }

            _isEndpointAlive = await CheckEndpointAliveAsync(cancellationToken).ConfigureAwait(false);
            _nextHealthCheckAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, HealthCheckIntervalSeconds));

            if (!_isEndpointAlive)
            {
                await EnsureDisconnectedAsync().ConfigureAwait(false);
            }

            return _isEndpointAlive;
        }

        public async Task ExecutePollingPhaseAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning || !_isEndpointAlive)
            {
                return;
            }

            await ExecuteSingleCycleSafelyAsync(cancellationToken).ConfigureAwait(false);
            MoveNextPollingTime();
        }

        public async Task ExecuteCycleAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (IsHealthCheckDue(now))
            {
                await ExecuteHealthCheckPhaseAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_isEndpointAlive)
            {
                return;
            }

            if (!IsPollingDue(now))
            {
                return;
            }

            await ExecutePollingPhaseAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteSingleCycleSafelyAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (IsItemEnabled)
                {
                    await ExecutePollingAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnsureDisconnectedAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _isEndpointAlive = false;
                OnPollingError(ex);
            }
        }

        private async Task EnsureDisconnectedAsync()
        {
            if (!IsConnectionOpen)
            {
                return;
            }

            await DisconnectAsync().ConfigureAwait(false);
        }

        private void MoveNextPollingTime()
        {
            var interval = TimeSpan.FromSeconds(_pollingIntervalSeconds);
            do
            {
                _nextPollingAtUtc = _nextPollingAtUtc.Add(interval);
            }
            while (_nextPollingAtUtc <= DateTime.UtcNow);
        }

        private DateTime GetNextAlignedPollingTimeUtc(DateTime utcNow)
        {
            var intervalTicks = TimeSpan.FromSeconds(_pollingIntervalSeconds).Ticks;
            var nextTicks = ((utcNow.Ticks + intervalTicks - 1) / intervalTicks) * intervalTicks;
            return new DateTime(nextTicks, DateTimeKind.Utc);
        }

        protected abstract bool IsItemEnabled { get; }
        protected abstract bool IsConnectionOpen { get; }
        protected abstract Task<bool> CheckEndpointAliveAsync(CancellationToken cancellationToken);
        protected abstract Task ExecutePollingAsync(CancellationToken cancellationToken);
        protected abstract Task DisconnectAsync();
        protected abstract void OnPollingError(Exception ex);

        public void Dispose()
        {
            try
            {
                StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
            }

            DisposeCore();
        }

        protected virtual void DisposeCore()
        {
        }
    }
}
