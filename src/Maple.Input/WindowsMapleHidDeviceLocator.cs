using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace Maple.Input
{
    public static class MapleHidDeviceIdentity
    {
        public static readonly Guid InterfaceClassGuid = new Guid("6E6E6F4A-21A5-4DD2-86E5-7DB4C7E8A101");
        public const int VendorId = 0xF1AE;
        public const int ProductId = 0x0001;
        public const string ReportDescriptorSha256 = "d0adc4c8754c228f1ed84f6d294b17df6e10fc13b684b7807325189b0b3b510e";
    }

    public interface IDeviceInterfaceEnumerator
    {
        bool TryEnumerate(Guid interfaceClassGuid, out IReadOnlyList<string> devicePaths, out string error);
    }

    public interface IMapleHidDeviceLocator
    {
        bool TryLocate(out string devicePath, out string error);
    }

    public sealed class WindowsMapleHidDeviceLocator : IMapleHidDeviceLocator
    {
        private readonly IDeviceInterfaceEnumerator enumerator;

        public WindowsMapleHidDeviceLocator() : this(new CfgMgr32DeviceInterfaceEnumerator()) { }

        public WindowsMapleHidDeviceLocator(IDeviceInterfaceEnumerator enumerator)
        {
            this.enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        }

        public bool TryLocate(out string devicePath, out string error)
        {
            devicePath = string.Empty;
            if (!enumerator.TryEnumerate(MapleHidDeviceIdentity.InterfaceClassGuid, out IReadOnlyList<string> paths, out error))
                return false;
            string[] distinct = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinct.Length == 0) { error = "HID_DEVICE_NOT_INSTALLED"; return false; }
            if (distinct.Length != 1) { error = "HID_DEVICE_AMBIGUOUS:" + distinct.Length; return false; }
            devicePath = distinct[0];
            error = string.Empty;
            return true;
        }
    }

    public sealed class CfgMgr32DeviceInterfaceEnumerator : IDeviceInterfaceEnumerator
    {
        private const uint PresentOnly = 0;
        private const int Success = 0;

        public bool TryEnumerate(Guid interfaceClassGuid, out IReadOnlyList<string> devicePaths, out string error)
        {
            devicePaths = Array.Empty<string>();
            if (!OperatingSystem.IsWindows()) { error = "HID_PLATFORM_NOT_SUPPORTED"; return false; }
            int result = CM_Get_Device_Interface_List_Size(out uint length, ref interfaceClassGuid, null, PresentOnly);
            if (result != Success) { error = ConfigManagerError("HID_ENUM_SIZE_FAILED", result); return false; }
            if (length <= 1) { error = string.Empty; return true; }
            var buffer = new char[length];
            result = CM_Get_Device_Interface_List(ref interfaceClassGuid, null, buffer, length, PresentOnly);
            if (result != Success) { error = ConfigManagerError("HID_ENUM_LIST_FAILED", result); return false; }
            devicePaths = new string(buffer)
                .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            error = string.Empty;
            return true;
        }

        private static string ConfigManagerError(string prefix, int code) =>
            prefix + ":" + code + ":" + new Win32Exception(code).Message;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
        private static extern int CM_Get_Device_Interface_List_Size(
            out uint length,
            ref Guid interfaceClassGuid,
            string deviceId,
            uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_ListW")]
        private static extern int CM_Get_Device_Interface_List(
            ref Guid interfaceClassGuid,
            string deviceId,
            [Out] char[] buffer,
            uint bufferLength,
            uint flags);
    }
}
