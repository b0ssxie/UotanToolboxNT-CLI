using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UotanToolbox.Common.Devices
{
    public class FastbootTransport : IDeviceTransport
    {
        public TransportType Type => TransportType.Fastboot;

        public async Task<IEnumerable<DeviceInfo>> ProbeAsync(CancellationToken cancel = default)
        {
            string output = await CallExternalProgram.Fastboot("devices");
            var ids = StringHelper.FastbootDevices(output);
            var fids = ids.Select(id => new DeviceInfo(id, TransportType.Fastboot));
            if (Global.System.Contains("Linux") && Global.root)
            {
                string devcon = await CallExternalProgram.LsUSB();
                if (output.Contains("no permissions") || (Global.UseNativeFastboot && devcon.Contains("(fastboot)") && ids.Length == 0))
                {
                    // CLI 模式：自动尝试提权修复 /dev/bus/usb 权限
                    await CallExternalProgram.Sudo("chmod -R 777 /dev/bus/usb/");
                    output = await CallExternalProgram.Fastboot("devices");
                    ids = StringHelper.FastbootDevices(output);
                    fids = ids.Select(id => new DeviceInfo(id, TransportType.Fastboot));
                    Global.root = false;
                }
            }
            return fids;
        }

        public Task<string> RunAsync(DeviceInfo device, string command, CancellationToken cancel = default, Action<string>? outputCallback = null)
        {
            string args = command.TrimStart().StartsWith("-s ", System.StringComparison.Ordinal)
                ? command
                : $"-s {device.Id} {command}";
            return CallExternalProgram.Fastboot(args, outputCallback);
        }

        public Task<bool> ClaimAsync(DeviceInfo device)
        {
            // placeholder
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(DeviceInfo device) => Task.CompletedTask;
    }
}