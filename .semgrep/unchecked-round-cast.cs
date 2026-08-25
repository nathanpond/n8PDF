using System;

class Fixture
{
    // ruleid: unchecked-round-cast
    int Bad(double v) => (int)Math.Round(v);

    // A (long) cast of a rounded real is out of scope — long holds the range.
    // ok: unchecked-round-cast
    long OkLong(double v) => (long)Math.Round(v * 914400.0);

    // ruleid: unchecked-round-cast
    int BadCeil(double v) => (int)Math.Ceiling(v);

    // Contained by Math.Max — a wrap yields a valid value, not corrupted geometry.
    // ok: unchecked-round-cast
    int OkContained(double v) => Math.Max(1, (int)Math.Round(v));

    // ok: unchecked-round-cast
    int OkClamped(double v) => Math.Clamp((int)Math.Floor(v), 1, 100);

    // Contained by Math.Min — the result is bounded, not corrupted.
    // ok: unchecked-round-cast
    int OkMin(double v) => Math.Min(1000, (int)Math.Floor(v));

    // The committed fix (#148/#204): round first, range-guard, then cast the guarded local.
    // ok: unchecked-round-cast
    int Good(double value)
    {
        var rounded = Math.Round(value);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : 0;
    }
}
