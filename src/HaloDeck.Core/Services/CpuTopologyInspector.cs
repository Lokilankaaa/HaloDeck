using System.Runtime.InteropServices;

namespace HaloDeck.Core.Services;

public enum CpuTopologyKind
{
    SingleCcdHomogeneous,
    DualCcdHomogeneous,
    HybridBigLittle
}

public sealed record CpuSetCoreDescriptor(
    uint CpuSetId,
    ushort Group,
    byte LogicalProcessorIndex,
    byte CoreIndex,
    byte LastLevelCacheIndex,
    byte EfficiencyClass);

public sealed class CpuTopologySnapshot
{
    public CpuTopologyKind Kind { get; init; } = CpuTopologyKind.SingleCcdHomogeneous;
    public int PhysicalCoreCount { get; init; }
    public int LogicalProcessorCount { get; init; }
    public int PerformanceCoreCount { get; init; }
    public int EfficiencyCoreCount { get; init; }
    public int L3ClusterCount { get; init; } = 1;
    public int TotalL3SizeMb { get; init; }
    public string SummaryBadge { get; init; } = string.Empty;
    public string StrategyHint { get; init; } = string.Empty;
    public uint[] AllCpuSetIds { get; init; } = Array.Empty<uint>();
    public uint[] BackgroundCpuSetIds { get; init; } = Array.Empty<uint>();
    public uint[] PrimaryGameCpuSetIds { get; init; } = Array.Empty<uint>();
}

/// <summary>
/// Probes Windows CPU Sets &amp; L3 Cache topology to distinguish:
/// 1) Single-CCD homogeneous Zen cores (e.g. Ryzen AI Max+ 388, 8840U, Z1E)
/// 2) Dual-CCD homogeneous Zen cores (e.g. Ryzen AI Max+ 395)
/// 3) Hybrid Zen + Zen-c cores (e.g. HX 370, Ryzen Z2 Extreme)
/// </summary>
public static class CpuTopologyInspector
{
    private static readonly Lazy<CpuTopologySnapshot> CachedProbe = new(ProbeCurrentSystemInternal);

    public static CpuTopologySnapshot GetCurrent() => CachedProbe.Value;

    public static CpuTopologySnapshot Analyze(
        IReadOnlyList<CpuSetCoreDescriptor> descriptors,
        IReadOnlyList<int>? l3CacheSizesKb = null)
    {
        if (descriptors is null || descriptors.Count == 0)
        {
            int fallbackLogical = Math.Max(1, Environment.ProcessorCount);
            return new CpuTopologySnapshot
            {
                Kind = CpuTopologyKind.SingleCcdHomogeneous,
                PhysicalCoreCount = Math.Max(1, fallbackLogical / 2),
                LogicalProcessorCount = fallbackLogical,
                PerformanceCoreCount = Math.Max(1, fallbackLogical / 2),
                EfficiencyCoreCount = 0,
                L3ClusterCount = 1,
                TotalL3SizeMb = 0,
                SummaryBadge = $"{Math.Max(1, fallbackLogical / 2)}核 {fallbackLogical}线程",
                StrategyHint = "闲置核休眠 + EPP 稳压"
            };
        }

        var ordered = descriptors
            .OrderBy(d => d.Group)
            .ThenBy(d => d.CoreIndex)
            .ThenBy(d => d.LogicalProcessorIndex)
            .ToList();

        var cores = ordered
            .GroupBy(d => (d.Group, d.CoreIndex))
            .OrderBy(g => g.Key.Group)
            .ThenBy(g => g.Key.CoreIndex)
            .ToList();

        int physicalCores = cores.Count;
        int logicalCount = ordered.Count;
        var distinctEff = ordered.Select(d => d.EfficiencyClass).Distinct().OrderBy(x => x).ToList();
        var distinctLlc = ordered.Select(d => d.LastLevelCacheIndex).Distinct().OrderBy(x => x).ToList();

        int totalL3Kb = l3CacheSizesKb is { Count: > 0 } ? l3CacheSizesKb.Sum() : 0;
        int totalL3Mb = totalL3Kb >= 1024 ? (int)Math.Round(totalL3Kb / 1024.0) : 0;
        int l3Clusters = Math.Max(distinctLlc.Count, l3CacheSizesKb?.Count ?? 0);
        if (l3Clusters <= 0) l3Clusters = 1;

        uint[] allIds = ordered.Select(d => d.CpuSetId).Distinct().ToArray();
        string l3Suffix = totalL3Mb > 0 ? $" · {totalL3Mb}MB L3" : string.Empty;

        // Case 1: True Hybrid Big.LITTLE (e.g. Zen 5 + Zen 5c with distinct EfficiencyClass)
        if (distinctEff.Count >= 2)
        {
            byte maxEff = distinctEff[^1];
            var perfCores = cores.Where(c => c.Max(x => x.EfficiencyClass) == maxEff).ToList();
            var effCores = cores.Where(c => c.Max(x => x.EfficiencyClass) < maxEff).ToList();

            uint[] bgIds = effCores.SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray();
            uint[] gameIds = perfCores.SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray();

            return new CpuTopologySnapshot
            {
                Kind = CpuTopologyKind.HybridBigLittle,
                PhysicalCoreCount = physicalCores,
                LogicalProcessorCount = logicalCount,
                PerformanceCoreCount = perfCores.Count,
                EfficiencyCoreCount = effCores.Count,
                L3ClusterCount = l3Clusters,
                TotalL3SizeMb = totalL3Mb,
                SummaryBadge = $"异构 {perfCores.Count}大+{effCores.Count}能效核{l3Suffix}",
                StrategyHint = "后台收敛至能效核 · 大核独占稳帧 + EPP 稳压",
                AllCpuSetIds = allIds,
                BackgroundCpuSetIds = bgIds,
                PrimaryGameCpuSetIds = gameIds
            };
        }

        // Case 2: Multi-CCD Homogeneous (e.g. Ryzen AI Max+ 395 8+8 cores across 2 L3 caches)
        if (distinctLlc.Count >= 2 || (l3CacheSizesKb is { Count: >= 2 } && physicalCores >= 12))
        {
            uint[] bgIds;
            uint[] gameIds;
            if (distinctLlc.Count >= 2)
            {
                byte primaryLlc = distinctLlc[0];
                gameIds = ordered.Where(d => d.LastLevelCacheIndex == primaryLlc).Select(d => d.CpuSetId).Distinct().ToArray();
                bgIds = ordered.Where(d => d.LastLevelCacheIndex != primaryLlc).Select(d => d.CpuSetId).Distinct().ToArray();
            }
            else
            {
                int halfCores = physicalCores / 2;
                gameIds = cores.Take(halfCores).SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray();
                bgIds = cores.Skip(halfCores).SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray();
            }

            int ccdCount = Math.Max(2, l3Clusters);
            return new CpuTopologySnapshot
            {
                Kind = CpuTopologyKind.DualCcdHomogeneous,
                PhysicalCoreCount = physicalCores,
                LogicalProcessorCount = logicalCount,
                PerformanceCoreCount = physicalCores,
                EfficiencyCoreCount = 0,
                L3ClusterCount = ccdCount,
                TotalL3SizeMb = totalL3Mb,
                SummaryBadge = $"{ccdCount}CCD {physicalCores}核{l3Suffix}",
                StrategyHint = "后台隔离至副CCD · 副CCD深度休眠省电",
                AllCpuSetIds = allIds,
                BackgroundCpuSetIds = bgIds,
                PrimaryGameCpuSetIds = gameIds
            };
        }

        // Case 3: Single-CCD Homogeneous All-Big-Core (e.g. Ryzen AI Max+ 388 8C/16T)
        int tailCoreCount = physicalCores >= 8 ? 2 : (physicalCores >= 4 ? 1 : 0);
        uint[] tailBgIds = tailCoreCount > 0
            ? cores.Skip(physicalCores - tailCoreCount).SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray()
            : Array.Empty<uint>();
        uint[] primaryIds = tailCoreCount > 0
            ? cores.Take(physicalCores - tailCoreCount).SelectMany(c => c.Select(x => x.CpuSetId)).Distinct().ToArray()
            : allIds;

        return new CpuTopologySnapshot
        {
            Kind = CpuTopologyKind.SingleCcdHomogeneous,
            PhysicalCoreCount = physicalCores,
            LogicalProcessorCount = logicalCount,
            PerformanceCoreCount = physicalCores,
            EfficiencyCoreCount = 0,
            L3ClusterCount = 1,
            TotalL3SizeMb = totalL3Mb,
            SummaryBadge = $"单CCD {physicalCores}核{l3Suffix}",
            StrategyHint = tailCoreCount > 0
                ? $"闲置核 CC6 深睡 + EPP 稳压 · 后台收敛末尾 {tailCoreCount} 核"
                : "闲置核 CC6 深睡 + EPP 稳压",
            AllCpuSetIds = allIds,
            BackgroundCpuSetIds = tailBgIds,
            PrimaryGameCpuSetIds = primaryIds
        };
    }

    private static CpuTopologySnapshot ProbeCurrentSystemInternal()
    {
        var descriptors = QuerySystemCpuSets();
        var l3SizesKb = QueryL3CacheSizesKb();
        return Analyze(descriptors, l3SizesKb);
    }

    private static List<CpuSetCoreDescriptor> QuerySystemCpuSets()
    {
        var list = new List<CpuSetCoreDescriptor>();
        try
        {
            GetSystemCpuSetInformation(0, 0, out uint needed, 0, 0);
            if (needed == 0)
            {
                return list;
            }

            nint buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, needed, out uint returned, 0, 0))
                {
                    return list;
                }

                int offset = 0;
                while (offset + 24 <= returned)
                {
                    int size = Marshal.ReadInt32(buffer, offset);
                    int type = Marshal.ReadInt32(buffer, offset + 4);
                    if (size <= 0)
                    {
                        break;
                    }

                    if (type == 0 && offset + 20 <= returned)
                    {
                        uint id = (uint)Marshal.ReadInt32(buffer, offset + 8);
                        ushort group = (ushort)Marshal.ReadInt16(buffer, offset + 12);
                        byte lpIdx = Marshal.ReadByte(buffer, offset + 14);
                        byte coreIdx = Marshal.ReadByte(buffer, offset + 15);
                        byte llcIdx = Marshal.ReadByte(buffer, offset + 16);
                        byte effClass = Marshal.ReadByte(buffer, offset + 18);
                        list.Add(new CpuSetCoreDescriptor(id, group, lpIdx, coreIdx, llcIdx, effClass));
                    }

                    offset += size;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Fall back gracefully if OS does not expose CPU Set table
        }

        return list;
    }

    private static List<int> QueryL3CacheSizesKb()
    {
        var sizesKb = new List<int>();
        try
        {
            const int RelationCache = 2;
            uint len = 0;
            GetLogicalProcessorInformationEx(RelationCache, 0, ref len);
            if (len == 0)
            {
                return sizesKb;
            }

            nint buffer = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationCache, buffer, ref len))
                {
                    return sizesKb;
                }

                int offset = 0;
                while (offset + 24 <= len)
                {
                    int rel = Marshal.ReadInt32(buffer, offset);
                    int size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size <= 0)
                    {
                        break;
                    }

                    if (rel == RelationCache)
                    {
                        byte cacheLevel = Marshal.ReadByte(buffer, offset + 8);
                        int cacheSizeBytes = Marshal.ReadInt32(buffer, offset + 12);
                        if (cacheLevel == 3 && cacheSizeBytes > 0)
                        {
                            sizesKb.Add(cacheSizeBytes / 1024);
                        }
                    }

                    offset += size;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Ignore L3 probe failures
        }

        return sizesKb;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(
        nint Information,
        uint BufferLength,
        out uint ReturnedLength,
        nint Process,
        uint Flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        int RelationshipType,
        nint Buffer,
        ref uint ReturnedLength);
}
