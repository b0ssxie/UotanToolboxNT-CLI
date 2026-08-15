using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UotanToolbox.Common.Devices
{
    public class DeviceEventArgs : EventArgs
    {
        public DeviceInfo Device { get; }
        public DeviceEventArgs(DeviceInfo device) => Device = device;
    }

    public class DeviceManager : IDisposable
    {
        private readonly IList<IDeviceTransport> _transports;
        private readonly Dictionary<string, DeviceInfo> _cache = new();
        private readonly Dictionary<string, int> _missingScanCounts = new();
        private readonly SemaphoreSlim _scanLock = new(1, 1);
        private readonly object _cacheLock = new();
        private bool _disposed;

        public DeviceManager(IEnumerable<IDeviceTransport> transports)
        {
            _transports = transports.ToList();
        }

        public IReadOnlyCollection<DeviceInfo> Devices
        {
            get
            {
                lock (_cacheLock)
                {
                    return _cache.Values.ToList();
                }
            }
        }

        public event EventHandler<DeviceEventArgs>? DeviceAdded;
        public event EventHandler<DeviceEventArgs>? DeviceRemoved;
        /// <summary>
        /// Raised when a previously-seen device is discovered again with changed details.
        /// </summary>
        public event EventHandler<DeviceEventArgs>? DeviceUpdated;
        /// <summary>
        /// Raised after a scan operation completes (added and removed events have already fired).
        /// </summary>
        public event EventHandler? ScanCompleted;

        public async Task ScanAsync(CancellationToken cancel = default)
        {
            await _scanLock.WaitAsync(cancel);
            try
            {
                var seen = new HashSet<string>();
                foreach (var tr in _transports)
                {
                    var list = await tr.ProbeAsync(cancel);
                    foreach (var d in list)
                    {
                        seen.Add(d.Id);
                        lock (_cacheLock)
                        {
                            _missingScanCounts.Remove(d.Id);

                            if (!_cache.ContainsKey(d.Id))
                            {
                                _cache[d.Id] = d;
                                DeviceAdded?.Invoke(this, new DeviceEventArgs(d));
                            }
                            else
                            {
                                // existing device, check if any details changed
                                var old = _cache[d.Id];
                                if (!old.Equals(d))
                                {
                                    _cache[d.Id] = d;
                                    DeviceUpdated?.Invoke(this, new DeviceEventArgs(d));
                                }
                            }
                        }
                    }
                }

                // Debounce transient probe misses: remove only after 2 consecutive misses.
                List<string> candidates;
                lock (_cacheLock)
                {
                    candidates = _cache.Keys.Except(seen).ToList();
                }
                foreach (var id in candidates)
                {
                    int missCount;
                    lock (_cacheLock)
                    {
                        if (!_missingScanCounts.TryGetValue(id, out missCount))
                        {
                            missCount = 0;
                        }

                        missCount++;
                        _missingScanCounts[id] = missCount;
                    }
                    if (missCount < 2)
                    {
                        continue;
                    }

                    DeviceInfo? di;
                    lock (_cacheLock)
                    {
                        if (!_cache.TryGetValue(id, out di))
                        {
                            _missingScanCounts.Remove(id);
                            continue;
                        }

                        _cache.Remove(id);
                        _missingScanCounts.Remove(id);
                    }
                    DeviceRemoved?.Invoke(this, new DeviceEventArgs(di));
                }

                // notify that the scan finished
                ScanCompleted?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _scanLock.Release();
            }
        }

        public async Task<string> ExecuteAsync(DeviceInfo device, string cmd) =>
            await _transports.First(t => t.Type == device.Transport).RunAsync(device, cmd);

        public async Task<string> ExecuteStreamingAsync(DeviceInfo device, string cmd, Action<string> outputCallback) =>
            await _transports.First(t => t.Type == device.Transport).RunAsync(device, cmd, outputCallback: outputCallback);

        public Task<bool> ClaimAsync(DeviceInfo d) =>
            _transports.First(t => t.Type == d.Transport).ClaimAsync(d);

        public Task ReleaseAsync(DeviceInfo d) =>
            _transports.First(t => t.Type == d.Transport).ReleaseAsync(d);

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var tr in _transports)
                {
                    if (tr is IDisposable d) d.Dispose();
                }
                _disposed = true;
            }
        }
    }
}