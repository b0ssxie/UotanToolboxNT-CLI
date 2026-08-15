using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UotanToolbox.Common.Devices
{
    public class EdlTransport : IDeviceTransport
    {
        public TransportType Type => TransportType.Edl;

        public async Task<IEnumerable<DeviceInfo>> ProbeAsync(CancellationToken cancel = default)
        {
            string devcon = Global.System == "Windows"
                ? await CallExternalProgram.Devcon("find usb*")
                : await CallExternalProgram.LsUSB();
            var ids = StringHelper.COMDevices(devcon);
            return ids.Select(id => new DeviceInfo(id, TransportType.Edl));
        }

        public Task<string> RunAsync(DeviceInfo device, string command, CancellationToken cancel = default, Action<string>? outputCallback = null)
        {
            // EDL devices don't support command execution through this interface
            return Task.FromResult(string.Empty);
        }

        public Task<bool> ClaimAsync(DeviceInfo device) => Task.FromResult(true);
        public Task ReleaseAsync(DeviceInfo device) => Task.CompletedTask;
    }
}
