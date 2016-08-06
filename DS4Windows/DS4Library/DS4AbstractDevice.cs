using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Threading.Tasks;


using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
using System.Drawing;
using DS4Windows.DS4Library;

namespace DS4Windows
{
    public struct DS4Color
    {
        public byte red;
        public byte green;
        public byte blue;
        public DS4Color(System.Drawing.Color c)
        {
            red = c.R;
            green = c.G;
            blue = c.B;
        }
        public DS4Color(byte r, byte g, byte b)
        {
            red = r;
            green = g;
            blue = b;
        }
        public override bool Equals(object obj)
        {
            if (obj is DS4Color)
            {
                DS4Color dsc = ((DS4Color)obj);
                return (this.red == dsc.red && this.green == dsc.green && this.blue == dsc.blue);
            }
            else
                return false;
        }
        public Color ToColor => Color.FromArgb(red, green, blue);
        public Color ToColorA
        {
            get
            {
                byte alphacolor = Math.Max(red, Math.Max(green, blue));
                Color reg = Color.FromArgb(red, green, blue);
                Color full = HuetoRGB(reg.GetHue(), reg.GetBrightness(), reg);
                return Color.FromArgb((alphacolor > 205 ? 255 : (alphacolor + 50)), full);
            }
        }

        private Color HuetoRGB(float hue, float light, Color rgb)
        {
            float L = (float)Math.Max(.5, light);
            float C = (1 - Math.Abs(2 * L - 1));
            float X = (C * (1 - Math.Abs((hue / 60) % 2 - 1)));
            float m = L - C / 2;
            float R = 0, G = 0, B = 0;
            if (light == 1) return Color.White;
            else if (rgb.R == rgb.G && rgb.G == rgb.B) return Color.White;
            else if (0 <= hue && hue < 60) { R = C; G = X; }
            else if (60 <= hue && hue < 120) { R = X; G = C; }
            else if (120 <= hue && hue < 180) { G = C; B = X; }
            else if (180 <= hue && hue < 240) { G = X; B = C; }
            else if (240 <= hue && hue < 300) { R = X; B = C; }
            else if (300 <= hue && hue < 360) { R = C; B = X; }
            return Color.FromArgb((int)((R + m) * 255), (int)((G + m) * 255), (int)((B + m) * 255));
        }

        public static bool TryParse(string value, ref DS4Color ds4color)
        {
            try
            {
                string[] ss = value.Split(',');
                return byte.TryParse(ss[0], out ds4color.red) &&byte.TryParse(ss[1], out ds4color.green) && byte.TryParse(ss[2], out ds4color.blue);
            }
            catch { return false; }
        }
        public override string ToString() => $"Red: {red} Green: {green} Blue: {blue}";
    }

    public enum ConnectionType : byte { BT, USB }; // Prioritize Bluetooth when both are connected.

    /**
     * The haptics engine uses a stack of these states representing the light bar and rumble motor settings.
     * It (will) handle composing them and the details of output report management.
     */
    public struct DS4HapticState
    {
        public DS4Color LightBarColor;
        public bool LightBarExplicitlyOff;
        public byte LightBarFlashDurationOn, LightBarFlashDurationOff;
        public byte RumbleMotorStrengthLeftHeavySlow, RumbleMotorStrengthRightLightFast;
        public bool RumbleMotorsExplicitlyOff;
        public bool IsLightBarSet()
        {
            return LightBarExplicitlyOff || LightBarColor.red != 0 || LightBarColor.green != 0 || LightBarColor.blue != 0;
        }
        public bool IsRumbleSet()
        {
            return RumbleMotorsExplicitlyOff || RumbleMotorStrengthLeftHeavySlow != 0 || RumbleMotorStrengthRightLightFast != 0;
        }
    }
    
    public abstract class DS4AbstractDevice
    {
        private HidDevice hDevice;
        private string Mac;
        private DS4State cState = new DS4State();
        private DS4State pState = new DS4State();
        private ConnectionType conType;

        private readonly DS4Touchpad touchpad = null;
        private readonly DS4SixAxis sixAxis = null;
        private byte rightLightFastRumble;
        private byte leftHeavySlowRumble;
        private DS4Color ligtBarColor;
        private byte ledFlashOn, ledFlashOff;
        private Thread ds4Input, ds4Output;
        private int battery;
        public DateTime lastActive = DateTime.UtcNow;
        public DateTime firstActive = DateTime.UtcNow;
        private bool charging;
        public event EventHandler<EventArgs> Report = null;
        public event EventHandler<EventArgs> Removal = null;

        private byte[] m_InputBuffer;

        private byte[] m_OutputBuffer;
        private byte[] m_OutputReportBuffer;
        private DS4InputStruct  m_InputStruct;

        public HidDevice HidDevice => hDevice;
        public bool IsExclusive => HidDevice.IsExclusive;
        public bool IsDisconnecting { get; private set; }

        public string MacAddress =>  Mac;

        public ConnectionType ConnectionType => conType;
        public int IdleTimeout { get; set; } // behavior only active when > 0

        public int Battery => battery;
        public bool Charging => charging;

        public byte RightLightFastRumble
        {
            get { return rightLightFastRumble; }
            set
            {
                if (value == rightLightFastRumble) return;
                rightLightFastRumble = value;
            }
        }

        public byte LeftHeavySlowRumble
        {
            get { return leftHeavySlowRumble; }
            set
            {
                if (value == leftHeavySlowRumble) return;
                leftHeavySlowRumble = value;
            }
        }

        public DS4Color LightBarColor
        {
            get { return ligtBarColor; }
            set
            {
                if (ligtBarColor.red != value.red || ligtBarColor.green != value.green || ligtBarColor.blue != value.blue)
                {
                    ligtBarColor = value;
                }
            }
        }

        public byte LightBarOnDuration
        {
            get { return ledFlashOn; }
            set
            {
                if (ledFlashOn != value)
                {
                    ledFlashOn = value;
                }
            }
        }
        
        public byte LightBarOffDuration
        {
            get { return ledFlashOff; }
            set
            {
                if (ledFlashOff != value)
                {
                    ledFlashOff = value;
                }
            }
        }

        public DS4Touchpad Touchpad { get { return touchpad; } }
        public DS4SixAxis SixAxis { get { return sixAxis; } }

        public DS4AbstractDevice(HidDevice hidDevice)
        {            
            hDevice = hidDevice;
            conType = DS4Devices.HidConnectionType(hDevice);
            Mac = hDevice.readSerial();

            m_InputBuffer = new byte[hidDevice.Capabilities.InputReportByteLength];
            m_OutputBuffer = new byte[hidDevice.Capabilities.OutputReportByteLength];
            m_OutputReportBuffer = new byte[hidDevice.Capabilities.OutputReportByteLength];

            touchpad = new DS4Touchpad();
            sixAxis = new DS4SixAxis();
        }

        public void StartUpdate()
        {
            if (ds4Input == null)
            {
                Console.WriteLine(MacAddress.ToString() + " " + System.DateTime.UtcNow.ToString("o") + "> start");
                sendOutputReport(true); // initialize the output report
                ds4Output = new Thread(performDs4Output);
                ds4Output.Name = "DS4 Output thread: " + Mac;
                ds4Output.Start();
                ds4Input = new Thread(performDs4Input);
                ds4Input.Name = "DS4 Input thread: " + Mac;
                ds4Input.Start();
            }
            else
                Console.WriteLine("Thread already running for DS4: " + Mac);
        }

        public void StopUpdate()
        {
            if (ds4Input != null && (ds4Input.ThreadState != System.Threading.ThreadState.Stopped || ds4Input.ThreadState != System.Threading.ThreadState.Aborted))
            {
                try
                {
                    ds4Input.Abort();
                    ds4Input.Join();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            StopOutputUpdate();
        }

        private void StopOutputUpdate()
        {
            if (ds4Output != null && (ds4Output.ThreadState != System.Threading.ThreadState.Stopped || ds4Output.ThreadState != System.Threading.ThreadState.Aborted))
            {
                try
                {
                    ds4Output.Abort();
                    ds4Output.Join();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private bool writeOutput()
        {
            byte[] outputReport = m_OutputBuffer;

            if (conType == ConnectionType.BT)
            {
                return hDevice.WriteOutputReportViaControl(outputReport);
            }
            else
            {
                return hDevice.WriteOutputReportViaInterrupt(outputReport, 8);
            }
        }

        private void performDs4Output()
        {
            byte[] outputReport = m_OutputBuffer;
            lock (outputReport)
            {
                int lastError = 0;
                while (true)
                {
                    if (writeOutput())
                    {
                        lastError = 0;
                        if (testRumble.IsRumbleSet()) // repeat test rumbles periodically; rumble has auto-shut-off in the DS4 firmware
                            Monitor.Wait(outputReport, 10000); // DS4 firmware stops it after 5 seconds, so let the motors rest for that long, too.
                        else
                            Monitor.Wait(outputReport);
                    }
                    else
                    {
                        int thisError = Marshal.GetLastWin32Error();
                        if (lastError != thisError)
                        {
                            Console.WriteLine(MacAddress.ToString() + " " + System.DateTime.UtcNow.ToString("o") + "> encountered write failure: " + thisError);
                            lastError = thisError;
                        }
                    }
                }
            }
        }

        /** Is the device alive and receiving valid sensor input reports? */
        public bool IsAlive()
        {
            return priorInputReport30 != 0xff;
        }
        private byte priorInputReport30 = 0xff;
        public double Latency = 0;
        bool warn;
        public string error;

        enum BufferInputs
        {
            InputSkip1, //0
            InputLX, //1
            InpuitLY, //2
            InputRX, //3
            InputRY, //4
            InputButtons1, //5 
            InputButtons2, //6
            InputButtons3, //7
            InputL2, //8
            InputR2, //9
            InputSkip5, //10-13
            InputSixaxis,   //14-20 gyro, 20-26 accel
            InputSkip4, //27-29
            InputReportStatus, //30

        }

        protected abstract bool ParseInputBuffer(byte[] buffer, ref DS4InputStruct inputStruct);

        private bool ReadInputBuffer()
        {
            HidDevice.ReadStatus res = hDevice.ReadFile(m_InputBuffer);
            return res == HidDevice.ReadStatus.Success;
        }

        private void performDs4Input()
        {
            firstActive = DateTime.UtcNow;
            System.Timers.Timer readTimeout = new System.Timers.Timer(); // Await 30 seconds for the initial packet, then 3 seconds thereafter.
            readTimeout.Elapsed += delegate { HidDevice.CancelIO(); };
            List<long> Latency = new List<long>();
            long oldtime = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (true)
            {
                string currerror = string.Empty;
                Latency.Add(sw.ElapsedMilliseconds - oldtime);
                oldtime = sw.ElapsedMilliseconds;

                if (Latency.Count > 100)
                    Latency.RemoveAt(0);

                this.Latency = Latency.Average();

                if (this.Latency > 10 && !warn && sw.ElapsedMilliseconds > 4000)
                {
                    warn = true;
                    //System.Diagnostics.Trace.WriteLine(System.DateTime.UtcNow.ToString("o") + "> " + "Controller " + /*this.DeviceNum*/ + 1 + " (" + this.MacAddress + ") is experiencing latency issues. Currently at " + Math.Round(this.Latency, 2).ToString() + "ms of recomended maximum 10ms");
                }
                else if (this.Latency <= 10 && warn) warn = false;

                if (readTimeout.Interval != 3000.0)
                {
                    if (readTimeout.Interval != 30000.0)
                        readTimeout.Interval = 30000.0;
                    else
                        readTimeout.Interval = 3000.0;
                }
                readTimeout.Enabled = true;

                bool bRead = ReadInputBuffer();
                readTimeout.Enabled = false;

                if(!bRead || !ParseInputBuffer(m_InputBuffer, ref m_InputStruct))
                {
                    continue;
                }

                DateTime utcNow = System.DateTime.UtcNow; // timestamp with UTC in case system time zone changes
                resetHapticState();
                cState.ReportTimeStamp = utcNow;

                cState.LX = m_InputStruct.m_InputLX;
                cState.LY = m_InputStruct.m_InputLY;
                cState.RX = m_InputStruct.m_InputRX;
                cState.RY = m_InputStruct.m_InputRY;
                cState.L2 = m_InputStruct.m_InputL2;
                cState.R2 = m_InputStruct.m_InputR2;

                cState.Triangle = m_InputStruct.m_InputButtons.m_Triangle;
                cState.Circle = m_InputStruct.m_InputButtons.m_Circle;
                cState.Cross = m_InputStruct.m_InputButtons.m_Cross;
                cState.Square = m_InputStruct.m_InputButtons.m_Square;
                cState.DpadUp = m_InputStruct.m_InputButtons.m_DPadUp;
                cState.DpadDown = m_InputStruct.m_InputButtons.m_DPadDown;
                cState.DpadLeft = m_InputStruct.m_InputButtons.m_DPadLeft;
                cState.DpadRight = m_InputStruct.m_InputButtons.m_DPadRight;

                //Convert dpad into individual On/Off bits instead of a clock representation
                byte dpad_state = 0;

                dpad_state = (byte)(
                ((cState.DpadRight ? 1 : 0) << 0) |
                ((cState.DpadLeft ? 1 : 0) << 1) |
	            ((cState.DpadDown ? 1 : 0) << 2) |
                ((cState.DpadUp ? 1 : 0) << 3));

                switch (dpad_state)
                {
                    case 0: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = false; break;
                    case 1: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 2: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 3: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 4: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = false; cState.DpadRight = false; break;
                    case 5: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 6: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 7: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 8: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = false; break;
                }

                cState.R3 = m_InputStruct.m_InputButtons.m_R3;
                cState.L3 = m_InputStruct.m_InputButtons.m_L3;
                cState.Options = m_InputStruct.m_InputButtons.m_Options;
                cState.Share = m_InputStruct.m_InputButtons.m_Share;
                cState.R1 = m_InputStruct.m_InputButtons.m_R1;
                cState.L1 = m_InputStruct.m_InputButtons.m_L1;

                cState.PS = m_InputStruct.m_InputButtons.m_PSButton;
                cState.TouchButton = m_InputStruct.m_InputButtons.m_TouchButton;
                cState.FrameCounter = m_InputStruct.m_InputButtons.m_FrameCounter;

                // Store Gyro and Accel values
                sixAxis.handleSixaxis(ref m_InputStruct.m_SixAxisInput, cState);

                
                charging = m_InputStruct.m_PowerStatus.m_Charging;
                battery = m_InputStruct.m_PowerStatus.m_Battery;
                cState.Battery = (byte)battery;
                if (m_InputStruct.m_PowerStatus.m_Data != priorInputReport30)
                {
                    priorInputReport30 = m_InputStruct.m_PowerStatus.m_Data;
                    Console.WriteLine(MacAddress.ToString() + " " + System.DateTime.UtcNow.ToString("o") + "> power subsystem octet: 0x" + priorInputReport30.ToString("x02"));
                }

                // XXX DS4State mapping needs fixup, turn touches into an array[4] of structs.  And include the touchpad details there instead.
                try
                {
                    
                    for(int i=0; i<m_InputStruct.m_TouchInput.m_Touches; i++)
                    {
                        // Even when idling there is still a touch packet indicating no touch 1 or 2
                        touchpad.handleTouchpad(ref m_InputStruct.m_TouchInput.m_TouchEvents[i], cState);
                    }
                }
                catch { currerror = "Index out of bounds: touchpad"; }
                
                /* Debug output of incoming HID data:
                if (cState.L2 == 0xff && cState.R2 == 0xff)
                {
                    Console.Write(MacAddress.ToString() + " " + System.DateTime.UtcNow.ToString("o") + ">");
                    for (int i = 0; i < inputReport.Length; i++)
                        Console.Write(" " + inputReport[i].ToString("x2"));
                    Console.WriteLine();
                } */
                if (!isDS4Idle())
                    lastActive = utcNow;
                if (conType == ConnectionType.BT)
                {
                    bool shouldDisconnect = false;
                    if (IdleTimeout > 0)
                    {
                        if (isDS4Idle())
                        {
                            DateTime timeout = lastActive + TimeSpan.FromSeconds(IdleTimeout);
                            if (!Charging)
                                shouldDisconnect = utcNow >= timeout;
                        }
                    }
                    if (shouldDisconnect && DisconnectBT())
                        return; // all done
                }
                // XXX fix initialization ordering so the null checks all go away
                if (Report != null)
                    Report(this, EventArgs.Empty);
                sendOutputReport(false);
                if (!string.IsNullOrEmpty(error))
                    error = string.Empty;
                if (!string.IsNullOrEmpty(currerror))
                    error = currerror;                
                cState.CopyTo(pState);
            }
        }

        public void FlushHID()
        {
            hDevice.flush_Queue();
        }
        private void sendOutputReport(bool synchronous)
        {
            byte[] outputReportBuffer = m_OutputReportBuffer;
            byte[] outputReport = m_OutputBuffer;

            setTestRumble();
            setHapticState();
            if (conType == ConnectionType.BT)
            {
                outputReportBuffer[0] = 0x11;
                outputReportBuffer[1] = 0x80;
                outputReportBuffer[3] = 0xff;
                outputReportBuffer[6] = rightLightFastRumble; //fast motor
                outputReportBuffer[7] = leftHeavySlowRumble; //slow motor
                outputReportBuffer[8] = LightBarColor.red; //red
                outputReportBuffer[9] = LightBarColor.green; //green
                outputReportBuffer[10] = LightBarColor.blue; //blue
                outputReportBuffer[11] = ledFlashOn; //flash on duration
                outputReportBuffer[12] = ledFlashOff; //flash off duration
            }
            else
            {
                outputReportBuffer[0] = 0x05;
                outputReportBuffer[1] = 0xff;
                outputReportBuffer[4] = rightLightFastRumble; //fast motor
                outputReportBuffer[5] = leftHeavySlowRumble; //slow  motor
                outputReportBuffer[6] = LightBarColor.red; //red
                outputReportBuffer[7] = LightBarColor.green; //green
                outputReportBuffer[8] = LightBarColor.blue; //blue
                outputReportBuffer[9] = ledFlashOn; //flash on duration
                outputReportBuffer[10] = ledFlashOff; //flash off duration
            }
            lock (outputReport)
            {
                if (synchronous)
                {
                    outputReportBuffer.CopyTo(outputReport, 0);
                    try
                    {
                        if (!writeOutput())
                        {
                            Console.WriteLine(MacAddress.ToString() + " " + System.DateTime.UtcNow.ToString("o") + "> encountered synchronous write failure: " + Marshal.GetLastWin32Error());
                            ds4Output.Abort();
                            ds4Output.Join();
                        }
                    }
                    catch
                    {
                        // If it's dead already, don't worry about it.
                    }
                }
                else
                {
                    bool output = false;
                    for (int i = 0; !output && i < outputReport.Length; i++)
                        output = outputReport[i] != outputReportBuffer[i];
                    if (output)
                    {
                        outputReportBuffer.CopyTo(outputReport, 0);
                        Monitor.Pulse(outputReport);
                    }
                }
            }
        }

        public bool DisconnectBT()
        {
            if (Mac != null)
            {
                Console.WriteLine("Trying to disconnect BT device " + Mac);
                IntPtr btHandle = IntPtr.Zero;
                int IOCTL_BTH_DISCONNECT_DEVICE = 0x41000c;

                byte[] btAddr = new byte[8];
                string[] sbytes = Mac.Split(':');
                for (int i = 0; i < 6; i++)
                {
                    //parse hex byte in reverse order
                    btAddr[5 - i] = Convert.ToByte(sbytes[i], 16);
                }
                long lbtAddr = BitConverter.ToInt64(btAddr, 0);

                NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS p = new NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS();
                p.dwSize = Marshal.SizeOf(typeof(NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS));
                IntPtr searchHandle = NativeMethods.BluetoothFindFirstRadio(ref p, ref btHandle);
                int bytesReturned = 0;
                bool success = false;
                while (!success && btHandle != IntPtr.Zero)
                {
                    success = NativeMethods.DeviceIoControl(btHandle, IOCTL_BTH_DISCONNECT_DEVICE, ref lbtAddr, 8, IntPtr.Zero, 0, ref bytesReturned, IntPtr.Zero);
                    NativeMethods.CloseHandle(btHandle);
                    if (!success)
                        if (!NativeMethods.BluetoothFindNextRadio(searchHandle, ref btHandle))
                            btHandle = IntPtr.Zero;

                }
                NativeMethods.BluetoothFindRadioClose(searchHandle);
                Console.WriteLine("Disconnect successful: " + success);
                success = true; // XXX return value indicates failure, but it still works?
                if(success)
                {
                    IsDisconnecting = true;
                    StopOutputUpdate();
                    if (Removal != null)
                        Removal(this, EventArgs.Empty);
                }
                return success;
            }
            return false;
        }

        private DS4HapticState testRumble = new DS4HapticState();
        public void setRumble(byte rightLightFastMotor, byte leftHeavySlowMotor)
        {
            testRumble.RumbleMotorStrengthRightLightFast = rightLightFastMotor;
            testRumble.RumbleMotorStrengthLeftHeavySlow = leftHeavySlowMotor;
            testRumble.RumbleMotorsExplicitlyOff = rightLightFastMotor == 0 && leftHeavySlowMotor == 0;
        }

        private void setTestRumble()
        {
            if (testRumble.IsRumbleSet())
            {
                pushHapticState(testRumble);
                if (testRumble.RumbleMotorsExplicitlyOff)
                    testRumble.RumbleMotorsExplicitlyOff = false;
            }
        }

        public DS4State getCurrentState()
        {
            return cState.Clone();
        }

        public DS4State getPreviousState()
        {
            return pState.Clone();
        }

        public void getExposedState(DS4StateExposed expState, DS4State state)
        {
            cState.CopyTo(state);
            //expState.Accel = accel;
            //expState.Gyro = gyro;
        }

        public void getCurrentState(DS4State state)
        {
            cState.CopyTo(state);
        }

        public void getPreviousState(DS4State state)
        {
            pState.CopyTo(state);
        }

        private bool isDS4Idle()
        {
            if (cState.Square || cState.Cross || cState.Circle || cState.Triangle)
                return false;
            if (cState.DpadUp || cState.DpadLeft || cState.DpadDown || cState.DpadRight)
                return false;
            if (cState.L3 || cState.R3 || cState.L1 || cState.R1 || cState.Share || cState.Options)
                return false;
            if (cState.L2 != 0 || cState.R2 != 0)
                return false;
            // TODO calibrate to get an accurate jitter and center-play range and centered position
            const int slop = 64;
            if (cState.LX <= 127 - slop || cState.LX >= 128 + slop || cState.LY <= 127 - slop || cState.LY >= 128 + slop)
                return false;
            if (cState.RX <= 127 - slop || cState.RX >= 128 + slop || cState.RY <= 127 - slop || cState.RY >= 128 + slop)
                return false;
            if (cState.Touch1 || cState.Touch2 || cState.TouchButton)
                return false;
            return true;
        }

        private DS4HapticState[] hapticState = new DS4HapticState[1];
        private int hapticStackIndex = 0;
        private void resetHapticState()
        {
            hapticStackIndex = 0;
        }

        // Use the "most recently set" haptic state for each of light bar/motor.
        private void setHapticState()
        {
            int i = 0;
            DS4Color lightBarColor = LightBarColor;
            byte lightBarFlashDurationOn = LightBarOnDuration, lightBarFlashDurationOff = LightBarOffDuration;
            byte rumbleMotorStrengthLeftHeavySlow = LeftHeavySlowRumble, rumbleMotorStrengthRightLightFast = rightLightFastRumble;
            foreach (DS4HapticState haptic in hapticState)
            {
                if (i++ == hapticStackIndex)
                    break; // rest haven't been used this time
                if (haptic.IsLightBarSet())
                {
                    lightBarColor = haptic.LightBarColor;
                    lightBarFlashDurationOn = haptic.LightBarFlashDurationOn;
                    lightBarFlashDurationOff = haptic.LightBarFlashDurationOff;
                }
                if (haptic.IsRumbleSet())
                {
                    rumbleMotorStrengthLeftHeavySlow = haptic.RumbleMotorStrengthLeftHeavySlow;
                    rumbleMotorStrengthRightLightFast = haptic.RumbleMotorStrengthRightLightFast;
                }
            }
            LightBarColor = lightBarColor;
            LightBarOnDuration = lightBarFlashDurationOn;
            LightBarOffDuration = lightBarFlashDurationOff;
            LeftHeavySlowRumble = rumbleMotorStrengthLeftHeavySlow;
            RightLightFastRumble = rumbleMotorStrengthRightLightFast;
        }

        public void pushHapticState(DS4HapticState hs)
        {
            if (hapticStackIndex == hapticState.Length)
            {
                DS4HapticState[] newHaptics = new DS4HapticState[hapticState.Length + 1];
                Array.Copy(hapticState, newHaptics, hapticState.Length);
                hapticState = newHaptics;
            }
            hapticState[hapticStackIndex++] = hs;
        }

        override
        public String ToString()
        {
            return Mac;
        }
    }
}
