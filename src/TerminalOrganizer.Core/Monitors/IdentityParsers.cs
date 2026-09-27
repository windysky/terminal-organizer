using System;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// The monitor-id and instance segments of a display device interface path
    /// (REQ-ID-001). Both are empty when the path does not parse.
    /// </summary>
    public sealed class InterfacePathParts
    {
        public static readonly InterfacePathParts Empty = new InterfacePathParts(string.Empty, string.Empty);

        private readonly string monitorId;
        private readonly string instance;

        public InterfacePathParts(string monitorId, string instance)
        {
            this.monitorId = monitorId;
            this.instance = instance;
        }

        public string MonitorId { get { return monitorId; } }
        public string Instance { get { return instance; } }

        public override string ToString()
        {
            return string.Format("{0}|{1}", monitorId, instance);
        }
    }

    /// <summary>
    /// Parses the documented display device interface path shape
    /// {@code \\?\DISPLAY#<monitor-id>#<instance>#{interface-guid}} into its id and instance
    /// segments (REQ-ID-001). Ordinal, never throws: an input that does not parse yields
    /// empty components (SPEC-LAYOUT-001 REQ-TC-002 data-content rule).
    /// </summary>
    public static class InterfacePathParser
    {
        // @MX:NOTE: shapes pinned by the SPEC-LAYOUT-001 research.md real fixtures (entry 14).
        private const string Prefix = "\\\\?\\DISPLAY#";
        private const int MinimumSegments = 3;

        public static InterfacePathParts Parse(string interfacePath)
        {
            if (interfacePath == null
                || interfacePath.Length < Prefix.Length
                || !interfacePath.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return InterfacePathParts.Empty;
            }
            string[] segments = interfacePath.Substring(Prefix.Length).Split('#');
            if (segments.Length < MinimumSegments
                || segments[0].Length == 0
                || segments[1].Length == 0)
            {
                return InterfacePathParts.Empty;
            }
            return new InterfacePathParts(segments[0], segments[1]);
        }
    }

    /// <summary>
    /// Extracts the serial string from EDID bytes (REQ-ID-002): the ASCII text of the first
    /// block-0 descriptor whose tag is 0xFF, with leading spaces, trailing spaces, trailing
    /// 0x0A and trailing NUL bytes removed. Never throws; no descriptor yields the empty string.
    /// </summary>
    public static class EdidParser
    {
        // @MX:NOTE: shapes pinned by the SPEC-LAYOUT-001 research.md real fixtures (YMYH14CO3VKS).
        private static readonly int[] DescriptorOffsets = new int[] { 54, 72, 90, 108 };
        private const int DescriptorLength = 18;
        private const byte SerialDescriptorTag = 0xFF;
        private const int TextOffsetInDescriptor = 4;
        private const int TextLength = 14;

        /// <summary>REQ-ID-002: inputs shorter than 56 bytes have no descriptor at all.</summary>
        private const int MinimumInputLength = 56;

        public static string ParseSerial(byte[] edid)
        {
            if (edid == null || edid.Length < MinimumInputLength)
            {
                return string.Empty;
            }
            foreach (int offset in DescriptorOffsets)
            {
                // A slot that cannot hold a complete 18-byte descriptor ends the scan; the
                // offsets ascend, so no later slot can be complete either (plan.md J.0 pins:
                // inputs of 56-71 bytes yield the empty string).
                if (offset + DescriptorLength > edid.Length)
                {
                    break;
                }
                if (!IsSerialDescriptor(edid, offset))
                {
                    continue;
                }
                return ExtractText(edid, offset);
            }
            return string.Empty;
        }

        /// <summary>
        /// Two descriptor shapes carry the 0xFF serial tag, both with their data area in bytes
        /// 4..17: the plan.md J.2 synthetic form (FF 00 00 00) and the real-hardware monitor
        /// descriptor form (00 00 00 FF — three zero flag bytes, tag at byte 3; live probe
        /// 2026-09-25, HKLM ...\DISPLAY\DEL30A9\...\Device Parameters\EDID, descriptor 2).
        /// </summary>
        private static bool IsSerialDescriptor(byte[] edid, int offset)
        {
            if (edid[offset] == SerialDescriptorTag)
            {
                return true;
            }
            return edid[offset] == 0x00 && edid[offset + 1] == 0x00
                && edid[offset + 2] == 0x00 && edid[offset + 3] == SerialDescriptorTag;
        }

        /// <summary>
        /// Reads the 14 data bytes at offset+4..offset+17. Leading NUL, space and control
        /// padding is skipped (the real-hardware form pads byte 4; REQ-ID-002 removes leading
        /// spaces); after the first text byte, a byte below 0x20 terminates the string
        /// (plan.md J.0 interior rule), and trailing spaces, 0x0A and NUL bytes are removed.
        /// </summary>
        private static string ExtractText(byte[] edid, int offset)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder(TextLength);
            bool started = false;
            for (int i = 0; i < TextLength; i++)
            {
                byte value = edid[offset + TextOffsetInDescriptor + i];
                if (!started)
                {
                    if (value <= 0x20)
                    {
                        continue;
                    }
                    started = true;
                    text.Append((char)value);
                    continue;
                }
                if (value < 0x20)
                {
                    break;
                }
                text.Append((char)value);
            }
            return text.ToString().TrimEnd(' ', '\n', '\0');
        }
    }
}
