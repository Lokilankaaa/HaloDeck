using System.Runtime.InteropServices;
using HandheldOpti.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace HandheldOpti.App.Native;

/// <summary>
/// Direct non-admin HID V2 communication for GPD Win 5 controller (VID_2F24&amp;PID_0137, UsagePage 0xFF00).
/// Verified to use HidD_SetOutputReport + HidD_GetInputReport (not interrupt FileStream reads).
/// </summary>
public sealed class GpdControllerService
{
    private const ushort GpdVid = 0x2F24;
    private const ushort GpdV2Pid = 0x0137;
    private const ushort GpdVendorUsagePage = 0xFF00;

    private const byte CmdHandshake = 0x10;
    private const byte CmdVersion = 0x11;
    private const byte CmdPrepareWrite = 0x20;
    private const byte CmdCommitWrite = 0x21;
    private const byte CmdEndSession = 0x22;
    private const byte CmdReadChunk = 0x44;

    private readonly object _hidLock = new();
    private GpdControllerConfig? _cachedConfig;

    public GpdControllerConfig? CachedConfig => _cachedConfig;

    public GpdControllerConfig ReadConfig(bool forceRefresh = false)
    {
        lock (_hidLock)
        {
            if (!forceRefresh && _cachedConfig is { Connected: true })
            {
                return _cachedConfig;
            }

            string? path = FindControllerV2Path();
            if (string.IsNullOrWhiteSpace(path))
            {
                return new GpdControllerConfig
                {
                    Connected = false,
                    StatusMessage = "未检测到 GPD Controller V2 (2F24:0137)；请确认掌机手柄开关处于手柄模式"
                };
            }

            using var handle = OpenHidDevice(path);
            if (handle.IsInvalid)
            {
                return new GpdControllerConfig
                {
                    Connected = false,
                    StatusMessage = $"打开 GPD 手柄 HID 设备失败 (Win32={Marshal.GetLastWin32Error()})"
                };
            }

            try
            {
                var hs = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdHandshake));
                if (hs is null || hs[8] != 0xAA)
                {
                    return new GpdControllerConfig
                    {
                        Connected = false,
                        StatusMessage = "GPD 手柄 V2 协议握手未响应 (0xAA 校验失败)"
                    };
                }

                string fwX = string.Empty;
                string fwK = string.Empty;
                var verResp = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdVersion));
                if (verResp is not null)
                {
                    fwX = $"{verResp[9]:X}.{verResp[10]:X2}";
                    fwK = $"{verResp[11]:X}.{verResp[12]:X2}";
                }

                var configBuf = new byte[GpdV2PacketCodec.ConfigBufferSize];
                for (int p = 0; p < 32; p++)
                {
                    var chunkResp = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdReadChunk, (byte)(p * 2)), delayMs: 25);
                    if (chunkResp is null)
                    {
                        return new GpdControllerConfig
                        {
                            Connected = false,
                            StatusMessage = $"读取手柄板载配置分片 {p}/32 超时"
                        };
                    }
                    Buffer.BlockCopy(chunkResp, 8, configBuf, p * 32, 32);
                }

                var decoded = GpdV2PacketCodec.Decode(configBuf, fwX, fwK);
                decoded.StatusMessage = decoded.ChecksumValid
                    ? $"已读取手柄板载配置 · 固件 X{fwX} / K{fwK}"
                    : "已读取配置但校验和异常，请勿覆盖写入";
                _cachedConfig = decoded;
                return decoded;
            }
            finally
            {
                try
                {
                    HidD_SetOutputReport(handle, GpdV2PacketCodec.BuildCommandReport(CmdEndSession), 64);
                }
                catch { }
            }
        }
    }

    public GpdControllerConfig WriteConfig(GpdControllerUpdateRequest request)
    {
        lock (_hidLock)
        {
            string? path = FindControllerV2Path();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("未检测到 GPD Controller V2 (2F24:0137)，无法写入板载配置。");
            }

            using var handle = OpenHidDevice(path);
            if (handle.IsInvalid)
            {
                throw new InvalidOperationException($"打开手柄 HID 通道失败 (Win32={Marshal.GetLastWin32Error()})。");
            }

            try
            {
                var hs = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdHandshake));
                if (hs is null || hs[8] != 0xAA)
                {
                    throw new InvalidOperationException("GPD 手柄协议握手失败，未修改任何配置。");
                }

                var verResp = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdVersion));
                string fwX = verResp is not null ? $"{verResp[9]:X}.{verResp[10]:X2}" : "";
                string fwK = verResp is not null ? $"{verResp[11]:X}.{verResp[12]:X2}" : "";

                // 1. Always read full current 1024-byte block first so untouched buttons/calibration remain intact
                var configBuf = new byte[GpdV2PacketCodec.ConfigBufferSize];
                for (int p = 0; p < 32; p++)
                {
                    var chunkResp = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdReadChunk, (byte)(p * 2)), delayMs: 25)
                        ?? throw new InvalidOperationException($"读取现有手柄配置分片 {p}/32 失败，写入已中止。");
                    Buffer.BlockCopy(chunkResp, 8, configBuf, p * 32, 32);
                }

                // 2. Patch only requested fields + recompute header checksums
                GpdV2PacketCodec.ApplyUpdateInPlace(configBuf, request);

                // 3. Prepare -> send 20 x 52-byte chunks (total 1024B) -> commit Flash
                var prep = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdPrepareWrite), delayMs: 80);
                if (prep is null)
                {
                    throw new InvalidOperationException("手柄未响应准备写入指令 (0x20)。");
                }

                for (int p = 0; p < 20; p++)
                {
                    var pkt = GpdV2PacketCodec.BuildWriteChunkReport(configBuf, p);
                    if (!HidD_SetOutputReport(handle, pkt, 64))
                    {
                        throw new InvalidOperationException($"写入手柄分片 {p}/20 失败 (Win32={Marshal.GetLastWin32Error()})。");
                    }
                    Thread.Sleep(35);
                }

                Thread.Sleep(100);
                var commitResp = SendAndRecv(handle, GpdV2PacketCodec.BuildCommandReport(CmdCommitWrite), delayMs: 160);
                if (commitResp is null)
                {
                    throw new InvalidOperationException("手柄未确认烧录提交 (0x21)。");
                }

                var result = GpdV2PacketCodec.Decode(configBuf, fwX, fwK);
                result.StatusMessage = $"已成功写入 GPD 手柄板载芯片（固件 X{fwX}），重启依然保留";
                _cachedConfig = result;
                return result;
            }
            finally
            {
                try
                {
                    HidD_SetOutputReport(handle, GpdV2PacketCodec.BuildCommandReport(CmdEndSession), 64);
                }
                catch { }
            }
        }
    }

    private static byte[]? SendAndRecv(SafeFileHandle handle, byte[] outReport, int delayMs = 50)
    {
        if (!HidD_SetOutputReport(handle, outReport, 64))
        {
            return null;
        }
        Thread.Sleep(delayMs);

        var inReport = new byte[64];
        inReport[0] = 0x01;
        return HidD_GetInputReport(handle, inReport, 64) ? inReport : null;
    }

    private static SafeFileHandle OpenHidDevice(string path) =>
        CreateFile(
            path,
            0xC0000000u, // GENERIC_READ | GENERIC_WRITE
            0x00000003u, // FILE_SHARE_READ | FILE_SHARE_WRITE
            0,
            3, // OPEN_EXISTING
            0,
            0);

    private static string? FindControllerV2Path()
    {
        HidD_GetHidGuid(out Guid hidGuid);
        const uint digcfPresent = 0x02;
        const uint digcfDeviceInterface = 0x10;
        nint devInfo = SetupDiGetClassDevs(ref hidGuid, null, 0, digcfPresent | digcfDeviceInterface);
        if (devInfo == 0 || devInfo == -1) return null;

        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint idx = 0; SetupDiEnumDeviceInterfaces(devInfo, 0, ref hidGuid, idx, ref ifaceData); idx++)
            {
                SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, 0, 0, out uint reqSize, 0);
                if (reqSize == 0) continue;

                nint detailBuf = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, detailBuf, reqSize, out _, 0))
                    {
                        continue;
                    }

                    string? devPath = Marshal.PtrToStringUni(detailBuf + 4);
                    if (string.IsNullOrWhiteSpace(devPath) ||
                        !devPath.Contains("vid_2f24", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var handle = OpenHidDevice(devPath);
                    if (handle.IsInvalid) continue;

                    var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(handle, ref attrs)) continue;
                    if (attrs.VendorID != GpdVid || attrs.ProductID != GpdV2Pid) continue;

                    if (HidD_GetPreparsedData(handle, out nint ppd) && ppd != 0)
                    {
                        try
                        {
                            if (HidP_GetCaps(ppd, out var caps) >= 0 &&
                                caps.UsagePage == GpdVendorUsagePage &&
                                caps.InputReportByteLength == 64 &&
                                caps.OutputReportByteLength == 64)
                            {
                                return devPath;
                            }
                        }
                        finally
                        {
                            HidD_FreePreparsedData(ppd);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }
        return null;
    }

    #region HID & SetupAPI P/Invokes

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    #endregion
}
