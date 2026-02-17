using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace HotCPU
{
    public static class BenchmarkManager
    {
        private const string BenchmarkDirName = "benchmark";
        private const string BenchmarkExeName = "hotcpu-benchmark.exe";

        public static void RunBenchmark()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                // Look for benchmark in local folder (dev or release structure)
                // In dev: ../../../benchmark/target/release or debug
                // In prod/publish: ./benchmark/
                
                string? benchmarkPath = FindBenchmarkExecutable(appDir);

                if (string.IsNullOrEmpty(benchmarkPath))
                {
                    MessageBox.Show("Benchmark executable not found.\nPlease build the benchmark project first.", 
                        "Benchmark Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = benchmarkPath,
                    WorkingDirectory = Path.GetDirectoryName(benchmarkPath),
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start benchmark: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string? FindBenchmarkExecutable(string baseDir)
        {
            // 1. Check direct subdirectory (Published/Release structure)
            string localPath = Path.Combine(baseDir, BenchmarkDirName, BenchmarkExeName);
            if (File.Exists(localPath)) return localPath;

            // 2. Dev environment (hotcpu/bin/Debug/net8.0-windows -> hotcpu/benchmark/target/debug/...)
            // Go up 4 levels from bin/Debug/net8.0-windows to get to solution root
            DirectoryInfo dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 4; i++)
            {
                if (dir.Parent == null) break;
                dir = dir.Parent;
            }

            // Check debug target
            string devDebugPath = Path.Combine(dir.FullName, BenchmarkDirName, "target", "debug", BenchmarkExeName);
            if (File.Exists(devDebugPath)) return devDebugPath;

            // Check release target
            string devReleasePath = Path.Combine(dir.FullName, BenchmarkDirName, "target", "release", BenchmarkExeName);
            if (File.Exists(devReleasePath)) return devReleasePath;

            return null;
        }
    }
}
