using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;

partial class ProxyServer
{
    public partial class Client
    {
        public int HttpHeaderMaxLength = 1024;


        public ProxyEventCallback HttpRequestFirstLineFilter = null;
        public ProxyEventCallback HttpRequestHeaderUserHandler = null;
        public ProxyEventCallback HttpRequestDataUserHandler = null;


        private string[] mRawHttpHdrLines = null;
        private string mHttpRequestHdrFirstLine = null;
        private string mHttpRequestMethod = string.Empty;
        private string mRawHttpRequestHdr = string.Empty;
        private string mHttpRequestPath = string.Empty;
        private string mHttpRequestVersion = string.Empty;
        private string mHttpRequestHost = string.Empty;
        private int mHttpRequestDataSize = 0;
        private byte[] mHttpRequestData = null;
        private byte[] mHttpResponseData = null;


        private void StartHttpProxy(byte firstByte)
        {
            this.mProxyTypeName = "HTTPS";
            this.mBuffer1 = new byte[HttpHeaderMaxLength];
            this.mRawHttpRequestHdr = string.Empty + (char)firstByte;
            this.RecvHttpHdrNext();
        }

        public bool IsHttpOnly
        {
            get
            {
                return this.mUseHttpOnly;
            }
        }

        public string HttpRequestHost
        {
            get
            {
                return this.mHttpRequestHost;
            }
        }

        public string HttpRequestMethod
        {
            get
            {
                return this.mHttpRequestMethod;
            }
        }

        public string HttpRequestPath
        {
            get
            {
                return this.mHttpRequestPath;
            }
        }

        public string HttpRequestVersion
        {
            get
            {
                return this.mHttpRequestVersion;
            }
        }

        public int HttpRequestDataSize
        {
            get
            {
                return this.mHttpRequestDataSize;
            }
        }

        public byte[] HttpRequestData
        {
            get
            {
                return this.mHttpRequestData;
            }
        }

        public byte[] HttpResponseData
        {
            get
            {
                return this.mHttpResponseData;
            }
            set
            {
                this.mHttpResponseData = value;
            }
        }

        private void RecvHttpHdrNext()
        {
            SocketRecv(this.mSock1, this.mBuffer1, 0, this.mBuffer1.Length, false, this.RecvPartialHttpHdrCompleted, null);
        }

        private void RecvPartialHttpHdrCompleted(SocketOperationResult result)
        {
            int n = result.actualSize;
            if (n <= 0)
            {
                this.Stop();
                return;
            }

            bool httpHdrEnd = false;
            for (int i = 0; i < n; ++i)
            {
                this.mRawHttpRequestHdr += (char)this.mBuffer1[i];
                if (this.mRawHttpRequestHdr.EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    this.mFirstBufferLen = n;
                    this.mFirstBufferOffset = i + 1;
                    httpHdrEnd = true;
                    break;
                }
            }

            if (null == this.mHttpRequestHdrFirstLine)
            {
                this.ProcessHttpRequestHeaderFirstLine();
            }

            if (httpHdrEnd)
            {
                this.ProcessHttpRequestHeader();
            }
            else
            {
                if (this.mRawHttpRequestHdr.Length < HttpHeaderMaxLength)
                {
                    this.RecvHttpHdrNext();
                }
                else
                {
                    this.Stop();
                }
            }
            //
        }

        private string GetFirstHttpRequestHeaderValue(string name)
        {
            int pos = this.mRawHttpRequestHdr.IndexOf("\r\n" + name + ":", 0, StringComparison.OrdinalIgnoreCase);
            if (pos > 0)
            {
                pos += (name.Length + 3);
                int pos2 = this.mRawHttpRequestHdr.IndexOf("\r\n", pos, StringComparison.Ordinal);
                if (pos2 < 0)
                {
                    return string.Empty;
                }
                return this.mRawHttpRequestHdr.Substring(pos, pos2 - pos).Trim();
            }
            return string.Empty;
        }

        private bool ParseHttpRequestHeader()
        {
            string[] lines = this.mRawHttpRequestHdr.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length < 3)
            {
                return false;
            }
            this.mRawHttpHdrLines = lines;

            string value = string.Empty;
            this.mHttpRequestHost = this.GetFirstHttpRequestHeaderValue("Host");

            this.mHttpRequestDataSize = 0;
            value = this.GetFirstHttpRequestHeaderValue("Content-Length");
            if (!string.IsNullOrEmpty(value))
            {
                if (!int.TryParse(value, out this.mHttpRequestDataSize))
                {
                    this.mHttpRequestDataSize = 0;
                }
            }
            if (this.mHttpRequestDataSize < 0)
            {
                this.mHttpRequestDataSize = 0;
            }

            return true;
        }

        private bool ParseConnInfo(string conn, out string host, out int port)
        {
            port = 0;
            host = string.Empty;
            int pos = conn.IndexOf(':');
            if (pos < 0)
            {
                host = conn;
                port = 80;
            }
            else
            {
                host = conn.Substring(0, pos);
                string portStr = conn.Substring(pos + 1);
                if (!int.TryParse(portStr, out port))
                {
                    port = -1;
                }
                if (port < 0 || port > 65535)
                {
                    return false;
                }
                //
            }
            return true;
        }

        private void ProcessPlainHttpProxyRequest()
        {
            this.mProxyTypeName = "HTTP";

            string host = this.mHttpRequestHost;
            this.mHttpRequestPath = this.mHttpRequestPath.Substring(7);
            int pos = this.mHttpRequestPath.IndexOf('/');
            if (pos < 0)
            {
                this.mProxyDstHost = this.mHttpRequestPath;
                this.mHttpRequestPath = "/";
            }
            else
            {
                this.mProxyDstHost = this.mHttpRequestPath.Substring(0, pos);
                this.mHttpRequestPath = this.mHttpRequestPath.Substring(pos);
            }

            host = this.mProxyDstHost;
            this.mProxyDstPort = 80;

            if (!ParseConnInfo(this.mProxyDstHost, out this.mProxyDstHost, out this.mProxyDstPort))
            {
                this.Stop();
                return;
            }

            if (!this.mUseHttpOnly)
            {
                if (!this.PreprocessProxyRemoteConnection())
                {
                    return;
                }
                //
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(this.mHttpRequestMethod.ToUpper());
            sb.Append(' ');
            sb.Append(this.mHttpRequestPath);
            sb.Append(' ');
            sb.Append(this.mHttpRequestVersion);
            sb.Append("\r\n");
            sb.Append("Host: ");
            sb.Append(host);
            sb.Append("\r\n");

            int n = this.mRawHttpHdrLines.Length - 1;
            for (int i = 1; i < n; ++i)
            {
                string line = this.mRawHttpHdrLines[i];
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }
                if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                sb.Append(line);
                sb.Append("\r\n");
            }            
            sb.Append("\r\n");

            string newHttpRequestHdr = sb.ToString();
            byte[] newHttpRequestHdrBytes = Encoding.ASCII.GetBytes(newHttpRequestHdr);

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(newHttpRequestHdrBytes, 0, newHttpRequestHdrBytes.Length);
                ms.Write(this.mBuffer1, this.mFirstBufferOffset, this.mFirstBufferLen - this.mFirstBufferOffset);
                ms.Close();
                this.mBuffer1 = ms.ToArray();
                this.mFirstBufferLen = this.mBuffer1.Length;
                this.mFirstBufferOffset = 0;
            }

            this.StartProxy();
        }

        private void ProcessHttpConnectMethod()
        {
            if (!ParseConnInfo(this.mHttpRequestPath, out this.mProxyDstHost, out this.mProxyDstPort))
            {
                this.Stop();
                return;
            }

            if (!this.PreprocessProxyRemoteConnection())
            {
                return;
            }

            string hdr = this.mHttpRequestVersion + " 200 OK\r\n\r\n";
            byte[] buf = Encoding.ASCII.GetBytes(hdr);
            SocketSend(this.mSock1, buf, 0, buf.Length, this.SendHttpConnectMethodAcceptCompleted, null);
        }

        private void SendHttpConnectMethodAcceptCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            this.StartProxy();
        }

        private void ProcessHttpRequestHeaderFirstLine()
        {
            int pos = this.mRawHttpRequestHdr.IndexOf("\r\n", StringComparison.Ordinal);
            if (pos < 0)
            {
                return;
            }

            this.mHttpRequestHdrFirstLine = this.mRawHttpRequestHdr.Substring(0, pos);
            string[] requestInfo = this.mHttpRequestHdrFirstLine.Split(' ');
            if (3 == requestInfo.Length)
            {
                this.mHttpRequestMethod = requestInfo[0].ToUpper();
                this.mHttpRequestPath = requestInfo[1];
                this.mHttpRequestVersion = requestInfo[2];           
            }

            this.InvokeProxyEventCallback(this.HttpRequestFirstLineFilter);
        }

        private void ProcessHttpRequestHeader()
        {
            if (!this.ParseHttpRequestHeader())
            {
                this.Stop();
                return;
            }

            bool rejectClientRequest = false;
            if (this.mUseHttpOnly)
            {
                rejectClientRequest = !this.InvokeProxyEventCallback(this.HttpRequestHeaderUserHandler);
                if (rejectClientRequest)
                {
                    this.Stop();
                }
                else
                {
                    this.RecvHttpRequestData();
                }
                return;
            }

            if (this.mHttpRequestMethod.Equals("CONNECT", StringComparison.Ordinal))
            {
                this.ProcessHttpConnectMethod();
            }
            else
            {
                if (this.mHttpRequestPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    this.ProcessPlainHttpProxyRequest();
                }
                else
                {
                    rejectClientRequest = true;
                }
            }

            if (rejectClientRequest)
            {
                this.Stop();
            }
            //
        }

        private void RecvHttpRequestData()
        {
            byte[] buf = new byte[this.mHttpRequestDataSize];
            int firstBufferRemain = this.mFirstBufferLen - this.mFirstBufferOffset;
            Array.Copy(this.mBuffer1, this.mFirstBufferOffset, buf, 0, firstBufferRemain);

            this.mHttpRequestData = buf;
            SocketRecv(this.mSock1, buf, firstBufferRemain, buf.Length - firstBufferRemain, true, this.RecvHttpRequestDataCompleted, null);
        }

        private void RecvHttpRequestDataCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            this.mHttpResponseData = null;
            if (!this.InvokeProxyEventCallback(this.HttpRequestDataUserHandler))
            {
                this.Stop();
                return;
            }

            if (null == this.mHttpResponseData || 0 == this.mHttpResponseData.Length)
            {
                this.Stop();
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(this.mHttpRequestVersion);
            sb.Append(" 200 OK\r\n");
            sb.Append("Server: nginx\r\n");
            sb.Append("Content-Length: ");
            sb.Append(this.mHttpResponseData.Length.ToString());
            sb.Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            string hdr = sb.ToString();
            byte[] buf = Encoding.ASCII.GetBytes(hdr);
            SocketSend(this.mSock1, buf, 0, buf.Length, this.SendHttpResponseHeaderCompleted, null);
        }

        private void SendHttpResponseHeaderCompleted(SocketOperationResult result)
        {
            if (result.actualSize != result.size)
            {
                this.Stop();
                return;
            }

            byte[] buf = this.mHttpResponseData;
            SocketSend(this.mSock1, buf, 0, buf.Length, this.SendHttpResponseDataCompleted, null);
        }

        private void SendHttpResponseDataCompleted(SocketOperationResult result)
        {
            try
            {
                this.mSock1.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                //
            }
            this.PendingStop();
        }

        //
    }
}

