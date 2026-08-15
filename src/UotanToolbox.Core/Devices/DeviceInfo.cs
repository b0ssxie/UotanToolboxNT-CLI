#nullable enable
using System.Collections.Generic;

namespace UotanToolbox.Common.Devices
{
    public class DeviceInfo
    {
        public string Id { get; }
        public TransportType Transport { get; }
        public IReadOnlyDictionary<string, string> Properties { get; }

        public DeviceInfo(string id, TransportType transport, IReadOnlyDictionary<string, string>? properties = null)
        {
            Id = id;
            Transport = transport;
            Properties = properties ?? new Dictionary<string, string>();
        }

        public override string ToString() => Id;

        public override bool Equals(object? obj)
        {
            if (obj is not DeviceInfo other)
                return false;
            if (Id != other.Id || Transport != other.Transport)
                return false;
            // compare properties dictionaries
            if (Properties == null && other.Properties == null)
                return true;
            if (Properties == null || other.Properties == null)
                return false;
            if (Properties.Count != other.Properties.Count)
                return false;
            foreach (var kv in Properties)
            {
                if (!other.Properties.TryGetValue(kv.Key, out var val) || val != kv.Value)
                    return false;
            }
            return true;
        }

        public override int GetHashCode() => (Id, Transport, Properties?.Count ?? 0).GetHashCode();
    }
}