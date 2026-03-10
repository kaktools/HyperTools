using System.Runtime.InteropServices;

namespace HyperTool.Services;

public sealed class SystemResourceSampler
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPrevious;

    public (double CpuPercent, double RamUsedGb, double RamTotalGb) Sample()
    {
        var cpuPercent = SampleCpuPercent();
        var (ramUsedGb, ramTotalGb) = SampleMemoryGb();
        return (cpuPercent, ramUsedGb, ramTotalGb);
    }

    private double SampleCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return 0;
        }

        var idle = FileTimeToUInt64(idleTime);
        var kernel = FileTimeToUInt64(kernelTime);
        var user = FileTimeToUInt64(userTime);

        if (!_hasPrevious)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPrevious = true;
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0)
        {
            return 0;
        }

        var busy = totalDelta - idleDelta;
        var percent = (double)busy / totalDelta * 100d;
        return Math.Clamp(percent, 0d, 100d);
    }

    private static (double RamUsedGb, double RamTotalGb) SampleMemoryGb()
    {
        var memoryStatus = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memoryStatus) || memoryStatus.TotalPhys == 0)
        {
            return (0, 0);
        }

        var usedBytes = memoryStatus.TotalPhys - memoryStatus.AvailPhys;
        var gb = 1024d * 1024d * 1024d;
        return (usedBytes / gb, memoryStatus.TotalPhys / gb);
    }

    private static ulong FileTimeToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | (uint)fileTime.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int LowDateTime;
        public int HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx memoryStatus);
}
