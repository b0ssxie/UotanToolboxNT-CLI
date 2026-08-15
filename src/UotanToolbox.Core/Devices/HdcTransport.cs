using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UotanToolbox.Common.Devices
{
    public class HdcTransport : IDeviceTransport
    {
        public TransportType Type => TransportType.Hdc;

        public async Task<IEnumerable<DeviceInfo>> ProbeAsync(CancellationToken cancel = default)
        {
            string output = await CallExternalProgram.HDC("list targets");
            var ids = StringHelper.HDCDevices(output);
            var hids = ids.Select(id => new DeviceInfo(id, TransportType.Hdc));
            if (Global.System.Contains("Linux") && Global.root)
            {
                string devcon = await CallExternalProgram.LsUSB();
                if (devcon.Contains("HDC Device") && ids.Length == 0)
                {
                    // CLI 模式：自动尝试提权修复 /dev/bus/usb 权限
                    await CallExternalProgram.Sudo("chmod -R 777 /dev/bus/usb/");
                    output = await CallExternalProgram.HDC("list targets");
                    ids = StringHelper.HDCDevices(output);
                    hids = ids.Select(id => new DeviceInfo(id, TransportType.Hdc));
                    Global.root = false;
                }
            }
            return hids;
        }

        public Task<string> RunAsync(DeviceInfo device, string command, CancellationToken cancel = default, Action<string>? outputCallback = null)
        {
            string args = command.TrimStart().StartsWith("-t ", System.StringComparison.Ordinal)
                ? command
                : $"-t {device.Id} {command}";
            return CallExternalProgram.HDC(args, outputCallback);
        }

        public Task<bool> ClaimAsync(DeviceInfo device) => Task.FromResult(true);
        public Task ReleaseAsync(DeviceInfo device) => Task.CompletedTask;
    }
}