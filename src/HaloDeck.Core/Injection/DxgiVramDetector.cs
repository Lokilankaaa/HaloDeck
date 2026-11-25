using System.Runtime.InteropServices;

namespace HaloDeck.Core.Injection;

public static class DxgiVramDetector
{
    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint factory, uint adapterIndex, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint adapter, out DxgiAdapterDesc1 desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);

    /// <summary>
    /// Detects the physical GPU's dedicated VRAM size in MB via DXGI, preferring non-software AMD adapters.
    /// Returns 0 if DXGI enumeration fails or only software adapters are present.
    /// </summary>
    public static int TryDetectDedicatedVramMb()
    {
        nint factoryPtr = nint.Zero;
        try
        {
            if (CreateDXGIFactory1(in IidDxgiFactory1, out factoryPtr) < 0 || factoryPtr == nint.Zero)
            {
                return 0;
            }

            nint vtable = Marshal.ReadIntPtr(factoryPtr);
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                Marshal.ReadIntPtr(vtable, 12 * nint.Size));

            nuint bestDedicated = 0;
            bool foundAmd = false;

            for (uint idx = 0; idx < 8; idx++)
            {
                if (enumAdapters1(factoryPtr, idx, out nint adapterPtr) < 0 || adapterPtr == nint.Zero)
                {
                    break;
                }

                try
                {
                    nint adapterVtable = Marshal.ReadIntPtr(adapterPtr);
                    var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                        Marshal.ReadIntPtr(adapterVtable, 10 * nint.Size));

                    if (getDesc1(adapterPtr, out var desc) >= 0 && (desc.Flags & 2) == 0)
                    {
                        bool isAmd = desc.VendorId == 0x1002;
                        if (isAmd && (!foundAmd || desc.DedicatedVideoMemory > bestDedicated))
                        {
                            foundAmd = true;
                            bestDedicated = desc.DedicatedVideoMemory;
                        }
                        else if (!foundAmd && desc.DedicatedVideoMemory > bestDedicated)
                        {
                            bestDedicated = desc.DedicatedVideoMemory;
                        }
                    }
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }

            return bestDedicated > 0
                ? (int)Math.Round(bestDedicated / (1024.0 * 1024.0))
                : 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (factoryPtr != nint.Zero)
            {
                try { Marshal.Release(factoryPtr); } catch { }
            }
        }
    }
}
