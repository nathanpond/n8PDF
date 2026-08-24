using System.Security.Cryptography;

namespace n8PDF;

// TEMPORARY (#230): proves the advisory Semgrep scan flags a known-bad pattern — ECB mode, which
// csharp.dotnet.security.use_ecb_mode catches — while the job stays green and the alert appears in
// code scanning. Removed in the following commit; never merges to main.
internal static class SemgrepProbe
{
    internal static void Configure(Aes aes) => aes.Mode = CipherMode.ECB;
}
