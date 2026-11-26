using System.Runtime.InteropServices;
using HaloDeck.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace HaloDeck.App.Native;

/// <summary>
/// Direct non-admin HID V2 communication for GPD Win 5 controller (VID_2F24&amp;PID_0137, UsagePage 0xFF00).
/// Verified to use HidD_SetOutputReport + HidD_GetInputReport (not interrupt FileStream reads).
/// </summary>
public sealed class GpdControllerService
{
    private const ushort GpdVid = 0x2F24;
    private const ushort GpdV2Pid = 0x0137;
    private const ushort GpdVendorUsagePage = 0xFF00;

    private const byte CmdV2Init1 = 0x21;
    private const byte CmdV2EndSession = 0x22;
    private const byte CmdV225 = 0x25;
    private const byte CmdV2Checksum = 0x27;
    private const byte CmdV2FlashLock = 0x29;
    private const byte CmdV2FlashUnlock = 0x2A;
    private const byte CmdV2Init2 = 0x2B;
    private const byte CmdV2Version = 0x41;
    private const byte CmdV2Write = 0x43;
    private const byte CmdV2Read = 0x44;

    private readonly object _hidLock = new();
    private static int _configTransferDepth;
    private GpdControllerConfig? _cachedConfig;
    private byte[]? _cachedRawConfigBuf;
    private string _cachedFwX = string.Empty;
    private string _cachedFwK = string.Empty;

    internal static bool IsConfigTransferActive => Volatile.Read(ref _configTransferDepth) > 0;

    public GpdControllerConfig? CachedConfig => _cachedConfig;

    public GpdControllerConfig ReadConfig(bool forceRefresh = false)
    {
        lock (_hidLock)
        {
            if (!forceRefresh && _cachedConfig is { Connected: true })
            {
                return _cachedConfig;
            }

            Interlocked.Increment(ref _configTransferDepth);
            try
            {
                string? path = FindControllerV2Path(out ushort? otherGpdPid);
                if (string.IsNullOrWhiteSpace(path))
                {
                    string msg = otherGpdPid.HasValue
                        ? $"检测到 GPD 手柄 (2F24:{otherGpdPid.Value:X4})，暂未适配该型号背键协议（其他功耗/OSD/超分功能正常）"
                        : "未检测到 GPD Controller V2 (2F24:0137)；请确认掌机手柄开关处于手柄模式";
                    return new GpdControllerConfig
                    {
                        Connected = false,
                        StatusMessage = msg
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
                    var (fwX, fwK, okVer, verErr) = ReadFirmwareVersionV2(handle);
                    if (!okVer)
                    {
                        return new GpdControllerConfig
                        {
                            Connected = false,
                            StatusMessage = verErr
                        };
                    }

                    var (configBuf, readErr) = ReadConfigBufferV2(handle);
                    if (configBuf is null)
                    {
                        return new GpdControllerConfig
                        {
                            Connected = false,
                            FirmwareX = fwX,
                            FirmwareK = fwK,
                            StatusMessage = readErr
                        };
                    }

                    var decoded = GpdV2PacketCodec.Decode(configBuf, fwX, fwK);
                    decoded.StatusMessage = decoded.ChecksumValid
                        ? $"已连接 Controller V2 · 固件 X{fwX} / K{fwK}"
                        : "已读取配置但校验和异常，请勿覆盖写入";
                    if (decoded.ChecksumValid)
                    {
                        _cachedRawConfigBuf = (byte[])configBuf.Clone();
                        _cachedFwX = fwX;
                        _cachedFwK = fwK;
                    }
                    _cachedConfig = decoded;
                    return decoded;
                }
                finally
                {
                    TrySendEndCommand(handle);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _configTransferDepth);
            }
        }
    }

    public GpdControllerConfig WriteConfig(GpdControllerUpdateRequest request)
    {
        lock (_hidLock)
        {
            Interlocked.Increment(ref _configTransferDepth);
            try
            {
                string? path = FindControllerV2Path(out ushort? otherGpdPid);
                if (string.IsNullOrWhiteSpace(path))
                {
                    string msg = otherGpdPid.HasValue
                        ? $"检测到 GPD 手柄 (2F24:{otherGpdPid.Value:X4})，暂未适配该型号背键写入协议。"
                        : "未检测到 GPD Controller V2 (2F24:0137)，无法写入板载配置。";
                    throw new InvalidOperationException(msg);
                }

                using var handle = OpenHidDevice(path);
                if (handle.IsInvalid)
                {
                    throw new InvalidOperationException($"打开手柄 HID 通道失败 (Win32={Marshal.GetLastWin32Error()})。");
                }

                try
                {
                    byte[] configBuf;
                    string fwX;
                    string fwK;

                    // Reuse the validated 1024-byte raw buffer cached from ReadConfig() to skip ~800ms of redundant
                    // firmware version + full-buffer readback before writing.
                    if (_cachedRawConfigBuf is { Length: GpdV2PacketCodec.ConfigBufferSize } cached &&
                        GpdV2PacketCodec.VerifyChecksum(cached))
                    {
                        configBuf = (byte[])cached.Clone();
                        fwX = _cachedFwX;
                        fwK = _cachedFwK;
                    }
                    else
                    {
                        var (readFwX, readFwK, okVer, verErr) = ReadFirmwareVersionV2(handle);
                        if (!okVer)
                        {
                            throw new InvalidOperationException(verErr);
                        }

                        var (readBuf, readErr) = ReadConfigBufferV2(handle);
                        if (readBuf is null)
                        {
                            throw new InvalidOperationException(readErr);
                        }

                        configBuf = readBuf;
                        fwX = readFwX;
                        fwK = readFwK;
                    }

                    // 2. Patch only requested fields + recompute header checksums
                    GpdV2PacketCodec.ApplyUpdateInPlace(configBuf, request);

                    // 3. V2 MemWrite (Init1 -> 56-byte page writes 0x43 -> Checksum 0x27 -> End)
                    WriteConfigMemV2(handle, configBuf);

                    // 4. V2 Cmd25 commit handshake (Init1 -> Cmd25 0x25 -> End)
                    SendCmd25V2(handle);

                    // 5. V2 Flash persist (Init1 -> Init2 -> FlashLock 0x29 -> 500ms -> FlashUnlock 0x2A -> End)
                    FlashConfigV2(handle);

                    _cachedRawConfigBuf = (byte[])configBuf.Clone();
                    _cachedFwX = fwX;
                    _cachedFwK = fwK;

                    var result = GpdV2PacketCodec.Decode(configBuf, fwX, fwK);
                    result.StatusMessage = $"已写入 GPD Controller V2 板载 Flash（固件 X{fwX}），重启依然保留";
                    _cachedConfig = result;
                    return result;
                }
                finally
                {
                    TrySendEndCommand(handle);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _configTransferDepth);
            }
        }
    }

    private static bool SendInitV2(SafeFileHandle handle, bool withInit2)
    {
        var init1 = SendAndRecv(handle, GpdV2PacketCodec.BuildV2Init1Report(), delayMs: 28);
        if (init1 is null || init1[8] != 0xAA)
        {
            return false;
        }
        if (!withInit2)
        {
            return true;
        }
        var init2 = SendAndRecv(handle, GpdV2PacketCodec.BuildV2Init2Report(), delayMs: 28);
        return init2 is not null && init2[8] == 0xAA;
    }

    private static (string FwX, string FwK, bool Ok, string Error) ReadFirmwareVersionV2(SafeFileHandle handle)
    {
        if (!SendInitV2(handle, withInit2: true))
        {
            return ("", "", false, "GPD Controller V2 初始化握手未响应 (0x21/0x2B)");
        }

        var verResp = SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2Version), delayMs: 32);
        if (verResp is null || !GpdV2PacketCodec.IsValidV2Response(verResp))
        {
            return ("", "", false, "读取 GPD V2 固件版本响应无效");
        }

        string fwX = $"{verResp[10]}.{verResp[11]:D2}";
        string fwK = $"{verResp[12]}.{verResp[13]:D2}";
        return (fwX, fwK, true, string.Empty);
    }

    private static (byte[]? Buffer, string Error) ReadConfigBufferV2(SafeFileHandle handle)
    {
        if (!SendInitV2(handle, withInit2: true))
        {
            return (null, "GPD Controller V2 读取握手未响应 (0x21/0x2B)");
        }

        var readCmd = GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2Read, bytesCount: 2, withMagic4: true);
        if (!HidD_SetOutputReport(handle, readCmd, 64))
        {
            return (null, $"下发 V2 读取配置命令失败 (Win32={Marshal.GetLastWin32Error()})");
        }

        var configBuf = new byte[GpdV2PacketCodec.ConfigBufferSize];
        int offset = 0;
        int packets = 0;
        while (offset < GpdV2PacketCodec.ConfigBufferSize && packets < 32)
        {
            packets++;
            Thread.Sleep(5);
            var inReport = new byte[64];
            inReport[0] = 0x01;
            if (!HidD_GetInputReport(handle, inReport, 64))
            {
                return (null, $"读取手柄 V2 配置数据流超时 (已读 {offset}/1024B)");
            }
            if (!GpdV2PacketCodec.IsValidV2Response(inReport))
            {
                return (null, $"手柄 V2 配置分片 #{packets} 校验和错误或被拒绝");
            }

            int len = inReport[2];
            if (len <= 0 || offset + len > GpdV2PacketCodec.ConfigBufferSize)
            {
                return (null, $"手柄 V2 分片长度异常 ({len}B @ {offset})");
            }

            Buffer.BlockCopy(inReport, 8, configBuf, offset, len);
            offset += len;
        }

        if (offset != GpdV2PacketCodec.ConfigBufferSize)
        {
            return (null, $"读取手柄 V2 配置不完整 ({offset}/1024B)");
        }

        // Match libOpenWinControls ControllerV2::readConfig() tail (Init2 -> Checksum 0x27 -> EndCmd 0x22)
        // so the MCU cleanly exits the read transaction before subsequent write commands.
        if (SendAndRecv(handle, GpdV2PacketCodec.BuildV2Init2Report(), delayMs: 28) is { } init2Resp && init2Resp[8] == 0xAA)
        {
            SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2Checksum, bytesCount: 2, withMagic4: true), delayMs: 32);
        }
        SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2EndSession), delayMs: 20);

        return (configBuf, string.Empty);
    }

    private static void WriteConfigMemV2(SafeFileHandle handle, byte[] configBuf)
    {
        if (!SendInitV2(handle, withInit2: false))
        {
            throw new InvalidOperationException("写入 V2 配置前 Init1 握手失败，未改动芯片内容。");
        }

        GpdV2PacketCodec.UpdateConfigHeaderChecksum(configBuf);

        for (int offset = 0; offset < GpdV2PacketCodec.ConfigBufferSize; offset += 56)
        {
            int count = Math.Min(GpdV2PacketCodec.ConfigBufferSize - offset, 56);
            var pkt = GpdV2PacketCodec.BuildV2WritePageReport((ushort)offset, configBuf.AsSpan(offset, count));
            if (!HidD_SetOutputReport(handle, pkt, 64))
            {
                throw new InvalidOperationException($"写入 V2 分片 @{offset} 失败 (Win32={Marshal.GetLastWin32Error()})。");
            }
            Thread.Sleep(4);
        }

        // Match libOpenWinControls ControllerV2::memWriteConfig(): send Checksum (0x27) directly after page writes
        // (without an intervening Init2) to verify and commit the buffer to controller RAM.
        ushort expectedSum = GpdV2PacketCodec.ComputeBytesSum(configBuf);
        var chkResp = SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2Checksum, bytesCount: 2, withMagic4: true), delayMs: 40);
        if (chkResp is null || chkResp[8] == 0xE2)
        {
            throw new InvalidOperationException("手柄未响应 V2 整块校验请求 (0x27)。");
        }

        ushort devSum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chkResp.AsSpan(10, 2));
        if (devSum != expectedSum)
        {
            throw new InvalidOperationException($"手柄 V2 写入整块校验不一致 (0x{devSum:X4} != 0x{expectedSum:X4})。");
        }

        SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2EndSession), delayMs: 22);
    }

    private static void SendCmd25V2(SafeFileHandle handle)
    {
        if (!SendInitV2(handle, withInit2: false))
        {
            throw new InvalidOperationException("V2 提交阶段 Init1 失败。");
        }

        var r25 = SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV225, bytesCount: 4, withMagic4: true), delayMs: 35);
        if (r25 is null || r25[8] == 0xE2)
        {
            throw new InvalidOperationException("手柄拒绝执行 V2 提交命令 (0x25)。");
        }

        SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2EndSession), delayMs: 22);
    }

    private static void FlashConfigV2(SafeFileHandle handle)
    {
        if (!SendInitV2(handle, withInit2: true))
        {
            throw new InvalidOperationException("V2 烧录 Flash 初始化失败。");
        }

        var rLock = SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2FlashLock), delayMs: 35);
        if (rLock is null || rLock[8] == 0xE2)
        {
            throw new InvalidOperationException("手柄拒绝 Flash 锁定命令 (0x29)。");
        }

        Thread.Sleep(500);

        var rUnlock = SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2FlashUnlock), delayMs: 35);
        if (rUnlock is null || rUnlock[8] == 0xE2)
        {
            throw new InvalidOperationException("手柄拒绝 Flash 解锁提交命令 (0x2A)。");
        }

        SendAndRecv(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2EndSession), delayMs: 22);
    }

    private static void TrySendEndCommand(SafeFileHandle handle)
    {
        try
        {
            HidD_SetOutputReport(handle, GpdV2PacketCodec.BuildV2SimpleCommand(CmdV2EndSession), 64);
        }
        catch { }
    }

    private static byte[]? SendAndRecv(SafeFileHandle handle, byte[] outReport, int delayMs = 35)
    {
        if (!HidD_SetOutputReport(handle, outReport, 64))
        {
            return null;
        }
        Thread.Sleep(delayMs);

        var inReport = new byte[64];
        inReport[0] = 0x01;
        if (HidD_GetInputReport(handle, inReport, 64))
        {
            return inReport;
        }

        // Single retry with a slightly longer settle window for slower firmware responses
        Thread.Sleep(35);
        Array.Clear(inReport, 0, inReport.Length);
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

    private static string? FindControllerV2Path(out ushort? otherGpdPid)
    {
        otherGpdPid = null;
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
                    if (attrs.VendorID != GpdVid) continue;
                    if (attrs.ProductID != GpdV2Pid)
                    {
                        otherGpdPid ??= attrs.ProductID;
                        continue;
                    }

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

    internal static string? FindVendorHidInputPath(out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        HidD_GetHidGuid(out Guid hidGuid);
        const uint digcfPresent = 0x02;
        const uint digcfDeviceInterface = 0x10;
        nint devInfo = SetupDiGetClassDevs(ref hidGuid, null, 0, digcfPresent | digcfDeviceInterface);
        if (devInfo == 0 || devInfo == -1) return null;

        string? fallbackPath = null;
        ushort fallbackPid = 0;

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
                    if (!HidD_GetAttributes(handle, ref attrs) || attrs.VendorID != GpdVid) continue;

                    if (HidD_GetPreparsedData(handle, out nint ppd) && ppd != 0)
                    {
                        try
                        {
                            if (HidP_GetCaps(ppd, out var caps) >= 0 &&
                                caps.UsagePage == GpdVendorUsagePage &&
                                caps.Usage == 0x0001 &&
                                caps.InputReportByteLength == 64)
                            {
                                if (attrs.ProductID == GpdV2Pid)
                                {
                                    vendorId = attrs.VendorID;
                                    productId = attrs.ProductID;
                                    return devPath;
                                }

                                if (fallbackPath is null)
                                {
                                    fallbackPath = devPath;
                                    fallbackPid = attrs.ProductID;
                                }
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

        if (fallbackPath is not null)
        {
            vendorId = GpdVid;
            productId = fallbackPid;
        }
        return fallbackPath;
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
