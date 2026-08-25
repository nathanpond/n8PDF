using System;

class Fixture
{
    // ruleid: unchecked-round-cast
    int Bad(double v) => (int)Math.Round(v);

    // ruleid: unchecked-round-cast
    long BadLong(double v) => (long)Math.Round(v * 914400.0);

    // ruleid: unchecked-round-cast
    int BadCeil(double v) => (int)Math.Ceiling(v);

    // The committed fix (#148/#204): round first, range-guard, then cast the guarded local.
    // ok: unchecked-round-cast
    int Good(double value)
    {
        var rounded = Math.Round(value);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : 0;
    }
}
