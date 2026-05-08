using System.Threading.Tasks;
using Microsoft.Win32;
using Xunit;

namespace HotCPU.Tests
{
    /// <summary>
    /// End-to-end tests for the unpackaged registry path. We cannot reliably exercise the
    /// packaged StartupTask path from a test host (no AppX identity), so those branches are
    /// covered by manual verification against an installed Store build.
    /// </summary>
    public class StartupManagerTests
    {
        private const string TestValueName = "HotCPU";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        [Fact]
        public async Task IsStartupEnabled_FalseWhenNotRegistered()
        {
            if (StartupManager.IsPackage) return; // covered by manual Store testing

            RemoveRunValue();
            var enabled = await StartupManager.IsStartupEnabledAsync();
            Assert.False(enabled);
        }

        [Fact]
        public async Task TrySetStartupEnabled_TrueThenFalse_WritesAndRemovesRunKey()
        {
            if (StartupManager.IsPackage) return;

            bool priorState;
            try { priorState = await StartupManager.IsStartupEnabledAsync(); }
            catch { priorState = false; }

            try
            {
                RemoveRunValue();

                var resultOn = await StartupManager.TrySetStartupEnabledAsync(true);
                Assert.Equal(StartupChangeResult.Success, resultOn);
                Assert.True(RunValueExists());
                // Value must be quoted so paths with spaces still parse.
                var value = GetRunValue();
                Assert.NotNull(value);
                Assert.StartsWith("\"", value);
                Assert.EndsWith("\"", value);

                var resultOff = await StartupManager.TrySetStartupEnabledAsync(false);
                Assert.Equal(StartupChangeResult.Success, resultOff);
                Assert.False(RunValueExists());
            }
            finally
            {
                // Restore previous state to not leave a side-effect on the developer machine.
                RemoveRunValue();
                if (priorState)
                {
                    await StartupManager.TrySetStartupEnabledAsync(true);
                }
            }
        }

        [Fact]
        public async Task TrySetStartupEnabled_Idempotent_WhenRepeatedlyCalled()
        {
            if (StartupManager.IsPackage) return;

            bool prior = await StartupManager.IsStartupEnabledAsync();
            try
            {
                Assert.Equal(StartupChangeResult.Success, await StartupManager.TrySetStartupEnabledAsync(true));
                Assert.Equal(StartupChangeResult.Success, await StartupManager.TrySetStartupEnabledAsync(true));
                Assert.True(await StartupManager.IsStartupEnabledAsync());

                Assert.Equal(StartupChangeResult.Success, await StartupManager.TrySetStartupEnabledAsync(false));
                Assert.Equal(StartupChangeResult.Success, await StartupManager.TrySetStartupEnabledAsync(false));
                Assert.False(await StartupManager.IsStartupEnabledAsync());
            }
            finally
            {
                RemoveRunValue();
                if (prior) await StartupManager.TrySetStartupEnabledAsync(true);
            }
        }

        [Fact]
        public void IsPackage_DoesNotThrow()
        {
            // The cached value simply must be stable and not raise.
            var a = StartupManager.IsPackage;
            var b = StartupManager.IsPackage;
            Assert.Equal(a, b);
        }

        private static void RemoveRunValue()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(TestValueName, throwOnMissingValue: false);
            }
            catch { }
        }

        private static bool RunValueExists()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(TestValueName) != null;
        }

        private static string? GetRunValue()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(TestValueName) as string;
        }
    }
}
