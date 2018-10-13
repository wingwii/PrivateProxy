using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Net;

partial class ProxyServer
{
    public partial class Client
    {
        private void StartSocks5Proxy()
        {
            this.mProxyTypeName = "Socks5";
            this.mBuffer1 = new byte[300];
            SocketRecv(this.mSock1, this.mBuffer1, 0, 1, true, this.RecvSocks5MethodCountCompleted, null);
        }

        private void RecvSocks5MethodCountCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            int methodCount = (int)this.mBuffer1[0];
            SocketRecv(this.mSock1, this.mBuffer1, 0, methodCount, true, this.RecvSocks5MethodListCompleted, null);
        }

        private void RecvSocks5MethodListCompleted(SocketOperationResult result)
        {
            int nMethod = result.actualSize;
            if (nMethod != result.size)
            {
                this.Stop();
                return;
            }

            bool methodAccepted = false;
            for (int i = 0; i < nMethod; ++i)
            {
                if (0 == this.mBuffer1[i])
                {
                    methodAccepted = true;
                    break;
                }
                //
            }

            if (!methodAccepted)
            {
                this.Stop();
                return;
            }

            this.mBuffer1[0] = 5;
            this.mBuffer1[1] = 0;
            SocketSend(this.mSock1, this.mBuffer1, 0, 2, this.SendSocks5MethodAcceptCompleted, null);
        }

        private void SendSocks5MethodAcceptCompleted(SocketOperationResult result)
        {
            SocketRecv(this.mSock1, this.mBuffer1, 0, 5, true, this.RecvSocks5RequestCompleted, null);
        }

        private void RecvSocks5RequestCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            if (1 != this.mBuffer1[1])
            {
                this.Stop();
                return;
            }

            int addrLen = 0;
            int addrType = (int)this.mBuffer1[3];
            if (1 == addrType)
            {
                addrLen = 3;
            }
            else if (3 == addrType)
            {
                addrLen = (int)this.mBuffer1[4];
            }
            else if (4 == addrType)
            {
                addrLen = 15;
            }

            if (addrLen <= 0)
            {
                this.Stop();
                return;
            }

            addrLen += 2;
            SocketRecv(this.mSock1, this.mBuffer1, 5, addrLen, true, this.RecvSocks5DstAddrDataCompleted, null);
        }

        private void RecvSocks5DstAddrDataCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            int offset = 4;
            int addrType = (int)this.mBuffer1[3];
            if (1 == addrType || 4 == addrType)
            {
                byte[] addrBytes = null;
                if (1 == addrType)
                {
                    addrBytes = new byte[4];
                }
                else
                {
                    addrBytes = new byte[16];
                }
                
                Array.Copy(this.mBuffer1, offset, addrBytes, 0, addrBytes.Length);
                offset += addrBytes.Length;

                IPAddress addr = new IPAddress(addrBytes);
                this.mProxyDstHost = addr.ToString();
            }
            else if (3 == addrType)
            {
                int addrLen = (int)this.mBuffer1[4];
                this.mProxyDstHost = Encoding.ASCII.GetString(this.mBuffer1, 5, addrLen);
                offset += (addrLen + 1);
            }

            this.mProxyDstPort = (int)this.mBuffer1[offset++];
            this.mProxyDstPort <<= 8;
            this.mProxyDstPort += (int)this.mBuffer1[offset];

            for (int i = 0; i < 10; ++i)
            {
                this.mBuffer1[i] = 0;
            }
            this.mBuffer1[0] = 5;
            this.mBuffer1[3] = 1;

            SocketSend(this.mSock1, this.mBuffer1, 0, 10, this.SendSocks5RequestAcceptCompleted, null);
        }

        private void SendSocks5RequestAcceptCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            if (!this.PreprocessProxyRemoteConnection())
            {
                return;
            }

            this.StartProxy();
        }

        //
    }
}