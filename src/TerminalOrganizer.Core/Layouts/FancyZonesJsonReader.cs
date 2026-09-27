using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TerminalOrganizer.Core.Layouts
{
    /// <summary>
    /// Parses the text of custom-layouts.json and applied-layouts.json (REQ-PAR-001..003). Pure: no file I/O.
    /// Data content never throws: a document-level problem yields Reason MalformedJson, and an entry
    /// that FancyZones' own parser would skip becomes a RejectedEntry.
    /// </summary>
    public static class FancyZonesJsonReader
    {
        // @MX:ANCHOR: [AUTO] The only place where the FancyZones custom-layouts file format enters Core.
        // @MX:REASON: Every custom-layout consumer (resolver, SPEC-LAYOUT-002 templates, tests) depends on this mapping.
        public static CustomLayoutsDocument ParseCustomLayouts(string json)
        {
            CustomLayoutsFileDto file;
            string error;
            if (!TryRead(json, out file, out error))
            {
                return new CustomLayoutsDocument(LayoutReason.MalformedJson, error, null, null);
            }
            if (file.Layouts == null)
            {
                return new CustomLayoutsDocument(LayoutReason.MalformedJson, "missing \"custom-layouts\" array", null, null);
            }

            List<CustomLayout> layouts = new List<CustomLayout>();
            List<RejectedEntry> rejected = new List<RejectedEntry>();
            for (int i = 0; i < file.Layouts.Length; i++)
            {
                CustomLayoutDto dto = file.Layouts[i];
                LayoutReason reason;
                string detail;
                CustomLayout layout = MapCustom(i, dto, out reason, out detail);
                if (layout != null)
                {
                    layouts.Add(layout);
                }
                else
                {
                    rejected.Add(new RejectedEntry(i, dto == null ? null : dto.Uuid, null, reason, detail));
                }
            }
            return new CustomLayoutsDocument(LayoutReason.None, string.Empty, layouts.ToArray(), rejected.ToArray());
        }

        // @MX:ANCHOR: [AUTO] The only place where the FancyZones applied-layouts file format enters Core.
        // @MX:REASON: Device-key lookup and the required-field rules of AppliedLayouts.cpp:39-107 live behind this call.
        public static AppliedLayoutsDocument ParseAppliedLayouts(string json)
        {
            AppliedLayoutsFileDto file;
            string error;
            if (!TryRead(json, out file, out error))
            {
                return new AppliedLayoutsDocument(LayoutReason.MalformedJson, error, null, null);
            }
            if (file.Entries == null)
            {
                return new AppliedLayoutsDocument(LayoutReason.MalformedJson, "missing \"applied-layouts\" array", null, null);
            }

            List<AppliedLayoutEntry> entries = new List<AppliedLayoutEntry>();
            List<RejectedEntry> rejected = new List<RejectedEntry>();
            for (int i = 0; i < file.Entries.Length; i++)
            {
                AppliedEntryDto dto = file.Entries[i];
                string detail;
                AppliedLayoutEntry entry = MapApplied(i, dto, out detail);
                if (entry != null)
                {
                    entries.Add(entry);
                }
                else
                {
                    string uuid = dto != null && dto.Layout != null ? dto.Layout.Uuid : null;
                    DeviceKey device = dto == null ? null : ReadableDevice(dto.Device);
                    rejected.Add(new RejectedEntry(i, uuid, device, LayoutReason.MalformedEntry, detail));
                }
            }
            return new AppliedLayoutsDocument(LayoutReason.None, string.Empty, entries.ToArray(), rejected.ToArray());
        }

        private static CustomLayout MapCustom(int position, CustomLayoutDto dto, out LayoutReason reason, out string detail)
        {
            reason = LayoutReason.MalformedEntry;
            detail = null;
            if (dto == null) { detail = "entry is null"; return null; }
            if (dto.Uuid == null) { detail = "missing uuid"; return null; }
            if (!IsGuid(dto.Uuid)) { detail = "uuid is not a GUID: " + dto.Uuid; return null; }
            if (dto.Name == null) { detail = "missing name"; return null; }
            if (dto.Info == null) { detail = "missing info"; return null; }
            if (dto.Type == null) { detail = "missing type"; return null; }

            InfoDto info = dto.Info;
            if (string.Equals(dto.Type, "canvas", StringComparison.Ordinal))
            {
                CanvasLayoutInfo canvas = MapCanvas(info, out detail);
                return canvas == null ? null : new CustomLayout(position, dto.Uuid, dto.Name, canvas);
            }
            if (string.Equals(dto.Type, "grid", StringComparison.Ordinal))
            {
                GridLayoutInfo grid = MapGrid(info, out reason, out detail);
                return grid == null ? null : new CustomLayout(position, dto.Uuid, dto.Name, grid);
            }
            detail = "custom type is neither grid nor canvas: " + dto.Type;
            return null;
        }

        private static GridLayoutInfo MapGrid(InfoDto info, out LayoutReason reason, out string detail)
        {
            reason = LayoutReason.MalformedEntry;
            int rows;
            int columns;
            if (!TryToInt(info.Rows, out rows)) { detail = "missing or out-of-range rows"; return null; }
            if (!TryToInt(info.Columns, out columns)) { detail = "missing or out-of-range columns"; return null; }
            if (info.RowsPercentage == null) { detail = "missing rows-percentage"; return null; }
            if (info.ColumnsPercentage == null) { detail = "missing columns-percentage"; return null; }
            if (info.CellChildMap == null) { detail = "missing cell-child-map"; return null; }

            // Shape checks mirror CustomLayouts.cpp:73-76 and 83-86.
            reason = LayoutReason.ShapeMismatch;
            if (info.RowsPercentage.Length != rows || info.ColumnsPercentage.Length != columns || info.CellChildMap.Length != rows)
            {
                detail = string.Format("rows {0} / columns {1} do not match rows-percentage {2}, columns-percentage {3}, cell-child-map {4}",
                    rows, columns, info.RowsPercentage.Length, info.ColumnsPercentage.Length, info.CellChildMap.Length);
                return null;
            }
            for (int r = 0; r < rows; r++)
            {
                if (info.CellChildMap[r] == null || info.CellChildMap[r].Length != columns)
                {
                    detail = string.Format("cell-child-map row {0} does not have {1} columns", r, columns);
                    return null;
                }
            }

            reason = LayoutReason.MalformedEntry;
            int[] rowsPercentage;
            int[] columnsPercentage;
            if (!TryToIntArray(info.RowsPercentage, out rowsPercentage)) { detail = "out-of-range rows-percentage value"; return null; }
            if (!TryToIntArray(info.ColumnsPercentage, out columnsPercentage)) { detail = "out-of-range columns-percentage value"; return null; }
            int[][] map = new int[rows][];
            for (int r = 0; r < rows; r++)
            {
                if (!TryToIntArray(info.CellChildMap[r], out map[r])) { detail = "out-of-range cell-child-map value"; return null; }
            }

            int spacing;
            int sensitivityRadius;
            if (!TryToIntOrDefault(info.Spacing, FancyZonesConstants.DefaultSpacing, out spacing)) { detail = "out-of-range spacing"; return null; }
            if (!TryToIntOrDefault(info.SensitivityRadius, FancyZonesConstants.DefaultSensitivityRadius, out sensitivityRadius))
            {
                detail = "out-of-range sensitivity-radius";
                return null;
            }
            bool showSpacing = info.ShowSpacing.HasValue ? info.ShowSpacing.Value : FancyZonesConstants.DefaultShowSpacing;

            detail = null;
            reason = LayoutReason.None;
            return new GridLayoutInfo(rows, columns, rowsPercentage, columnsPercentage, map, showSpacing, spacing, sensitivityRadius);
        }

        private static CanvasLayoutInfo MapCanvas(InfoDto info, out string detail)
        {
            int refWidth;
            int refHeight;
            if (!TryToInt(info.RefWidth, out refWidth)) { detail = "missing or out-of-range ref-width"; return null; }
            if (!TryToInt(info.RefHeight, out refHeight)) { detail = "missing or out-of-range ref-height"; return null; }
            if (info.Zones == null) { detail = "missing zones"; return null; }

            CanvasZone[] zones = new CanvasZone[info.Zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                CanvasZoneDto z = info.Zones[i];
                int x;
                int y;
                int width;
                int height;
                if (z == null || !TryToInt(z.X, out x) || !TryToInt(z.Y, out y) || !TryToInt(z.Width, out width) || !TryToInt(z.Height, out height))
                {
                    detail = string.Format("canvas zone {0} lacks X, Y, width or height", i);
                    return null;
                }
                zones[i] = new CanvasZone(x, y, width, height);
            }

            int sensitivityRadius;
            if (!TryToIntOrDefault(info.SensitivityRadius, FancyZonesConstants.DefaultSensitivityRadius, out sensitivityRadius))
            {
                detail = "out-of-range sensitivity-radius";
                return null;
            }
            detail = null;
            return new CanvasLayoutInfo(refWidth, refHeight, zones, sensitivityRadius);
        }

        private static AppliedLayoutEntry MapApplied(int position, AppliedEntryDto dto, out string detail)
        {
            if (dto == null) { detail = "entry is null"; return null; }
            DeviceDto device = dto.Device;
            if (device == null) { detail = "missing device object (legacy device-id entries are not supported)"; return null; }
            if (device.Monitor == null) { detail = "missing device.monitor"; return null; }
            if (device.VirtualDesktop == null) { detail = "missing device.virtual-desktop"; return null; }
            if (!IsGuid(device.VirtualDesktop)) { detail = "virtual-desktop is not a GUID: " + device.VirtualDesktop; return null; }
            int monitorNumber;
            if (!TryToIntOrDefault(device.MonitorNumber, 0, out monitorNumber)) { detail = "out-of-range monitor-number"; return null; }

            AppliedLayoutDto layout = dto.Layout;
            if (layout == null) { detail = "missing applied-layout"; return null; }
            if (layout.Uuid == null) { detail = "missing applied-layout.uuid"; return null; }
            if (!IsGuid(layout.Uuid)) { detail = "applied-layout.uuid is not a GUID: " + layout.Uuid; return null; }
            if (layout.Type == null) { detail = "missing applied-layout.type"; return null; }
            if (!layout.ShowSpacing.HasValue) { detail = "missing applied-layout.show-spacing"; return null; }
            int spacing;
            int zoneCount;
            int sensitivityRadius;
            if (!TryToInt(layout.Spacing, out spacing)) { detail = "missing or out-of-range applied-layout.spacing"; return null; }
            if (!TryToInt(layout.ZoneCount, out zoneCount)) { detail = "missing or out-of-range applied-layout.zone-count"; return null; }
            if (!TryToIntOrDefault(layout.SensitivityRadius, FancyZonesConstants.DefaultSensitivityRadius, out sensitivityRadius))
            {
                detail = "out-of-range applied-layout.sensitivity-radius";
                return null;
            }

            detail = null;
            DeviceKey key = new DeviceKey(device.Monitor, device.MonitorInstance ?? string.Empty, monitorNumber,
                device.SerialNumber ?? string.Empty, device.VirtualDesktop);
            return new AppliedLayoutEntry(position, key, layout.Uuid, layout.Type, layout.ShowSpacing.Value, spacing, zoneCount, sensitivityRadius);
        }

        /// <summary>The device key of a rejected entry when monitor and virtual-desktop are present; null otherwise.</summary>
        private static DeviceKey ReadableDevice(DeviceDto device)
        {
            if (device == null || device.Monitor == null || device.VirtualDesktop == null)
            {
                return null;
            }
            int monitorNumber;
            if (!TryToIntOrDefault(device.MonitorNumber, 0, out monitorNumber))
            {
                return null;
            }
            return new DeviceKey(device.Monitor, device.MonitorInstance ?? string.Empty, monitorNumber,
                device.SerialNumber ?? string.Empty, device.VirtualDesktop);
        }

        /// <summary>
        /// Braced GUIDs only ("{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"), as FancyZones' CLSIDFromString requires
        /// (REQ-PAR-003).
        /// </summary>
        internal static bool IsGuid(string value)
        {
            Guid ignored;
            return value != null && Guid.TryParseExact(value, "B", out ignored);
        }

        /// <summary>Truncates toward zero like static_cast&lt;int&gt;(double); false when absent, NaN or outside Int32.</summary>
        private static bool TryToInt(double? value, out int result)
        {
            result = 0;
            if (!value.HasValue) return false;
            double d = Math.Truncate(value.Value);
            // @MX:NOTE: [AUTO] Int32 bounds: FancyZones stores these numbers in 32-bit ints; wider values are rejected, not wrapped.
            if (!(d >= int.MinValue && d <= int.MaxValue)) return false;
            result = (int)d;
            return true;
        }

        private static bool TryToIntOrDefault(double? value, int defaultValue, out int result)
        {
            if (!value.HasValue)
            {
                result = defaultValue;
                return true;
            }
            return TryToInt(value, out result);
        }

        private static bool TryToIntArray(double[] values, out int[] result)
        {
            result = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (!TryToInt(values[i], out result[i])) return false;
            }
            return true;
        }

        private static bool TryRead<T>(string json, out T value, out string error) where T : class
        {
            value = null;
            error = null;
            if (json == null)
            {
                error = "document text is null";
                return false;
            }
            string text = json.Length > 0 && json[0] == '﻿' ? json.Substring(1) : json;
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    value = serializer.ReadObject(stream) as T;
                }
            }
            catch (Exception ex)
            {
                // Data content must never throw (REQ-PAR-002): any serializer failure is MalformedJson.
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            if (value == null)
            {
                error = "document is not a JSON object";
                return false;
            }
            return true;
        }

        // ----- DataContract DTOs (internal). Numbers are double? so fractions truncate instead of failing. -----

        [DataContract]
        internal sealed class CustomLayoutsFileDto
        {
            [DataMember(Name = "custom-layouts")] public CustomLayoutDto[] Layouts { get; set; }
        }

        [DataContract]
        internal sealed class CustomLayoutDto
        {
            [DataMember(Name = "uuid")] public string Uuid { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "info")] public InfoDto Info { get; set; }
        }

        /// <summary>Union of the grid and canvas info shapes (CustomLayouts.cpp:26-101).</summary>
        [DataContract]
        internal sealed class InfoDto
        {
            [DataMember(Name = "rows")] public double? Rows { get; set; }
            [DataMember(Name = "columns")] public double? Columns { get; set; }
            [DataMember(Name = "rows-percentage")] public double[] RowsPercentage { get; set; }
            [DataMember(Name = "columns-percentage")] public double[] ColumnsPercentage { get; set; }
            [DataMember(Name = "cell-child-map")] public double[][] CellChildMap { get; set; }
            [DataMember(Name = "show-spacing")] public bool? ShowSpacing { get; set; }
            [DataMember(Name = "spacing")] public double? Spacing { get; set; }
            [DataMember(Name = "sensitivity-radius")] public double? SensitivityRadius { get; set; }
            [DataMember(Name = "ref-width")] public double? RefWidth { get; set; }
            [DataMember(Name = "ref-height")] public double? RefHeight { get; set; }
            [DataMember(Name = "zones")] public CanvasZoneDto[] Zones { get; set; }
        }

        [DataContract]
        internal sealed class CanvasZoneDto
        {
            [DataMember(Name = "X")] public double? X { get; set; }
            [DataMember(Name = "Y")] public double? Y { get; set; }
            [DataMember(Name = "width")] public double? Width { get; set; }
            [DataMember(Name = "height")] public double? Height { get; set; }
        }

        [DataContract]
        internal sealed class AppliedLayoutsFileDto
        {
            [DataMember(Name = "applied-layouts")] public AppliedEntryDto[] Entries { get; set; }
        }

        [DataContract]
        internal sealed class AppliedEntryDto
        {
            [DataMember(Name = "device")] public DeviceDto Device { get; set; }
            [DataMember(Name = "applied-layout")] public AppliedLayoutDto Layout { get; set; }
        }

        [DataContract]
        internal sealed class DeviceDto
        {
            [DataMember(Name = "monitor")] public string Monitor { get; set; }
            [DataMember(Name = "monitor-instance")] public string MonitorInstance { get; set; }
            [DataMember(Name = "monitor-number")] public double? MonitorNumber { get; set; }
            [DataMember(Name = "serial-number")] public string SerialNumber { get; set; }
            [DataMember(Name = "virtual-desktop")] public string VirtualDesktop { get; set; }
        }

        [DataContract]
        internal sealed class AppliedLayoutDto
        {
            [DataMember(Name = "uuid")] public string Uuid { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "show-spacing")] public bool? ShowSpacing { get; set; }
            [DataMember(Name = "spacing")] public double? Spacing { get; set; }
            [DataMember(Name = "zone-count")] public double? ZoneCount { get; set; }
            [DataMember(Name = "sensitivity-radius")] public double? SensitivityRadius { get; set; }
        }
    }
}
