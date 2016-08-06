using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DS4Windows.DS4Library
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PackedByte
    {
        private byte m_Data;

        public PackedByte(byte data = 0)
        {
            this.m_Data = data;
        }
        static byte[] masks =
        {
            0,
            2-1,//1
            4-1,//2
            8-1,//3
            16-1,//4
            32-1,//5
            64-1,//6
            128-1,//7
            256-1,//8
        };

        public byte Get(int start, int count)
        {
            byte mask=masks[count];
            return (byte)(((m_Data & (mask << start))) >> start);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4InputSixaxis
    {
        public ushort m_GyroX;
        public ushort m_GyroY;
        public ushort m_GyroZ;

        public ushort m_AccelY;
        public ushort m_AccelX;
        public ushort m_AccelZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Ds4InputButtons
    {
        [MarshalAs(UnmanagedType.Struct)]
        private PackedByte m_Data1;

        [MarshalAs(UnmanagedType.Struct)]
        private PackedByte m_Data2;

        [MarshalAs(UnmanagedType.Struct)]
        private PackedByte m_Data3;

        public bool m_Triangle { get { return m_Data1.Get(7, 1) > 0 ? true : false; } }
        public bool m_Circle { get { return m_Data1.Get(6, 1) > 0 ? true : false; } }
        public bool m_Cross { get { return m_Data1.Get(5, 1) > 0 ? true : false; } }
        public bool m_Square { get { return m_Data1.Get(4, 1) > 0 ? true : false; } }

        public bool m_DPadUp { get { return m_Data1.Get(3, 1) > 0 ? true : false; } }
        public bool m_DPadDown { get { return m_Data1.Get(2, 1) > 0 ? true : false; } }
        public bool m_DPadLeft { get { return m_Data1.Get(1, 1) > 0 ? true : false; } }
        public bool m_DPadRight { get { return m_Data1.Get(0, 1) > 0 ? true : false; } }

        public bool m_R3 { get { return m_Data2.Get(7, 1) > 0 ? true : false; } }
        public bool m_L3 { get { return m_Data2.Get(6, 1) > 0 ? true : false; } }
        public bool m_Options { get { return m_Data2.Get(5, 1) > 0 ? true : false; } }
        public bool m_Share { get { return m_Data2.Get(4, 1) > 0 ? true : false; } }
        public bool m_R1 { get { return m_Data2.Get(1, 1) > 0 ? true : false; } }
        public bool m_L1 { get { return m_Data2.Get(0, 1) > 0 ? true : false; } }

        public bool m_PSButton { get { return m_Data3.Get(0, 1) > 0 ? true : false; } }
        public bool m_TouchButton { get { return m_Data3.Get(1, 1) > 0 ? true : false; } }
        public byte m_FrameCounter { get { return m_Data3.Get(2, 6); } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4PowerStatus
    {
        public byte m_Data;

        public bool m_Charging { get { return (m_Data & 0x10) > 0; } }
        public byte m_Battery { get { return (byte) ((m_Data & 0x0f) * 10); } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS43ByteCoords
    {
        private byte m_Byte1;
        private byte m_Byte2;
        private byte m_Byte3;

        public ushort m_X { get { return (ushort)(m_Byte1 + (m_Byte2 & 0xF) * 255); } }
        public ushort m_Y { get { return (ushort)((m_Byte2 & 0xF0) + m_Byte3 * 16); } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4InputTouchEntry
    {
        [MarshalAs(UnmanagedType.Struct)]
        private PackedByte  m_Data;

        [MarshalAs(UnmanagedType.Struct)]
        public DS43ByteCoords  m_TouchArgs;

        public bool m_Touch { get { return m_Data.Get(7, 1) > 0 ? false : true; } }
        public byte m_TouchIdentifier { get { return m_Data.Get(0, 7); } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4InputTouchEvent
    {
        static readonly ushort MAX_RES_X = 1920;

        public byte m_TouchPacketCounter;

        [MarshalAs(UnmanagedType.Struct)]
        public DS4InputTouchEntry m_Touch1;

        [MarshalAs(UnmanagedType.Struct)]
        public DS4InputTouchEntry m_Touch2;

        public bool m_TouchLeft { get { return m_Touch1.m_TouchArgs.m_X >= (MAX_RES_X*2) / 5; } }
        public bool m_TouchRight { get { return !m_TouchLeft; } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4InputTouchPad
    {
        public byte m_Touches;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.Struct)]
        public DS4InputTouchEvent[] m_TouchEvents;
    }

    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct DS4InputStruct
    {
        public byte m_ReportID;

        public byte m_InputLX;
        public byte m_InputLY;
        public byte m_InputRX;
        public byte m_InputRY;

        [MarshalAs(UnmanagedType.Struct)]
        public Ds4InputButtons m_InputButtons;

        public byte m_InputL2;
        public byte m_InputR2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        byte[] m_emptyPreSixAxis;

        [MarshalAs(UnmanagedType.Struct)]
        public DS4InputSixaxis m_SixAxisInput;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        byte[] m_emptyPreStatusReport;

        //charging, battery
        [MarshalAs(UnmanagedType.Struct)]
        public DS4PowerStatus m_PowerStatus;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        byte[] m_emptyPreTouch;

        [MarshalAs(UnmanagedType.Struct)]
        public DS4InputTouchPad m_TouchInput;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DS4BTHeaderStruct
    {
        public byte m_InputStatus;
        private byte m_DummyByte;
    }
}