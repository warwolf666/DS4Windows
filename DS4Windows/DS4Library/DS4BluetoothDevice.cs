using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DS4Windows.DS4Library
{
    class DS4BluetoothDevice : DS4AbstractDevice
    {
        public DS4BluetoothDevice(HidDevice hidDevice) : base(hidDevice)
        {

        }

        protected override bool ParseInputBuffer(byte[] buffer, ref DS4InputStruct inputStruct)
        {
            unsafe
            {
                fixed (byte* headerPtr = &buffer[0])
                {
                    DS4BTHeaderStruct header = (DS4BTHeaderStruct) Marshal.PtrToStructure((IntPtr) headerPtr, typeof(DS4BTHeaderStruct));
                    if(header.m_InputStatus == 0x11)
                    {
                        int headerSize = Marshal.SizeOf(header);
                        fixed(byte* inputPtr = &buffer[headerSize])
                        {
                            inputStruct = (DS4InputStruct) Marshal.PtrToStructure((IntPtr) inputPtr, typeof(DS4InputStruct));
                            return inputStruct.m_ReportID == 0;
                        }
                    }
                }
            }

            return false;
        }
    }
}
