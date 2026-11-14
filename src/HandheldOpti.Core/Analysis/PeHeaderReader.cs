using System.Buffers.Binary;
using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Analysis;

public static class PeHeaderReader
{
    public static PeArchitecture ReadArchitecture(string executablePath)
    {
        try
        {
            using var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 0x40)
            {
                return PeArchitecture.Unknown;
            }

            Span<byte> dosHeader = stackalloc byte[0x40];
            if (stream.Read(dosHeader) != 0x40)
            {
                return PeArchitecture.Unknown;
            }

            if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
            {
                return PeArchitecture.Unknown;
            }

            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[0x3C..0x40]);
            if (peOffset <= 0 || peOffset + 6 > stream.Length)
            {
                return PeArchitecture.Unknown;
            }

            stream.Position = peOffset;
            Span<byte> peHeader = stackalloc byte[6];
            if (stream.Read(peHeader) != 6)
            {
                return PeArchitecture.Unknown;
            }

            // "PE\0\0"
            if (peHeader[0] != 'P' || peHeader[1] != 'E' || peHeader[2] != 0 || peHeader[3] != 0)
            {
                return PeArchitecture.Unknown;
            }

            ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..6]);
            return machine switch
            {
                0x8664 => PeArchitecture.X64, // IMAGE_FILE_MACHINE_AMD64
                0x014C => PeArchitecture.X86, // IMAGE_FILE_MACHINE_I386
                _ => PeArchitecture.Unknown
            };
        }
        catch
        {
            return PeArchitecture.Unknown;
        }
    }
}
