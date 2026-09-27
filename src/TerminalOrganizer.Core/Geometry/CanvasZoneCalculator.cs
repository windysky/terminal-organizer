using System.Collections.Generic;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Geometry
{
    /// <summary>
    /// Single-precision port of the canvas branch of LayoutConfigurator::Custom (LayoutConfigurator.cpp:400-440)
    /// with DPIAware::InverseConvert / Convert (dpi_aware.cpp:49-63, 96-110). No spacing; zone id = array position.
    /// </summary>
    public static class CanvasZoneCalculator
    {
        // @MX:ANCHOR: [AUTO] Canvas geometry entry point used by the resolver for every canvas custom layout.
        // @MX:REASON: The output must match FancyZones pixel for pixel; callers rely on truncation, not rounding.
        // @MX:WARN: [AUTO] Every (float) cast below is load-bearing: it forces IEEE single rounding after each
        // operation, matching MSVC /fp:precise SSE float math. Removing one can move a truncated edge by 1 px.
        // @MX:REASON: C# may otherwise evaluate float expressions at higher precision; AC-012 pins the result.
        /// <summary>
        /// Computes relative zone rects for a canvas on a work area of width x height at the given DPI.
        /// Checks: InvalidCanvasReference, EmptyLayout, then per zone ArithmeticOverflow, EdgeBelowMinimum, NegativeSize.
        /// </summary>
        public static ZoneComputation Calculate(int width, int height, int dpi, int refWidth, int refHeight, CanvasZone[] zones)
        {
            if (zones == null) throw new System.ArgumentNullException("zones");

            // Step 7: layout-level checks.
            if (refWidth <= 0 || refHeight <= 0)
            {
                return ZoneComputation.Failure(LayoutReason.InvalidCanvasReference,
                    string.Format("canvas reference {0}x{1} is not positive", refWidth, refHeight));
            }
            if (zones.Length == 0)
            {
                return ZoneComputation.Failure(LayoutReason.EmptyLayout, "canvas has no zones");
            }

            // InverseConvert: logical size = physical * 96 / dpi.
            float logicalWidth = (float)((float)width * (float)FancyZonesConstants.DefaultDpi);
            logicalWidth = (float)(logicalWidth / (float)dpi);
            float logicalHeight = (float)((float)height * (float)FancyZonesConstants.DefaultDpi);
            logicalHeight = (float)(logicalHeight / (float)dpi);

            List<Zone> result = new List<Zone>();
            for (int i = 0; i < zones.Length; i++)
            {
                CanvasZone z = zones[i];
                float x = (float)((float)((float)z.X * logicalWidth) / (float)refWidth);
                float y = (float)((float)((float)z.Y * logicalHeight) / (float)refHeight);
                float zoneWidth = (float)((float)((float)z.Width * logicalWidth) / (float)refWidth);
                float zoneHeight = (float)((float)((float)z.Height * logicalHeight) / (float)refHeight);

                // Convert: physical = logical * dpi / 96.
                x = (float)((float)(x * (float)dpi) / (float)FancyZonesConstants.DefaultDpi);
                y = (float)((float)(y * (float)dpi) / (float)FancyZonesConstants.DefaultDpi);
                zoneWidth = (float)((float)(zoneWidth * (float)dpi) / (float)FancyZonesConstants.DefaultDpi);
                zoneHeight = (float)((float)(zoneHeight * (float)dpi) / (float)FancyZonesConstants.DefaultDpi);

                float rightF = (float)(x + zoneWidth);
                float bottomF = (float)(y + zoneHeight);

                string where = string.Format("canvas zone {0}", i);
                if (!TruncationFitsInt32(x) || !TruncationFitsInt32(y) || !TruncationFitsInt32(rightF) || !TruncationFitsInt32(bottomF))
                {
                    return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow, where + ": edge outside the 32-bit range");
                }

                // (long) truncation toward zero, as static_cast<long>(float) does.
                long left = (long)x;
                long top = (long)y;
                long right = (long)rightF;
                long bottom = (long)bottomF;
                long rectWidth = right - left;
                long rectHeight = bottom - top;
                if (rectWidth > int.MaxValue || rectHeight > int.MaxValue)
                {
                    return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow, where + ": size outside the 32-bit range");
                }
                if (left < FancyZonesConstants.MaxNegativeSpacing || top < FancyZonesConstants.MaxNegativeSpacing
                    || right < FancyZonesConstants.MaxNegativeSpacing || bottom < FancyZonesConstants.MaxNegativeSpacing)
                {
                    return ZoneComputation.Failure(LayoutReason.EdgeBelowMinimum,
                        string.Format("{0}: edge below {1} (l={2}, t={3}, r={4}, b={5})", where,
                            FancyZonesConstants.MaxNegativeSpacing, left, top, right, bottom));
                }
                if (rectWidth < 0 || rectHeight < 0)
                {
                    return ZoneComputation.Failure(LayoutReason.NegativeSize,
                        string.Format("{0}: negative size {1}x{2}", where, rectWidth, rectHeight));
                }
                result.Add(new Zone(i, (int)left, (int)top, (int)rectWidth, (int)rectHeight));
            }
            return ZoneComputation.Success(result.ToArray(), null);
        }

        /// <summary>True when truncating the value toward zero yields an Int32 (NaN and infinities fail).</summary>
        private static bool TruncationFitsInt32(float value)
        {
            double d = value;
            return d > -2147483649.0 && d < 2147483648.0;
        }
    }
}
