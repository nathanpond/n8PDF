using System;
namespace n8PDF;

// TEMPORARY (#234): a new high-severity finding on changed code, to show the gate go red. The full
// suite build compiles this (internal, unused-but-not-warned); reverted in the next commit.
internal static class SemgrepGateProbe
{
    internal static int WrapMe(double v) => (int)Math.Round(v);
}
