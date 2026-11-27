using System;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [timebase] One time axis for the instruments that draw time (the roadmap's "osiloskop ve mantık analizörü tek zaman
    /// ekseninde", as a mixed-signal scope or Saleae's analog and digital lanes share theirs): the instant under the pointer
    /// in one view is marked in the others that show the same session's time, and the scope's window can be taken over by
    /// the logic analyzer (the Code page's timeline). A view sets the cursor while the pointer is over its plot and clears
    /// it when the pointer leaves; it does not draw the mark it set (it has its own pointer).
    /// </summary>
    public static class TimeCursor
    {
        /// <summary>The instant (the session's simulated seconds), or null.</summary>
        public static double? Time { get; private set; }

        /// <summary>The session whose time it is (a circuit or a world session).</summary>
        public static object? Clock { get; private set; }

        /// <summary>The view that set it.</summary>
        public static object? From { get; private set; }

        /// <summary>The scope's last window (its session and the seconds it spans), for "Osiloskopla eşle".</summary>
        public static (object Clock, double Start, double End)? ScopeSpan { get; private set; }

        /// <summary>Raised when the cursor moves, appears or goes.</summary>
        public static event Action? Changed;

        public static void Set(object? clock, object from, double? time)
        {
            if (time == null && !ReferenceEquals(From, from)) return;   // only the view that set it clears it
            if (ReferenceEquals(Clock, clock) && ReferenceEquals(From, from) && Time == time) return;
            Clock = clock;
            From = from;
            Time = time;
            Changed?.Invoke();
        }

        /// <summary>The instant <paramref name="view"/> should mark: set by another view on the same session's time.</summary>
        public static double? For(object? clock, object view) =>
            Time is { } t && clock != null && ReferenceEquals(Clock, clock) && !ReferenceEquals(From, view) ? t : null;

        /// <summary>The scope's window as it was last drawn.</summary>
        public static void ScopeShows(object clock, double start, double end) => ScopeSpan = (clock, start, end);
    }
}
