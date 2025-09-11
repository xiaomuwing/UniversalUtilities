using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
namespace UniversalUtilities
{
    public struct NetworkConnectionParameter
    {
        public NetworkStream stream;
        public byte[] bytes;
        public int portIn;
        public IPAddress ipAddressIn;
    }

    public class ModbusServer
    {
        private readonly bool debug = false;
        byte[] bytes = new byte[2100];
        public HoldingRegisters holdingRegisters;
        public InputRegisters inputRegisters;
        public Coils coils;
        public DiscreteInputs discreteInputs;
        private int portIn;
        private IPAddress ipAddressIn;
        private UdpClient udpClient;
        private IPEndPoint iPEndPoint;
        private TCPHandler tcpHandler;
        private readonly bool udpFlag;
        private readonly bool serialFlag;
        private string serialPort = "COM1";
        private SerialPort serialport;
        Thread listenerThread;
        public bool FunctionCode1Disabled { get; set; }
        public bool FunctionCode2Disabled { get; set; }
        public bool FunctionCode3Disabled { get; set; }
        public bool FunctionCode4Disabled { get; set; }
        public bool FunctionCode5Disabled { get; set; }
        public bool FunctionCode6Disabled { get; set; }
        public bool FunctionCode15Disabled { get; set; }
        public bool FunctionCode16Disabled { get; set; }
        public bool FunctionCode23Disabled { get; set; }
        public bool PortChanged { get; set; }
        object lockCoils = new object();
        object lockHoldingRegisters = new object();
        private volatile bool shouldStop;
        public ModbusServer()
        {
            holdingRegisters = new HoldingRegisters(this);
            inputRegisters = new InputRegisters(this);
            coils = new Coils(this);
            discreteInputs = new DiscreteInputs(this);

        }
        #region events
        public delegate void CoilsChangedHandler(int coil, int numberOfCoils);
        public event CoilsChangedHandler CoilsChanged;

        public delegate void HoldingRegistersChangedHandler(int register, int numberOfRegisters);
        public event HoldingRegistersChangedHandler HoldingRegistersChanged;

        public delegate void NumberOfConnectedClientsChangedHandler();
        public event NumberOfConnectedClientsChangedHandler NumberOfConnectedClientsChanged;

        public delegate void LogDataChangedHandler();
        public event LogDataChangedHandler LogDataChanged;
        #endregion
        public void Listen()
        {

            listenerThread = new Thread(ListenerThread);
            listenerThread.Start();
        }
        public void StopListening()
        {
            try
            {
                tcpHandler.Disconnect();
                listenerThread.Abort();
            }
            catch (Exception) { }
            listenerThread.Join();
        }
        private void ListenerThread()
        {
            if (!udpFlag & !serialFlag)
            {
                if (udpClient != null)
                {
                    try
                    {
                        udpClient.Close();
                    }
                    catch (Exception) { }
                }
                tcpHandler = new TCPHandler(Port);
                tcpHandler.dataChanged += new TCPHandler.DataChanged(ProcessReceivedData);
                tcpHandler.numberOfClientsChanged += new TCPHandler.NumberOfClientsChanged(numberOfClientsChanged);
            }
            else if (serialFlag)
            {
                if (serialport == null)
                {
                    serialport = new SerialPort();
                    serialport.PortName = serialPort;
                    serialport.BaudRate = this.Baudrate;
                    serialport.Parity = this.Parity;
                    serialport.StopBits = StopBits;
                    serialport.WriteTimeout = 10000;
                    serialport.ReadTimeout = 1000;
                    serialport.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                    serialport.Open();
                }
            }
            else
                while (!shouldStop)
                {
                    if (udpFlag)
                    {
                        if (udpClient == null | PortChanged)
                        {
                            udpClient = new UdpClient(Port);
                            udpClient.Client.ReceiveTimeout = 1000;
                            iPEndPoint = new IPEndPoint(IPAddress.Any, Port);
                            PortChanged = false;
                        }
                        if (tcpHandler != null)
                            tcpHandler.Disconnect();
                        try
                        {
                            bytes = udpClient.Receive(ref iPEndPoint);
                            portIn = iPEndPoint.Port;
                            NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
                            networkConnectionParameter.bytes = bytes;
                            ipAddressIn = iPEndPoint.Address;
                            networkConnectionParameter.portIn = portIn;
                            networkConnectionParameter.ipAddressIn = ipAddressIn;
                            ParameterizedThreadStart pts = new ParameterizedThreadStart(this.ProcessReceivedData);
                            Thread processDataThread = new Thread(pts);
                            processDataThread.Start(networkConnectionParameter);
                        }
                        catch (Exception)
                        {
                        }
                    }

                }
        }
        #region SerialHandler
        private bool dataReceived = false;
        private byte[] readBuffer = new byte[2094];
        private DateTime lastReceive;
        private int nextSign = 0;
        private bool DetectValidModbusFrame(byte[] readBuffer, int length)
        {
            // minimum length 6 bytes
            if (length < 6)
                return false;
            //SlaveID correct
            if ((readBuffer[0] < 1) | (readBuffer[0] > 247))
                return false;
            //CRC correct?
            byte[] crc = new byte[2];
            crc = BitConverter.GetBytes(calculateCRC(readBuffer, (ushort)(length - 2), 0));
            if (crc[0] != readBuffer[length - 2] | crc[1] != readBuffer[length - 1])
                return false;
            return true;
        }
        private ushort calculateCRC(byte[] data, ushort numberOfBytes, int startByte)
        {
            byte[] auchCRCHi = {
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
            0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
            0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81,
            0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
            0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
            0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
            0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
            0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
            0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
            0x40
            };

            byte[] auchCRCLo = {
            0x00, 0xC0, 0xC1, 0x01, 0xC3, 0x03, 0x02, 0xC2, 0xC6, 0x06, 0x07, 0xC7, 0x05, 0xC5, 0xC4,
            0x04, 0xCC, 0x0C, 0x0D, 0xCD, 0x0F, 0xCF, 0xCE, 0x0E, 0x0A, 0xCA, 0xCB, 0x0B, 0xC9, 0x09,
            0x08, 0xC8, 0xD8, 0x18, 0x19, 0xD9, 0x1B, 0xDB, 0xDA, 0x1A, 0x1E, 0xDE, 0xDF, 0x1F, 0xDD,
            0x1D, 0x1C, 0xDC, 0x14, 0xD4, 0xD5, 0x15, 0xD7, 0x17, 0x16, 0xD6, 0xD2, 0x12, 0x13, 0xD3,
            0x11, 0xD1, 0xD0, 0x10, 0xF0, 0x30, 0x31, 0xF1, 0x33, 0xF3, 0xF2, 0x32, 0x36, 0xF6, 0xF7,
            0x37, 0xF5, 0x35, 0x34, 0xF4, 0x3C, 0xFC, 0xFD, 0x3D, 0xFF, 0x3F, 0x3E, 0xFE, 0xFA, 0x3A,
            0x3B, 0xFB, 0x39, 0xF9, 0xF8, 0x38, 0x28, 0xE8, 0xE9, 0x29, 0xEB, 0x2B, 0x2A, 0xEA, 0xEE,
            0x2E, 0x2F, 0xEF, 0x2D, 0xED, 0xEC, 0x2C, 0xE4, 0x24, 0x25, 0xE5, 0x27, 0xE7, 0xE6, 0x26,
            0x22, 0xE2, 0xE3, 0x23, 0xE1, 0x21, 0x20, 0xE0, 0xA0, 0x60, 0x61, 0xA1, 0x63, 0xA3, 0xA2,
            0x62, 0x66, 0xA6, 0xA7, 0x67, 0xA5, 0x65, 0x64, 0xA4, 0x6C, 0xAC, 0xAD, 0x6D, 0xAF, 0x6F,
            0x6E, 0xAE, 0xAA, 0x6A, 0x6B, 0xAB, 0x69, 0xA9, 0xA8, 0x68, 0x78, 0xB8, 0xB9, 0x79, 0xBB,
            0x7B, 0x7A, 0xBA, 0xBE, 0x7E, 0x7F, 0xBF, 0x7D, 0xBD, 0xBC, 0x7C, 0xB4, 0x74, 0x75, 0xB5,
            0x77, 0xB7, 0xB6, 0x76, 0x72, 0xB2, 0xB3, 0x73, 0xB1, 0x71, 0x70, 0xB0, 0x50, 0x90, 0x91,
            0x51, 0x93, 0x53, 0x52, 0x92, 0x96, 0x56, 0x57, 0x97, 0x55, 0x95, 0x94, 0x54, 0x9C, 0x5C,
            0x5D, 0x9D, 0x5F, 0x9F, 0x9E, 0x5E, 0x5A, 0x9A, 0x9B, 0x5B, 0x99, 0x59, 0x58, 0x98, 0x88,
            0x48, 0x49, 0x89, 0x4B, 0x8B, 0x8A, 0x4A, 0x4E, 0x8E, 0x8F, 0x4F, 0x8D, 0x4D, 0x4C, 0x8C,
            0x44, 0x84, 0x85, 0x45, 0x87, 0x47, 0x46, 0x86, 0x82, 0x42, 0x43, 0x83, 0x41, 0x81, 0x80,
            0x40
            };
            UInt16 usDataLen = numberOfBytes;
            byte uchCRCHi = 0xFF;
            byte uchCRCLo = 0xFF;
            int i = 0;
            int uIndex;
            while (usDataLen > 0)
            {
                usDataLen--;
                if ((i + startByte) < data.Length)
                {
                    uIndex = uchCRCLo ^ data[i + startByte];
                    uchCRCLo = (byte)(uchCRCHi ^ auchCRCHi[uIndex]);
                    uchCRCHi = auchCRCLo[uIndex];
                }
                i++;
            }
            return (UInt16)((UInt16)uchCRCHi << 8 | uchCRCLo);
        }
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            int silence = 4000 / Baudrate;
            if ((DateTime.Now.Ticks - lastReceive.Ticks) > TimeSpan.TicksPerMillisecond * silence)
                nextSign = 0;


            SerialPort sp = (SerialPort)sender;

            int numbytes = sp.BytesToRead;
            byte[] rxbytearray = new byte[numbytes];

            sp.Read(rxbytearray, 0, numbytes);

            Array.Copy(rxbytearray, 0, readBuffer, nextSign, rxbytearray.Length);
            lastReceive = DateTime.Now;
            nextSign = numbytes + nextSign;
            if (DetectValidModbusFrame(readBuffer, nextSign))
            {

                dataReceived = true;
                nextSign = 0;

                NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
                networkConnectionParameter.bytes = readBuffer;
                ParameterizedThreadStart pts = new ParameterizedThreadStart(this.ProcessReceivedData);
                Thread processDataThread = new Thread(pts);
                processDataThread.Start(networkConnectionParameter);
                dataReceived = false;

            }
            else
                dataReceived = false;
        }
        #endregion
        #region Method numberOfClientsChanged
        private void numberOfClientsChanged()
        {
            NumberOfConnections = tcpHandler.NumberOfConnectedClients;
            if (NumberOfConnectedClientsChanged != null)
                NumberOfConnectedClientsChanged();
        }
        #endregion
        object lockProcessReceivedData = new object();
        #region Method ProcessReceivedData
        private void ProcessReceivedData(object networkConnectionParameter)
        {
            lock (lockProcessReceivedData)
            {
                Byte[] bytes = new byte[((NetworkConnectionParameter)networkConnectionParameter).bytes.Length];
                NetworkStream stream = ((NetworkConnectionParameter)networkConnectionParameter).stream;
                int portIn = ((NetworkConnectionParameter)networkConnectionParameter).portIn;
                IPAddress ipAddressIn = ((NetworkConnectionParameter)networkConnectionParameter).ipAddressIn;


                Array.Copy(((NetworkConnectionParameter)networkConnectionParameter).bytes, 0, bytes, 0, ((NetworkConnectionParameter)networkConnectionParameter).bytes.Length);

                ModbusProtocol receiveDataThread = new ModbusProtocol();
                ModbusProtocol sendDataThread = new ModbusProtocol();

                try
                {
                    UInt16[] wordData = new UInt16[1];
                    byte[] byteData = new byte[2];
                    receiveDataThread.timeStamp = DateTime.Now;
                    receiveDataThread.request = true;
                    if (!SerialFlag)
                    {
                        //Lese Transaction identifier
                        byteData[1] = bytes[0];
                        byteData[0] = bytes[1];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.transactionIdentifier = wordData[0];

                        //Lese Protocol identifier
                        byteData[1] = bytes[2];
                        byteData[0] = bytes[3];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.protocolIdentifier = wordData[0];

                        //Lese length
                        byteData[1] = bytes[4];
                        byteData[0] = bytes[5];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.length = wordData[0];
                    }

                    //Lese unit identifier
                    receiveDataThread.unitIdentifier = bytes[6 - 6 * Convert.ToInt32(SerialFlag)];
                    //Check UnitIdentifier
                    if ((receiveDataThread.unitIdentifier != this.UnitIdentifier) & (receiveDataThread.unitIdentifier != 0))
                        return;

                    // Lese function code
                    receiveDataThread.functionCode = bytes[7 - 6 * Convert.ToInt32(SerialFlag)];

                    // Lese starting address 
                    byteData[1] = bytes[8 - 6 * Convert.ToInt32(SerialFlag)];
                    byteData[0] = bytes[9 - 6 * Convert.ToInt32(SerialFlag)];
                    Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                    receiveDataThread.startingAdress = wordData[0];

                    if (receiveDataThread.functionCode <= 4)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.quantity = wordData[0];
                    }
                    if (receiveDataThread.functionCode == 5)
                    {
                        receiveDataThread.receiveCoilValues = new ushort[1];
                        // Lese Value
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, receiveDataThread.receiveCoilValues, 0, 2);
                    }
                    if (receiveDataThread.functionCode == 6)
                    {
                        receiveDataThread.receiveRegisterValues = new ushort[1];
                        // Lese Value
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, receiveDataThread.receiveRegisterValues, 0, 2);
                    }
                    if (receiveDataThread.functionCode == 15)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.quantity = wordData[0];

                        receiveDataThread.byteCount = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];

                        if ((receiveDataThread.byteCount % 2) != 0)
                            receiveDataThread.receiveCoilValues = new ushort[receiveDataThread.byteCount / 2 + 1];
                        else
                            receiveDataThread.receiveCoilValues = new ushort[receiveDataThread.byteCount / 2];
                        // Lese Value
                        Buffer.BlockCopy(bytes, 13 - 6 * Convert.ToInt32(SerialFlag), receiveDataThread.receiveCoilValues, 0, receiveDataThread.byteCount);
                    }
                    if (receiveDataThread.functionCode == 16)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.quantity = wordData[0];

                        receiveDataThread.byteCount = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];
                        receiveDataThread.receiveRegisterValues = new ushort[receiveDataThread.quantity];
                        for (int i = 0; i < receiveDataThread.quantity; i++)
                        {
                            // Lese Value
                            byteData[1] = bytes[13 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            byteData[0] = bytes[14 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            Buffer.BlockCopy(byteData, 0, receiveDataThread.receiveRegisterValues, i * 2, 2);
                        }

                    }
                    if (receiveDataThread.functionCode == 23)
                    {
                        // Lese starting Address Read
                        byteData[1] = bytes[8 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[9 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.startingAddressRead = wordData[0];
                        // Lese quantity Read
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.quantityRead = wordData[0];
                        // Lese starting Address Write
                        byteData[1] = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[13 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.startingAddressWrite = wordData[0];
                        // Lese quantity Write
                        byteData[1] = bytes[14 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[15 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.quantityWrite = wordData[0];

                        receiveDataThread.byteCount = bytes[16 - 6 * Convert.ToInt32(SerialFlag)];
                        receiveDataThread.receiveRegisterValues = new ushort[receiveDataThread.quantityWrite];
                        for (int i = 0; i < receiveDataThread.quantityWrite; i++)
                        {
                            // Lese Value
                            byteData[1] = bytes[17 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            byteData[0] = bytes[18 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            Buffer.BlockCopy(byteData, 0, receiveDataThread.receiveRegisterValues, i * 2, 2);
                        }
                    }
                }
                catch (Exception exc)
                { }
                this.CreateAnswer(receiveDataThread, sendDataThread, stream, portIn, ipAddressIn);
                //this.sendAnswer();
                this.CreateLogData(receiveDataThread, sendDataThread);

                if (LogDataChanged != null)
                    LogDataChanged();
            }
        }
        #endregion
        #region Method CreateAnswer
        private void CreateAnswer(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {

            switch (receiveData.functionCode)
            {
                // Read Coils
                case 1:
                    if (!FunctionCode1Disabled)
                        this.ReadCoils(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    break;
                // Read Input Registers
                case 2:
                    if (!FunctionCode2Disabled)
                        this.ReadDiscreteInputs(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Read Holding Registers
                case 3:
                    if (!FunctionCode3Disabled)
                        this.ReadHoldingRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Read Input Registers
                case 4:
                    if (!FunctionCode4Disabled)
                        this.ReadInputRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Write single coil
                case 5:
                    if (!FunctionCode5Disabled)
                        this.WriteSingleCoil(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Write single register
                case 6:
                    if (!FunctionCode6Disabled)
                        this.WriteSingleRegister(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Write Multiple coils
                case 15:
                    if (!FunctionCode15Disabled)
                        this.WriteMultipleCoils(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Write Multiple registers
                case 16:
                    if (!FunctionCode16Disabled)
                        this.WriteMultipleRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Error: Function Code not supported
                case 23:
                    if (!FunctionCode23Disabled)
                        this.ReadWriteMultipleRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                        sendData.exceptionCode = 1;
                        sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }

                    break;
                // Error: Function Code not supported
                default:
                    sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                    sendData.exceptionCode = 1;
                    sendException(sendData.errorCode, sendData.exceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    break;
            }
            sendData.timeStamp = DateTime.Now;
        }
        #endregion
        private void ReadCoils(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            if ((receiveData.quantity < 1) | (receiveData.quantity > 0x07D0))  //Invalid quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1 + receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))     //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                if ((receiveData.quantity % 8) == 0)
                    sendData.byteCount = (byte)(receiveData.quantity / 8);
                else
                    sendData.byteCount = (byte)(receiveData.quantity / 8 + 1);

                sendData.sendCoilValues = new bool[receiveData.quantity];
                lock (lockCoils)
                    Array.Copy(coils.localArray, receiveData.startingAdress + 1, sendData.sendCoilValues, 0, receiveData.quantity);
            }
            if (true)
            {
                Byte[] data;

                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];

                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];
                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;

                //ByteCount
                data[8] = sendData.byteCount;

                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendCoilValues = null;
                }

                if (sendData.sendCoilValues != null)
                    for (int i = 0; i < (sendData.byteCount); i++)
                    {
                        byteData = new byte[2];
                        for (int j = 0; j < 8; j++)
                        {

                            byte boolValue;
                            if (sendData.sendCoilValues[i * 8 + j] == true)
                                boolValue = 1;
                            else
                                boolValue = 0;
                            byteData[1] = (byte)((byteData[1]) | (boolValue << j));
                            if ((i * 8 + j + 1) >= sendData.sendCoilValues.Length)
                                break;
                        }
                        data[9 + i] = byteData[1];
                    }
                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }
                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
            }
        }
        private void ReadDiscreteInputs(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            if ((receiveData.quantity < 1) | (receiveData.quantity > 0x07D0))  //Invalid quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1 + receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                if ((receiveData.quantity % 8) == 0)
                    sendData.byteCount = (byte)(receiveData.quantity / 8);
                else
                    sendData.byteCount = (byte)(receiveData.quantity / 8 + 1);

                sendData.sendCoilValues = new bool[receiveData.quantity];
                Array.Copy(discreteInputs.localArray, receiveData.startingAdress + 1, sendData.sendCoilValues, 0, receiveData.quantity);
            }
            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;

                //ByteCount
                data[8] = sendData.byteCount;


                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendCoilValues = null;
                }

                if (sendData.sendCoilValues != null)
                    for (int i = 0; i < (sendData.byteCount); i++)
                    {
                        byteData = new byte[2];
                        for (int j = 0; j < 8; j++)
                        {

                            byte boolValue;
                            if (sendData.sendCoilValues[i * 8 + j] == true)
                                boolValue = 1;
                            else
                                boolValue = 0;
                            byteData[1] = (byte)((byteData[1]) | (boolValue << j));
                            if ((i * 8 + j + 1) >= sendData.sendCoilValues.Length)
                                break;
                        }
                        data[9 + i] = byteData[1];
                    }

                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }
                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
            }
        }
        private void ReadHoldingRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            if ((receiveData.quantity < 1) | (receiveData.quantity > 0x007D))  //Invalid quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1 + receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                sendData.byteCount = (byte)(2 * receiveData.quantity);
                sendData.sendRegisterValues = new ushort[receiveData.quantity];
                lock (lockHoldingRegisters)
                    Buffer.BlockCopy(holdingRegisters.localArray, receiveData.startingAdress * 2 + 2, sendData.sendRegisterValues, 0, receiveData.quantity * 2);
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = (ushort)(0x03 + sendData.byteCount);

            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;

                //ByteCount
                data[8] = sendData.byteCount;

                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }


                if (sendData.sendRegisterValues != null)
                    for (int i = 0; i < (sendData.byteCount / 2); i++)
                    {
                        byteData = BitConverter.GetBytes((Int16)sendData.sendRegisterValues[i]);
                        data[9 + i * 2] = byteData[1];
                        data[10 + i * 2] = byteData[0];
                    }
                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }
                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
            }
        }
        private void ReadInputRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            if ((receiveData.quantity < 1) | (receiveData.quantity > 0x007D))  //Invalid quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1 + receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                sendData.byteCount = (byte)(2 * receiveData.quantity);
                sendData.sendRegisterValues = new ushort[receiveData.quantity];
                Buffer.BlockCopy(inputRegisters.localArray, receiveData.startingAdress * 2 + 2, sendData.sendRegisterValues, 0, receiveData.quantity * 2);
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = (ushort)(0x03 + sendData.byteCount);

            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;

                //ByteCount
                data[8] = sendData.byteCount;


                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }


                if (sendData.sendRegisterValues != null)
                    for (int i = 0; i < (sendData.byteCount / 2); i++)
                    {
                        byteData = BitConverter.GetBytes((Int16)sendData.sendRegisterValues[i]);
                        data[9 + i * 2] = byteData[1];
                        data[10 + i * 2] = byteData[0];
                    }
                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
            }
        }
        private void WriteSingleCoil(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            sendData.startingAdress = receiveData.startingAdress;
            sendData.receiveCoilValues = receiveData.receiveCoilValues;
            if ((receiveData.receiveCoilValues[0] != 0x0000) & (receiveData.receiveCoilValues[0] != 0xFF00))  //Invalid Value
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1) > 65535) | (receiveData.startingAdress < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                if (receiveData.receiveCoilValues[0] == 0xFF00)
                {
                    lock (lockCoils)
                        coils[receiveData.startingAdress + 1] = true;
                }
                if (receiveData.receiveCoilValues[0] == 0x0000)
                {
                    lock (lockCoils)
                        coils[receiveData.startingAdress + 1] = false;
                }
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = 0x06;

            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;



                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.startingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.receiveCoilValues[0]);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
                if (CoilsChanged != null)
                    CoilsChanged(receiveData.startingAdress + 1, 1);
            }
        }
        private void WriteSingleRegister(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            sendData.startingAdress = receiveData.startingAdress;
            sendData.receiveRegisterValues = receiveData.receiveRegisterValues;

            if ((receiveData.receiveRegisterValues[0] < 0x0000) | (receiveData.receiveRegisterValues[0] > 0xFFFF))  //Invalid Value
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if (((receiveData.startingAdress + 1) > 65535) | (receiveData.startingAdress < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                lock (lockHoldingRegisters)
                    holdingRegisters[receiveData.startingAdress + 1] = unchecked((short)receiveData.receiveRegisterValues[0]);
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = 0x06;

            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);


                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;



                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.startingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.receiveRegisterValues[0]);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
                if (HoldingRegistersChanged != null)
                    HoldingRegistersChanged(receiveData.startingAdress + 1, 1);
            }
        }
        private void WriteMultipleCoils(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            sendData.startingAdress = receiveData.startingAdress;
            sendData.quantity = receiveData.quantity;

            if ((receiveData.quantity == 0x0000) | (receiveData.quantity > 0x07B0))  //Invalid Quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if ((((int)receiveData.startingAdress + 1 + (int)receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                lock (lockCoils)
                    for (int i = 0; i < receiveData.quantity; i++)
                    {
                        int shift = i % 16;
                        /*                if ((i == receiveData.quantity - 1) & (receiveData.quantity % 2 != 0))
                                        {
                                            if (shift < 8)
                                                shift = shift + 8;
                                            else
                                                shift = shift - 8;
                                        }*/
                        int mask = 0x1;
                        mask = mask << (shift);
                        if ((receiveData.receiveCoilValues[i / 16] & (ushort)mask) == 0)

                            coils[receiveData.startingAdress + i + 1] = false;
                        else

                            coils[receiveData.startingAdress + i + 1] = true;

                    }
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = 0x06;
            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;



                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.startingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.quantity);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
                if (CoilsChanged != null)
                    CoilsChanged(receiveData.startingAdress + 1, receiveData.quantity);
            }
        }
        private void WriteMultipleRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;
            sendData.startingAdress = receiveData.startingAdress;
            sendData.quantity = receiveData.quantity;

            if ((receiveData.quantity == 0x0000) | (receiveData.quantity > 0x07B0))  //Invalid Quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if ((((int)receiveData.startingAdress + 1 + (int)receiveData.quantity) > 65535) | (receiveData.startingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                lock (lockHoldingRegisters)
                    for (int i = 0; i < receiveData.quantity; i++)
                    {
                        holdingRegisters[receiveData.startingAdress + i + 1] = unchecked((short)receiveData.receiveRegisterValues[i]);
                    }
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = 0x06;
            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;



                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.startingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.quantity);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
                HoldingRegistersChanged?.Invoke(receiveData.startingAdress + 1, receiveData.quantity);
            }
        }
        private void ReadWriteMultipleRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = this.UnitIdentifier;
            sendData.functionCode = receiveData.functionCode;


            if ((receiveData.quantityRead < 0x0001) | (receiveData.quantityRead > 0x007D) | (receiveData.quantityWrite < 0x0001) | (receiveData.quantityWrite > 0x0079) | (receiveData.byteCount != (receiveData.quantityWrite * 2)))  //Invalid Quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 3;
            }
            if ((((int)receiveData.startingAddressRead + 1 + (int)receiveData.quantityRead) > 65535) | (((int)receiveData.startingAddressWrite + 1 + (int)receiveData.quantityWrite) > 65535) | (receiveData.quantityWrite < 0) | (receiveData.quantityRead < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.errorCode = (byte)(receiveData.functionCode + 0x80);
                sendData.exceptionCode = 2;
            }
            if (sendData.exceptionCode == 0)
            {
                sendData.sendRegisterValues = new ushort[receiveData.quantityRead];
                lock (lockHoldingRegisters)
                    Buffer.BlockCopy(holdingRegisters.localArray, receiveData.startingAddressRead * 2 + 2, sendData.sendRegisterValues, 0, receiveData.quantityRead * 2);

                lock (holdingRegisters)
                    for (int i = 0; i < receiveData.quantityWrite; i++)
                    {
                        holdingRegisters[receiveData.startingAddressWrite + i + 1] = unchecked((short)receiveData.receiveRegisterValues[i]);
                    }
                sendData.byteCount = (byte)(2 * receiveData.quantityRead);
            }
            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = Convert.ToUInt16(3 + 2 * receiveData.quantityRead);
            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData = new byte[2];

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;

                //Function Code
                data[7] = sendData.functionCode;

                //ByteCount
                data[8] = sendData.byteCount;


                if (sendData.exceptionCode > 0)
                {
                    data[7] = sendData.errorCode;
                    data[8] = sendData.exceptionCode;
                    sendData.sendRegisterValues = null;
                }
                else
                {
                    if (sendData.sendRegisterValues != null)
                        for (int i = 0; i < (sendData.byteCount / 2); i++)
                        {
                            byteData = BitConverter.GetBytes((Int16)sendData.sendRegisterValues[i]);
                            data[9 + i * 2] = byteData[1];
                            data[10 + i * 2] = byteData[0];
                        }

                }


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }

                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
                HoldingRegistersChanged?.Invoke(receiveData.startingAddressWrite + 1, receiveData.quantityWrite);
            }
        }
        private void sendException(int errorCode, int exceptionCode, ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.response = true;

            sendData.transactionIdentifier = receiveData.transactionIdentifier;
            sendData.protocolIdentifier = receiveData.protocolIdentifier;

            sendData.unitIdentifier = receiveData.unitIdentifier;
            sendData.errorCode = (byte)errorCode;
            sendData.exceptionCode = (byte)exceptionCode;

            if (sendData.exceptionCode > 0)
                sendData.length = 0x03;
            else
                sendData.length = (ushort)(0x03 + sendData.byteCount);

            if (true)
            {
                Byte[] data;
                if (sendData.exceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.byteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData = new byte[2];
                sendData.length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.transactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.protocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.unitIdentifier;


                data[7] = sendData.errorCode;
                data[8] = sendData.exceptionCode;


                try
                {
                    if (SerialFlag)
                    {
                        if (!serialport.IsOpen)
                            throw new Exception("serial port not opened");
                        //Create CRC
                        sendData.crc = calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                        }
                    }
                    else if (UDPFlag)
                    {
                        //UdpClient udpClient = new UdpClient();
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception) { }
            }
        }
        private void CreateLogData(ModbusProtocol receiveData, ModbusProtocol sendData)
        {
            for (int i = 0; i < 98; i++)
            {
                ModbusLogData[99 - i] = ModbusLogData[99 - i - 2];

            }
            ModbusLogData[0] = receiveData;
            ModbusLogData[1] = sendData;

        }
        public int NumberOfConnections { get; private set; } = 0;
        ModbusProtocol[] ModbusLogData { get; } = new ModbusProtocol[100];
        public int Port { get; set; } = 505;
        public bool UDPFlag { get; set; }
        public bool SerialFlag { get; set; }
        public int Baudrate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.Even;
        public StopBits StopBits { get; set; } = StopBits.One;
        public string SerialPort
        {
            get
            {
                return serialPort;
            }
            set
            {
                serialPort = value;
                if (serialPort != null)
                    SerialFlag = true;
                else
                    SerialFlag = false;
            }
        }
        public byte UnitIdentifier { get; set; } = 1;
        public class HoldingRegisters
        {
            public short[] localArray = new short[65535];
            ModbusServer modbusServer;

            public HoldingRegisters(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public short this[int x]
            {
                get { return this.localArray[x]; }
                set
                {
                    this.localArray[x] = value;

                }
            }
        }
        public class InputRegisters
        {
            public short[] localArray = new short[65535];
            ModbusServer modbusServer;

            public InputRegisters(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public Int16 this[int x]
            {
                get { return this.localArray[x]; }
                set
                {
                    this.localArray[x] = value;

                }
            }
        }
        public class Coils
        {
            public bool[] localArray = new bool[65535];
            ModbusServer modbusServer;

            public Coils(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public bool this[int x]
            {
                get { return this.localArray[x]; }
                set
                {
                    this.localArray[x] = value;

                }
            }
        }
        public class DiscreteInputs
        {
            public bool[] localArray = new bool[65535];
            ModbusServer modbusServer;

            public DiscreteInputs(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public bool this[int x]
            {
                get { return this.localArray[x]; }
                set
                {
                    this.localArray[x] = value;

                }
            }


        }

        public void SetHoldingRegisterValue(int address, float value)
        {
            FloatToHEX(value, out short high, out short low);
            holdingRegisters[address + 1] = low;
            holdingRegisters[address] = high;
        }

        private static void FloatToHEX(float value, out short high, out short low)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            high = BitConverter.ToInt16(bytes, 2);
            low = BitConverter.ToInt16(bytes, 0);
        }
    }

    public class ModbusProtocol
    {
        public enum ProtocolType { ModbusTCP = 0, ModbusUDP = 1, ModbusRTU = 2 };
        public DateTime timeStamp;
        public bool request;
        public bool response;
        public ushort transactionIdentifier;
        public ushort protocolIdentifier;
        public ushort length;
        public byte unitIdentifier;
        public byte functionCode;
        public ushort startingAdress;
        public ushort startingAddressRead;
        public ushort startingAddressWrite;
        public ushort quantity;
        public ushort quantityRead;
        public ushort quantityWrite;
        public byte byteCount;
        public byte exceptionCode;
        public byte errorCode;
        public ushort[] receiveCoilValues;
        public ushort[] receiveRegisterValues;
        public ushort[] sendRegisterValues;
        public bool[] sendCoilValues;
        public ushort crc;
    }

    public class TCPHandler
    {
        public delegate void DataChanged(object networkConnectionParameter);
        public event DataChanged dataChanged;
        public delegate void NumberOfClientsChanged();
        public event NumberOfClientsChanged numberOfClientsChanged;
        TcpListener server = null;
        private List<Client> tcpClientLastRequestList = new List<Client>();
        public int NumberOfConnectedClients { get; set; }
        public string ipAddress = null;
        public TCPHandler(int port)
        {
            IPAddress localAddr = IPAddress.Any;
            server = new TcpListener(localAddr, port);
            server.Start();
            server.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
        }
        private void AcceptTcpClientCallback(IAsyncResult asyncResult)
        {
            TcpClient tcpClient = new TcpClient();
            try
            {
                tcpClient = server.EndAcceptTcpClient(asyncResult);
                tcpClient.ReceiveTimeout = 4000;
                if (ipAddress != null)
                {
                    string ipEndpoint = tcpClient.Client.RemoteEndPoint.ToString();
                    ipEndpoint = ipEndpoint.Split(':')[0];
                    if (ipEndpoint != ipAddress)
                    {
                        tcpClient.Client.Disconnect(false);
                        return;
                    }
                }
            }
            catch (Exception) { }
            try
            {
                server.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
                Client client = new Client(tcpClient);
                NetworkStream networkStream = client.NetworkStream;
                networkStream.ReadTimeout = 4000;
                networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);
            }
            catch (Exception) { }
        }
        private int GetAndCleanNumberOfConnectedClients(Client client)
        {
            lock (this)
            {
                int i = 0;
                bool objetExists = false;
                foreach (Client clientLoop in tcpClientLastRequestList)
                {
                    if (client.Equals(clientLoop))
                        objetExists = true;
                }
                try
                {
                    tcpClientLastRequestList.RemoveAll(delegate (Client c)
                    {
                        return ((DateTime.Now.Ticks - c.Ticks) > 40000000);
                    }

                        );
                }
                catch (Exception) { }
                if (!objetExists)
                    tcpClientLastRequestList.Add(client);


                return tcpClientLastRequestList.Count;
            }
        }
        private void ReadCallback(IAsyncResult asyncResult)
        {
            NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
            Client client = asyncResult.AsyncState as Client;
            client.Ticks = DateTime.Now.Ticks;
            NumberOfConnectedClients = GetAndCleanNumberOfConnectedClients(client);
            numberOfClientsChanged?.Invoke();
            if (client != null)
            {
                int read;
                NetworkStream networkStream = null;
                try
                {
                    networkStream = client.NetworkStream;

                    read = networkStream.EndRead(asyncResult);
                }
                catch
                {
                    return;
                }


                if (read == 0)
                {
                    //OnClientDisconnected(client.TcpClient);
                    //connectedClients.Remove(client);
                    return;
                }
                byte[] data = new byte[read];
                Buffer.BlockCopy(client.Buffer, 0, data, 0, read);
                networkConnectionParameter.bytes = data;
                networkConnectionParameter.stream = networkStream;
                dataChanged?.Invoke(networkConnectionParameter);
                try
                {
                    networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);
                }
                catch (Exception)
                {
                }
            }
        }
        public void Disconnect()
        {
            try
            {
                foreach (Client clientLoop in tcpClientLastRequestList)
                {
                    clientLoop.NetworkStream.Close(00);
                }
            }
            catch (Exception) { }
            server.Stop();

        }
        internal class Client
        {
            public long Ticks { get; set; }
            public Client(TcpClient tcpClient)
            {
                this.TcpClient = tcpClient;
                int bufferSize = tcpClient.ReceiveBufferSize;
                Buffer = new byte[bufferSize];
            }
            public TcpClient TcpClient { get; }
            public byte[] Buffer { get; }
            public NetworkStream NetworkStream
            {
                get
                {
                    return TcpClient.GetStream();
                }
            }
        }
    }
}
