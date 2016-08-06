using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DS4Windows.DS4Library
{
    class DS4USBDevice : DS4AbstractDevice
    {
        public DS4USBDevice(HidDevice hidDevice) : base(hidDevice)
        {

        }

        protected override bool ParseInputBuffer(byte[] buffer, ref DS4InputStruct inputStruct)
        {
            unsafe
            {
                fixed (byte* inputPtr = &buffer[0])
                {
                    inputStruct = (DS4InputStruct)Marshal.PtrToStructure((IntPtr)inputPtr, typeof(DS4InputStruct));
                    return true;
                }
            }
        }
    }
}
