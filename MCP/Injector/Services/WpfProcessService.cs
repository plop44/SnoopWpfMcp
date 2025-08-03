using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SnoopWpfMcpServer.Models;

namespace SnoopWpfMcpServer.Services
{
    public interface IWpfProcessService
    {
        Task<List<WpfProcessInfo>> GetWpfProcessesAsync();
    }

    public class WpfProcessService : IWpfProcessService
    {
        private readonly ILogger<WpfProcessService> _logger;
        private static readonly string[] WpfAssemblies = { 
            "PresentationFramework", 
            "PresentationCore", 
            "WindowsBase",
            "System.Windows.Presentation"
        };

        // WPF-specific window class name pattern used by HwndWrapper
        private static readonly Regex WindowClassNameRegex = new(@"^HwndWrapper\[.*;.*;.*\]$", RegexOptions.Compiled);
        
        // Current process ID to exclude self
        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        public WpfProcessService(ILogger<WpfProcessService> logger)
        {
            _logger = logger;
        }

        public async Task<List<WpfProcessInfo>> GetWpfProcessesAsync()
        {
            _logger.LogInformation("Scanning for WPF processes...");
            var wpfProcesses = new List<WpfProcessInfo>();

            try
            {
                var allProcesses = Process.GetProcesses();
                var tasks = allProcesses.Select(async process =>
                {
                    try
                    {
                        if (IsWpfProcess(process))
                        {
                            var processInfo = await CreateProcessInfoAsync(process);
                            if (processInfo != null)
                            {
                                return processInfo;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Error checking process {process.Id}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                    return null;
                }).ToArray();

                var results = await Task.WhenAll(tasks);
                wpfProcesses.AddRange(results.Where(p => p != null)!);

                _logger.LogInformation($"Found {wpfProcesses.Count} WPF processes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning for WPF processes");
            }

            return wpfProcesses;
        }

        private bool IsWpfProcess(Process process)
        {
            try
            {
                // Check if process has exited
                if (process.HasExited)
                    return false;

                // Skip system processes and processes we can't access
                if (process.Id <= 4 || string.IsNullOrEmpty(process.ProcessName))
                    return false;

                // Exclude our own process (like SnoopWPF does)
                if (process.Id == CurrentProcessId)
                    return false;

                // First check: Look for WPF window class names (HwndWrapper pattern)
                var isWpfByWindowClass = CheckForWpfWindowClasses(process);
                if (isWpfByWindowClass)
                {
                    _logger.LogDebug($"Process {process.Id} ({process.ProcessName}) identified as WPF by window class");
                    return true;
                }

                // Second check: Look for WPF graphics modules (wpfgfx_* dlls)
                var isWpfByModules = CheckForWpfGraphicsModules(process);
                if (isWpfByModules)
                {
                    _logger.LogDebug($"Process {process.Id} ({process.ProcessName}) identified as WPF by graphics modules");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error checking if process {process.Id} is WPF: {ex.Message}");
                return false;
            }
        }

        private bool CheckForWpfWindowClasses(Process process)
        {
            try
            {
                // Check if process has main window with WPF class name pattern
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    var className = GetWindowClassName(process.MainWindowHandle);
                    if (!string.IsNullOrEmpty(className) && WindowClassNameRegex.IsMatch(className))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not check window class for process {process.Id}: {ex.Message}");
            }

            return false;
        }

        private bool CheckForWpfGraphicsModules(Process process)
        {
            try
            {
                var modules = process.Modules;
                foreach (ProcessModule module in modules)
                {
                    // Check for WPF graphics modules (following SnoopWPF's approach)
                    if (module.ModuleName.StartsWith("wpfgfx_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Also check for traditional WPF assemblies as fallback
                    var moduleName = Path.GetFileNameWithoutExtension(module.ModuleName);
                    if (WpfAssemblies.Any(wpfAssembly => 
                        string.Equals(moduleName, wpfAssembly, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not access modules for process {process.Id}: {ex.Message}");
            }

            return false;
        }

        private string GetWindowClassName(IntPtr hwnd)
        {
            try
            {
                const int maxChars = 256;
                var stringBuilder = new System.Text.StringBuilder(maxChars);
                if (GetClassName(hwnd, stringBuilder, maxChars) > 0)
                {
                    return stringBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not get window class name: {ex.Message}");
            }

            return string.Empty;
        }

        // P/Invoke declaration for GetClassName
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private async Task<WpfProcessInfo?> CreateProcessInfoAsync(Process process)
        {
            try
            {
                return await Task.Run(() => new WpfProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    MainWindowTitle = process.MainWindowTitle ?? string.Empty,
                    FileName = GetProcessFileName(process),
                    WorkingDirectory = GetProcessWorkingDirectory(process),
                    IsWpfApplication = true, // We only call this for WPF processes
                    HasMainWindow = process.MainWindowHandle != IntPtr.Zero,
                    StartTime = process.StartTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error creating process info for {process.Id}: {ex.Message}");
                return null;
            }
        }

        private string GetProcessFileName(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetProcessWorkingDirectory(Process process)
        {
            try
            {
                // Try to get working directory using WMI
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                using var objects = searcher.Get();
                
                foreach (ManagementObject obj in objects)
                {
                    var commandLine = obj["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(commandLine))
                    {
                        // Extract directory from command line if possible
                        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var exePath = parts[0].Trim('"');
                            return Path.GetDirectoryName(exePath) ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not get working directory for process {process.Id}: {ex.Message}");
            }

            return string.Empty;
        }
    }
}
