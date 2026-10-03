using System.Runtime.InteropServices;
using HandbrakeMapper.Models;

namespace HandbrakeMapper.Services;

public sealed class JoystickService
{
    private const uint JOYERR_NOERROR = 0;
    private const uint JOY_RETURNALL = 0x000000FF;
    private const uint JOYCAPS_HASZ = 0x0001;
    private const uint JOYCAPS_HASR = 0x0002;
    private const uint JOYCAPS_HASU = 0x0004;
    private const uint JOYCAPS_HASV = 0x0008;

    public sealed record DeviceInfo(int Id, string Name, IReadOnlyList<AxisKind> Axes, int ButtonCount);

    public sealed record State(uint X, uint Y, uint Z, uint R, uint U, uint V, uint Buttons)
    {
        public uint GetAxis(AxisKind axis) => axis switch
        {
            AxisKind.X => X,
            AxisKind.Y => Y,
            AxisKind.Z => Z,
            AxisKind.R => R,
            AxisKind.U => U,
            AxisKind.V => V,
            _ => X
        };

        public bool GetButton(int oneBasedButton)
        {
            if (oneBasedButton < 1 || oneBasedButton > 32)
                return false;
            return (Buttons & (1u << (oneBasedButton - 1))) != 0;
        }
    }

    public IReadOnlyList<DeviceInfo> Enumerate()
    {
        var result = new List<DeviceInfo>();
        var count = joyGetNumDevs();

        for (uint id = 0; id < count; id++)
        {
            var caps = new JOYCAPS();
            if (joyGetDevCapsW(id, ref caps, (uint)Marshal.SizeOf<JOYCAPS>()) != JOYERR_NOERROR)
                continue;

            if (!TryRead((int)id, out _))
                continue;

            var axes = new List<AxisKind> { AxisKind.X, AxisKind.Y };
            if ((caps.wCaps & JOYCAPS_HASZ) != 0) axes.Add(AxisKind.Z);
            if ((caps.wCaps & JOYCAPS_HASR) != 0) axes.Add(AxisKind.R);
            if ((caps.wCaps & JOYCAPS_HASU) != 0) axes.Add(AxisKind.U);
            if ((caps.wCaps & JOYCAPS_HASV) != 0) axes.Add(AxisKind.V);

            result.Add(new DeviceInfo((int)id, caps.szPname.TrimEnd('\0'), axes, (int)caps.wNumButtons));
        }

        return result;
    }

    public bool TryRead(int id, out State state)
    {
        var info = new JOYINFOEX
        {
            dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
            dwFlags = JOY_RETURNALL
        };

        var code = joyGetPosEx((uint)id, ref info);
        if (code != JOYERR_NOERROR)
        {
            state = new State(0, 0, 0, 0, 0, 0, 0);
            return false;
        }

        state = new State(info.dwXpos, info.dwYpos, info.dwZpos, info.dwRpos, info.dwUpos, info.dwVpos, info.dwButtons);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOYCAPS
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint joyGetDevCapsW(uint uJoyID, ref JOYCAPS pjc, uint cbjc);

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
}
