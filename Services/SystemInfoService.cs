using System.Runtime.InteropServices;

namespace DynamicIsland;

public readonly record struct SystemStats(
    double Cpu,
    double Ram,
    double RamUsedGb,
    double RamTotalGb,
    bool HasBattery,
    int Battery,
    bool PluggedIn);

public sealed class SystemInfoService
{
    long _idle, _kernel, _user;

    public SystemStats Read()
    {
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long dIdle = idle - _idle, dTotal = kernel - _kernel + (user - _user);
            if (_kernel != 0 && dTotal > 0) cpu = Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
            _idle = idle; _kernel = kernel; _user = user;
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double totalGb = mem.ullTotalPhys / 1073741824.0;
        double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        GetSystemPowerStatus(out var power);
        bool hasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100;

        return new SystemStats(cpu, mem.dwMemoryLoad, usedGb, totalGb, hasBattery, power.BatteryLifePercent, power.ACLineStatus == 1);
    }

    /// <summary>Только питание — дёшево, можно вызывать часто.</summary>
    public (bool HasBattery, int Percent, bool PluggedIn) ReadPower()
    {
        GetSystemPowerStatus(out var power);
        bool hasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100;
        return (hasBattery, power.BatteryLifePercent, power.ACLineStatus == 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
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

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
