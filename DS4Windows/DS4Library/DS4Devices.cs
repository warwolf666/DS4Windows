using DS4Windows.DS4Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DS4Windows
{
    public class DS4Devices
    {
        private static Dictionary<string, DS4AbstractDevice> Devices = new Dictionary<string, DS4AbstractDevice>();
        private static HashSet<String> DevicePaths = new HashSet<String>();
        public static bool isExclusiveMode = false;

        //enumerates ds4 controllers in the system
        public static void findControllers()
        {
            lock (Devices)
            {
                int[] pid = { 0x5C4 };
                IEnumerable<HidDevice> hDevices = HidDevices.Enumerate(0x054C, pid);
                // Sort Bluetooth first in case USB is also connected on the same controller.
                hDevices = hDevices.OrderBy<HidDevice, ConnectionType>((HidDevice d) => { return HidConnectionType(d); });

                foreach (HidDevice hDevice in hDevices)
                {
                    if (DevicePaths.Contains(hDevice.DevicePath))
                        continue; // BT/USB endpoint already open once
                    if (!hDevice.IsOpen)
                    {
                        hDevice.OpenDevice(isExclusiveMode);
                        // TODO in exclusive mode, try to hold both open when both are connected
                        if (isExclusiveMode && !hDevice.IsOpen)
                            hDevice.OpenDevice(false);
                    }
                    if (hDevice.IsOpen)
                    {
                        if (Devices.ContainsKey(hDevice.readSerial()))
                            continue; // happens when the BT endpoint already is open and the USB is plugged into the same host
                        else
                        {
                            DS4AbstractDevice ds4Device = CreateAbstractDevice(hDevice);
                            ds4Device.Removal += On_Removal;
                            Devices.Add(ds4Device.MacAddress, ds4Device);
                            DevicePaths.Add(hDevice.DevicePath);
                            ds4Device.StartUpdate();
                        }
                    }
                }
                
            }
        }

        private static DS4AbstractDevice CreateAbstractDevice(HidDevice hidDevice)
        {
            switch(HidConnectionType(hidDevice))
            {
                case ConnectionType.BT:
                {
                    return new DS4BluetoothDevice(hidDevice);
                }
                case ConnectionType.USB:
                {
                    return new DS4USBDevice(hidDevice);
                }
            }

            return null;
        }

        public static ConnectionType HidConnectionType(HidDevice hidDevice)
        {
            return hidDevice.Capabilities.InputReportByteLength == 64 ? ConnectionType.USB : ConnectionType.BT;
        }

        //allows to get DS4AbstractDevice by specifying unique MAC address
        //format for MAC address is XX:XX:XX:XX:XX:XX
        public static DS4AbstractDevice getDS4Controller(string mac)
        {
            lock (Devices)
            {
                DS4AbstractDevice device = null;
                try
                {
                    Devices.TryGetValue(mac, out device);
                }
                catch (ArgumentNullException) { }
                return device;
            }
        }
        
        //returns DS4 controllers that were found and are running
        public static IEnumerable<DS4AbstractDevice> getDS4Controllers()
        {
            lock (Devices)
            {
                DS4AbstractDevice[] controllers = new DS4AbstractDevice[Devices.Count];
                Devices.Values.CopyTo(controllers, 0);
                return controllers;
            }
        }

        public static void stopControllers()
        {
            lock (Devices)
            {
                IEnumerable<DS4AbstractDevice> devices = getDS4Controllers();
                foreach (DS4AbstractDevice device in devices)
                {
                    device.StopUpdate();
                    device.HidDevice.CloseDevice();
                }
                Devices.Clear();
                DevicePaths.Clear();
            }
        }

        //called when devices is diconnected, timed out or has input reading failure
        public static void On_Removal(object sender, EventArgs e)
        {
            lock (Devices)
            {
                DS4AbstractDevice device = (DS4AbstractDevice)sender;
                device.HidDevice.CloseDevice();
                Devices.Remove(device.MacAddress);
                DevicePaths.Remove(device.HidDevice.DevicePath);
            }
        }
    }
}
