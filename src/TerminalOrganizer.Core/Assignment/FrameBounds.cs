namespace TerminalOrganizer.Core.Assignment
{
    /// <summary>
    /// The invisible resize border of one top-level window: per edge, the visible frame
    /// (DWMWA_EXTENDED_FRAME_BOUNDS) minus the window rect (GetWindowRect). Windows
    /// Terminal at 96 dpi measures Left 7, Top 0, Right -7, Bottom -7. Immutable.
    /// </summary>
    public sealed class FrameMargins
    {
        public static readonly FrameMargins None = new FrameMargins(0, 0, 0, 0);

        private readonly int left;
        private readonly int top;
        private readonly int right;
        private readonly int bottom;

        public FrameMargins(int left, int top, int right, int bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }

        public int Left { get { return left; } }
        public int Top { get { return top; } }
        public int Right { get { return right; } }
        public int Bottom { get { return bottom; } }
    }

    /// <summary>
    /// Pure arithmetic for the invisible-border compensation (FancyZones parity): the
    /// zone is sized to the VISIBLE frame, so the rect handed to SetWindowPos is the zone
    /// grown by the border. Uses the FancyZones edge set only — left, right and bottom are
    /// adjusted, the top edge is not (PowerToys AdjustRectForSizeWindowToRect). Rects are
    /// {left, top, width, height}. A null margins argument is the identity.
    /// </summary>
    public static class FrameBounds
    {
        /// <summary>Margins = frame minus window, per edge.</summary>
        public static FrameMargins Measure(int windowLeft, int windowTop, int windowRight, int windowBottom,
            int frameLeft, int frameTop, int frameRight, int frameBottom)
        {
            return new FrameMargins(frameLeft - windowLeft, frameTop - windowTop,
                frameRight - windowRight, frameBottom - windowBottom);
        }

        /// <summary>The visible-frame rect of a window whose outer rect is given.</summary>
        public static int[] VisibleRect(int windowLeft, int windowTop, int windowWidth, int windowHeight,
            FrameMargins margins)
        {
            FrameMargins m = margins ?? FrameMargins.None;
            int left = windowLeft + m.Left;
            int right = windowLeft + windowWidth + m.Right;
            int bottom = windowTop + windowHeight + m.Bottom;
            return new int[] { left, windowTop, right - left, bottom - windowTop };
        }

        /// <summary>The outer rect to pass to SetWindowPos so the visible frame lands on the zone.</summary>
        public static int[] ExpandTarget(int zoneLeft, int zoneTop, int zoneWidth, int zoneHeight,
            FrameMargins margins)
        {
            FrameMargins m = margins ?? FrameMargins.None;
            int left = zoneLeft - m.Left;
            int right = zoneLeft + zoneWidth - m.Right;
            int bottom = zoneTop + zoneHeight - m.Bottom;
            return new int[] { left, zoneTop, right - left, bottom - zoneTop };
        }
    }
}
