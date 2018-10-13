using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;

partial class ProxyServer
{
    public partial class Client
    {
        public int ProxyBufferSize = 2048;

        public delegate bool ProxyEventCallback(Client client);

        private delegate void SocketOperationCallback(SocketOperationResult result);
        private class SocketOperationResult
        {
            public Socket sock = null;
            public bool directionOut = true;
            public byte[] buf = null;
            public int offset = 0;
            public int size = 0;
            public int actualSize = 0;
            public int lastBytes = 0;
            public bool waitAll = false;
            public SocketOperationCallback callback = null;
            public object state = null;
        }

        public ProxyEventCallback ProxyClientConnectionFilter = null;
        public ProxyEventCallback ProxyFirstByteFilter = null;
        public ProxyEventCallback ProxyRemoteConnectionFilter = null;
        public ProxyEventCallback ProxyRemoteConnectionPostProcess = null;

        public object ProxySessionTag = null;

        private string mProxyTypeName = string.Empty;
        private Socket mSock1 = null;
        private Socket mSock2 = null;
        private string mClientIP = string.Empty;
        private string mProxyDstHost = string.Empty;
        private int mProxyFirstByte = 0;
        private int mProxyDstPort = 0;
        private int mFirstBufferLen = 0;
        private int mFirstBufferOffset = 0;
        private byte[] mBuffer1 = null;
        private byte[] mBuffer2 = null;
        private bool mUseHttpOnly = false;


        public Client(Socket sock, bool useHttpOnly)
        {
            this.mUseHttpOnly = useHttpOnly;
            this.mSock1 = sock;

            this.ProxyFirstByteFilter = this.AlwaysAcceptedProxyFilter;
            this.ProxyRemoteConnectionFilter = this.AlwaysAcceptedProxyFilter;
        }

        private static SocketOperationResult PrepareSocketOperationResult(Socket sock, bool directionOut, byte[] buf, int offset, int size, bool waitAll, SocketOperationCallback callback, object state)
        {
            SocketOperationResult result = new SocketOperationResult();
            result.sock = sock;
            result.directionOut = directionOut;
            result.buf = buf;
            result.offset = offset;
            result.size = size;
            result.waitAll = waitAll;
            result.callback = callback;
            result.state = state;
            return result;
        }

        private static void InvokeSocketOperationCallback(SocketOperationResult result)
        {
            SocketOperationCallback callback = result.callback;
            if (null == callback)
            {
                return;
            }

            try
            {
                callback(result);
            }
            catch (Exception)
            {
            }
        }

        private static void SendRecvNext(SocketOperationResult result)
        {
            bool waitAll = result.waitAll;
            int actualSize = result.actualSize;
            int remain = result.size - actualSize;
            if ((waitAll && remain <= 0) || (!waitAll && result.lastBytes > 0))
            {
                InvokeSocketOperationCallback(result);
                return;
            }

            bool ok = false;
            byte[] buf = result.buf;
            int offset = result.offset + actualSize;
            Socket sock = result.sock;
            try
            {
                if (result.directionOut)
                {
                    sock.BeginSend(buf, offset, remain, SocketFlags.None, SendRecvPartialCompleted, result);
                }
                else
                {
                    sock.BeginReceive(buf, offset, remain, SocketFlags.None, SendRecvPartialCompleted, result);
                }
                ok = true;
            }
            catch (Exception)
            {
                //
            }

            if (!ok)
            {
                result.lastBytes = -1;
                InvokeSocketOperationCallback(result);
            }

            //
        }

        private static void SendRecvPartialCompleted(IAsyncResult iar)
        {
            SocketOperationResult result = iar.AsyncState as SocketOperationResult;
            if (null == result)
            {
                return;
            }

            int n = 0;
            Socket sock = result.sock;
            try
            {
                if (result.directionOut)
                {
                    n = sock.EndSend(iar);
                }
                else
                {
                    n = sock.EndReceive(iar);
                }
            }
            catch (Exception)
            {
                //
            }

            result.lastBytes = n;
            if (n <= 0)
            {
                InvokeSocketOperationCallback(result);
                return;
            }

            result.actualSize += n;
            SendRecvNext(result);
        }

        private static void SocketSend(Socket sock, byte[] buf, int offset, int size, SocketOperationCallback callback, object state)
        {
            SocketOperationResult result = PrepareSocketOperationResult(sock, true, buf, offset, size, true, callback, state);
            SendRecvNext(result);
        }

        private static void SocketRecv(Socket sock, byte[] buf, int offset, int size, bool waitAll, SocketOperationCallback callback, object state)
        {
            SocketOperationResult result = PrepareSocketOperationResult(sock, false, buf, offset, size, waitAll, callback, state);
            SendRecvNext(result);
        }

        private static void CloseSocket(Socket sock)
        {
            if (null == sock)
            {
                return;
            }

            try { sock.Close(); }
            catch (Exception) { }
        }

        public void Stop()
        {
            CloseSocket(this.mSock1);
            CloseSocket(this.mSock2);
        }

        public string ProxyTypeName
        {
            get
            {
                return this.mProxyTypeName;
            }
        }
        public string ProxyDestinationHost
        {
            get
            {
                return this.mProxyDstHost;
            }
            set
            {
                this.mProxyDstHost = value;
            }
        }

        public int ProxyDestinationPort
        {
            get
            {
                return this.mProxyDstPort;
            }
            set
            {
                this.mProxyDstPort = value;
            }
        }

        public string RemoteAddress
        {
            get
            {
                return this.mClientIP;
            }
        }

        public Socket ProxyLocalSocket
        {
            get
            {
                return this.mSock1;
            }
        }

        public Socket ProxyRemoteSocket
        {
            get
            {
                return this.mSock2;
            }
        }

        public int ProxyFirstByte
        {
            get
            {
                return this.mProxyFirstByte;
            }
        }

        private bool InvokeProxyEventCallback(ProxyEventCallback callback)
        {
            bool result = false;
            if (callback != null)
            {
                try
                {
                    result = callback(this);
                }
                catch (Exception)
                {
                    //
                }
            }
            return result;
        }

        public void Start()
        {
            try
            {
                this.mClientIP = (this.mSock1.RemoteEndPoint as IPEndPoint).Address.ToString();
            }
            catch (Exception)
            {
                //
            }

            this.InvokeProxyEventCallback(this.ProxyClientConnectionFilter);

            byte[] buf = new byte[16];
            SocketRecv(this.mSock1, buf, 0, 1, true, this.RecvFirstByteCompleted, null);
        }

        private bool AlwaysAcceptedProxyFilter(Client client)
        {
            return true;
        }

        private void RecvFirstByteCompleted(SocketOperationResult result)
        {
            if (result.actualSize != 1)
            {
                this.Stop();
                return;
            }

            byte firstByte = result.buf[0];
            this.mProxyFirstByte = (int)firstByte;

            bool firstByteOK = this.InvokeProxyEventCallback(this.ProxyFirstByteFilter);
            if (firstByteOK)
            {
                if (firstByte > 5)
                {
                    this.StartHttpProxy(firstByte);
                    return;
                }
                else if (5 == firstByte)
                {
                    if (!this.mUseHttpOnly)
                    {
                        this.StartSocks5Proxy();
                        return;
                    }
                }
                //
            }

            this.Stop();
        }

        private bool PreprocessProxyRemoteConnection()
        {
            bool filterOK = this.InvokeProxyEventCallback(this.ProxyRemoteConnectionFilter);
            if (!filterOK || string.IsNullOrEmpty(this.mProxyDstHost) || this.mProxyDstPort < 0 || this.mProxyDstPort > 65535)
            {
                this.Stop();
                return false;
            }
            else
            {
                return true;
            }
        }

        private void StartProxy()
        {
            bool ok = false;
            try
            {
                this.mSock2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this.mSock2.BeginConnect(this.mProxyDstHost, this.mProxyDstPort, this.ProxyConnectDstHostCompleted, null);
                ok = true;
            }
            catch (Exception)
            {
            }

            if (!ok)
            {
                this.Stop();
            }
            //
        }

        private void ProxyConnectDstHostCompleted(IAsyncResult result)
        {
            bool ok = false;
            try
            {
                this.mSock2.EndConnect(result);
                ok = true;
            }
            catch (Exception)
            {
                //
            }

            if (!ok)
            {
                this.Stop();
                return;
            }

            this.InvokeProxyEventCallback(this.ProxyRemoteConnectionPostProcess);

            this.ForwardClientFirstData();
        }

        private void ForwardClientFirstData()
        {
            int firstBufferRemain = this.mFirstBufferLen - this.mFirstBufferOffset;
            if (firstBufferRemain <= 0)
            {
                this.StartProxyTransfer();
            }
            else
            {
                SocketSend(this.mSock2, this.mBuffer1, this.mFirstBufferOffset, firstBufferRemain, this.ForwardClientFirstDataCompleted, null);
            }
        }

        private void ForwardClientFirstDataCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }
            this.StartProxyTransfer();
        }

        private void StartProxyTransfer()
        {
            this.mBuffer1 = new byte[ProxyBufferSize];
            this.mBuffer2 = new byte[ProxyBufferSize];

            this.PairedSocketTransfer(true);
            this.PairedSocketTransfer(false);
        }

        private void PairedSocketTransfer(bool direction1To2)
        {
            byte[] buf = this.mBuffer1;
            Socket sock1 = this.mSock1;
            Socket sock2 = this.mSock2;
            if (!direction1To2)
            {
                buf = this.mBuffer2;
                sock1 = this.mSock2;
                sock2 = this.mSock1;
            }

            SocketRecv(sock1, buf, 0, buf.Length, false, this.PairedSockRecvCompleted, direction1To2);
        }

        private void PairedSockRecvCompleted(SocketOperationResult result)
        {
            int n = result.actualSize;
            if (n <= 0)
            {
                this.PendingStop();
                return;
            }

            bool direction1To2 = (bool)result.state;
            byte[] buf = result.buf;
            Socket sock = this.mSock2;
            if (!direction1To2)
            {
                sock = this.mSock1;
            }

            SocketSend(sock, buf, 0, n, this.PairedSockFwdCompleted, direction1To2);
        }

        private void PairedSockFwdCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.PendingStop();
                return;
            }

            bool direction1To2 = (bool)result.state;
            this.PairedSocketTransfer(direction1To2);
        }

        private static void ShutdownSocket(Socket sock)
        {
            if (sock != null)
            {
                try { sock.Shutdown(SocketShutdown.Both); }
                catch (Exception) { }
            }
            //
        }

        private void PendingStop()
        {
            ShutdownSocket(this.mSock1);
            ShutdownSocket(this.mSock2);

#if _USE_DELAY_STOP           
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = 5000;
            timer.Elapsed += new System.Timers.ElapsedEventHandler(this.TimerWaitForClientRecv_Elapsed);
            timer.Enabled = true;
            timer.Start();
#else       
            this.Stop();     
#endif            
            //
        }

        private void TimerWaitForClientRecv_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this.Stop();
            System.Timers.Timer timer = sender as System.Timers.Timer;
            if (timer != null)
            {
                timer.Enabled = false;
                timer.Stop();
                timer.Dispose();
            }
            //
        }


        //


        //
    }
    //
}
