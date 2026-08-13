using System.Runtime.InteropServices;

namespace AllPurposeAssistant.Helpers;

/// <summary>
/// 读取系统内存占用率与 CPU 使用率。
/// 内存占用率由 GlobalMemoryStatusEx 直接返回；
/// CPU 使用率通过两次采样 GetSystemTimes 的差值计算，
/// 因此需周期性调用 GetCpuUsagePercent() 才能得到有效值。
/// </summary>
public static class SystemMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    private static FileTime _prevIdle;
    private static FileTime _prevKernel;
    private static FileTime _prevUser;
    private static bool _hasPrev;

    /// <summary>内存占用率，0-100，系统直接给出。</summary>
    public static double GetMemoryUsagePercent()
    {
        var mem = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref mem))
            return 0;
        return mem.dwMemoryLoad;
    }

    /// <summary>CPU 占用率，0-100。首次调用建立基线返回 0，之后每次调用返回相对上次采样的占用率。</summary>
    public static double GetCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        var result = 0.0;
        if (_hasPrev)
        {
            ulong idleDiff = ToUInt64(idle) - ToUInt64(_prevIdle);
            ulong kernelDiff = ToUInt64(kernel) - ToUInt64(_prevKernel);
            ulong userDiff = ToUInt64(user) - ToUInt64(_prevUser);
            ulong totalDiff = kernelDiff + userDiff;

            if (totalDiff > 0)
                result = Math.Clamp((1.0 - (double)idleDiff / totalDiff) * 100.0, 0.0, 100.0);
        }

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        _hasPrev = true;
        return result;
    }

    private static ulong ToUInt64(FileTime ft) => ((ulong)ft.High << 32) | ft.Low;
}
