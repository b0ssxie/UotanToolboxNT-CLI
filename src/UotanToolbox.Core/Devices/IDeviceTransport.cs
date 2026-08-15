using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UotanToolbox.Common.Devices
{
    public interface IDeviceTransport
    {
        TransportType Type { get; }

        /// <summary>
        /// 扫描当前由该传输可见的设备。
        /// </summary>
        Task<IEnumerable<DeviceInfo>> ProbeAsync(CancellationToken cancel = default);

        /// <summary>
        /// 在指定设备上执行命令，返回输出。
        /// </summary>
        Task<string> RunAsync(DeviceInfo device, string command, CancellationToken cancel = default, Action<string>? outputCallback = null);

        /// <summary>
        /// 试图获取对设备的独占访问。
        /// </summary>
        Task<bool> ClaimAsync(DeviceInfo device);

        /// <summary>
        /// 释放之前获取的访问。
        /// </summary>
        Task ReleaseAsync(DeviceInfo device);
    }
}