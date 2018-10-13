using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

partial class PrivateProxyServer
{
    public const string ProxyServerVersion = "3.0";
    public const string PasswordSALT1 = "2goEifVdxV";
    public const string PasswordSALT2 = "M40178o746";
    public const string DateTimeSaltFormat = "yyyyMMddHHmmss";


    private class ProxyInfo
    {
        public string realIP = string.Empty;
        public string proxyType = string.Empty;
    }

    private class SessionInfo
    {
        public DateTime lastUpdatedTime = DateTime.Now;
        public int aliveTimeInMinute = 0;
    }


    private Dictionary<string, ProxyInfo> mDicProxyInfoReverseLookup = new Dictionary<string, ProxyInfo>(StringComparer.Ordinal);
    private static Dictionary<string, SessionInfo> gDicIpSession = new Dictionary<string, SessionInfo>();
    private ProxyServer mServer = null;
    private Socket mListenSock = null;
    private string mPasswd = string.Empty;
    private int mLocalAuthServerPort = 0;
    private string mSpecialProxyAuthHost = string.Empty;
    private int mSpecialProxyAuthPort = 0;


    static PrivateProxyServer()
    {
        Thread thread = new Thread(ThreadCleanupExpiredSession, 0xffff);
        thread.IsBackground = true;
        thread.Start();
    }

    public PrivateProxyServer(string bindName, string specialProxyAuthHost, string password)
    {
        this.mSpecialProxyAuthHost = specialProxyAuthHost;
        this.mPasswd = password;
        
        this.mServer = new ProxyServer(bindName);
        this.mServer.ProxyClientConnectionFilter = this.ProxyClientConnectionFilter;
        this.mServer.ProxyRemoteConnectionFilter = this.ProxyRemoteConnectionFilter;
    }

    private static void ThreadCleanupExpiredSession()
    {
        int tick = 0;
        while (true)
        {
            Thread.Sleep(1000);
            ++tick;            
            if (tick > 60)
            {
                tick = 0;
                CleanupExpiredSession();
            }
            //
        }
        //
    }

    private static void CleanupExpiredSession()
    {
        DateTime now = DateTime.Now;
        List<string> lstExpiredIP = new List<string>();
        lock (gDicIpSession)
        {
            foreach (var kv in gDicIpSession)
            {
                SessionInfo session = kv.Value;
                if (session != null)
                {
                    TimeSpan dt = now - session.lastUpdatedTime;
                    if ((int)dt.TotalMinutes > session.aliveTimeInMinute)
                    {
                        lstExpiredIP.Add(kv.Key);
                    }
                    //
                }
                //
            }

            foreach (string ip in lstExpiredIP)
            {
                gDicIpSession.Remove(ip);
            }
            //
        }
        //
    }

    public void Start()
    {
        this.ParseSpecialProxyAuthHost();

        this.mListenSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        this.mListenSock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        this.mListenSock.Listen(8);

        this.mLocalAuthServerPort = (this.mListenSock.LocalEndPoint as IPEndPoint).Port;
        this.AcceptNext();

        this.mServer.Start();
    }

    private void ParseSpecialProxyAuthHost()
    {
        int pos = this.mSpecialProxyAuthHost.IndexOf(':');
        if (pos < 0)
        {
            this.mSpecialProxyAuthPort = -1;
        }
        else
        {
            string portStr = this.mSpecialProxyAuthHost.Substring(pos + 1);
            this.mSpecialProxyAuthHost = this.mSpecialProxyAuthHost.Substring(0, pos);
            if (!int.TryParse(portStr, out this.mSpecialProxyAuthPort))
            {
                this.mSpecialProxyAuthPort = -1;
            }
            //
        }

        if (this.mSpecialProxyAuthPort < 0 || this.mSpecialProxyAuthPort > 65535)
        {
            this.mSpecialProxyAuthPort = 8000;
        }
        //
    }

    private void AcceptNext()
    {
        try
        {
            this.mListenSock.BeginAccept(this.OnAccept, null);
        }
        catch (Exception)
        {
            //
        }
    }

    private void OnAccept(IAsyncResult result)
    {
        Socket sock = null;
        try
        {
            sock = this.mListenSock.EndAccept(result);
        }
        catch (Exception)
        {
            //
        }

        this.AcceptNext();

        if (sock != null)
        {
            try
            {
                this.ProcessNewConn(sock);
            }
            catch (Exception)
            {
                //
            }
        }
        //
    }

    private void ProcessNewConn(Socket sock)
    {
        sock.NoDelay = true;
        ProxyServer.Client client = new ProxyServer.Client(sock, true);
        client.HttpRequestHeaderUserHandler = this.ProcessHttpRequestHeader;
        client.HttpRequestDataUserHandler = this.ProcessHttpRequestData;
        client.Start();
    }

    private bool ProxyConnectionPostProcess(ProxyServer.Client client)
    {
        Socket sock = client.ProxyRemoteSocket;
        string localEndPoint = sock.LocalEndPoint.ToString();
        
        ProxyInfo proxy = new ProxyInfo();
        proxy.realIP = client.RemoteAddress;
        proxy.proxyType = client.ProxyTypeName;

        lock (this.mDicProxyInfoReverseLookup)
        {
            this.mDicProxyInfoReverseLookup[localEndPoint] = proxy;
        }
        return true;
    }

    private bool ProcessHttpRequestHeader(ProxyServer.Client client)
    {
        if (client.HttpRequestDataSize > 1024)
        {
            return false;
        }

        string method = client.HttpRequestMethod;
        if (string.IsNullOrEmpty(method))
        {
            return false;
        }

        method = method.ToUpper();
        if (!method.Equals("GET", StringComparison.Ordinal) && !method.Equals("POST", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private bool ProcessHttpRequestData(ProxyServer.Client client)
    {
        StringBuilder sb = new StringBuilder();
        string clientIP = client.RemoteAddress;
        string method = client.HttpRequestMethod.ToUpper();
        string host = client.HttpRequestHost;

        SessionInfo session = null;        
        int currentSessionAliveTimeout = 0;

        if (method.Equals("GET", StringComparison.Ordinal))
        {   
            int pos = host.IndexOf(':');
            if (pos >= 0)
            {
                host = host.Substring(0, pos);
            }
            if (host.Equals(this.mSpecialProxyAuthHost, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(clientIP))
                {
                    lock (gDicIpSession)
                    {
                        if (!gDicIpSession.TryGetValue(clientIP, out session))
                        {
                            session = null;
                        }
                        //
                    }
                    //
                }
                if (session != null)
                {
                    currentSessionAliveTimeout = session.aliveTimeInMinute;
                }
                else
                {
                    if (currentSessionAliveTimeout <= 0)
                    {
                        currentSessionAliveTimeout = 480;
                    }
                    //
                }

                this.PrepareLoginPageHTML(client, sb, currentSessionAliveTimeout);
            }
            else
            {
                this.PrepareWelcomePage(sb);
            }
        }
        else
        {
            byte[] postDataBuf = client.HttpRequestData;
            if (null == postDataBuf)
            {
                return false;
            }

            string postData = Encoding.ASCII.GetString(postDataBuf);
            if (this.ProxyLogin(postData))
            {
                sb.AppendLine(GetJsonStringResponse(0, "Login OK !"));
            }
            else
            {
                sb.AppendLine(GetJsonStringResponse(5, "Login failed"));
            }
            //
        }

        string html = sb.ToString();
        byte[] htmlBuf = Encoding.UTF8.GetBytes(html);
        client.HttpResponseData = htmlBuf;
        return true;
    }

    private string GetJsonStringResponse(int code, string msg)
    {
        return ("{ \"code\":" + code.ToString() + ",\"msg\":\"" + msg + "\" }");
    }

    private static string Byte2HexString(byte[] data)
    {
        StringBuilder sb = new StringBuilder();
        foreach (byte b in data)
        {
            sb.Append(string.Format("{0:X2}", (uint)b));
        }
        return sb.ToString();
    }

    private bool ProxyClientConnectionFilter(ProxyServer.Client client)
    {
        DateTime now = DateTime.Now;

        TimeSpan dt;
        SessionInfo session = null;
        bool sessionOK = false;
        string clientIP = client.RemoteAddress;
        if (!string.IsNullOrEmpty(clientIP))
        {
            lock (gDicIpSession)
            {
                if (!gDicIpSession.TryGetValue(clientIP, out session))
                {
                    session = null;
                }
                if (session != null)
                {
                    dt = now - session.lastUpdatedTime;
                    sessionOK = ((int)dt.TotalMinutes < session.aliveTimeInMinute);
                    if (sessionOK)
                    {
                        session.lastUpdatedTime = now;
                    }
                    else
                    {
                        gDicIpSession.Remove(clientIP);
                    }
                    //
                }
                //
            }
            //
        }

        client.ProxyRemoteConnectionFilter = this.ProxyRemoteConnectionFilter;
        client.ProxySessionTag = sessionOK;
        return true;
    }

    private bool ProxyRemoteConnectionFilter(ProxyServer.Client client)
    {
        int port = client.ProxyDestinationPort;
        string host = client.ProxyDestinationHost;
        bool redirect = false;

        if (port == this.mSpecialProxyAuthPort && host.Equals(this.mSpecialProxyAuthHost, StringComparison.OrdinalIgnoreCase))
        {
            redirect = true;
        }
        else
        {
            bool sessionOK = (bool)client.ProxySessionTag;
            if (!sessionOK)
            {
                if (80 == port)
                {
                    redirect = true;
                }
                else
                {
                    client.ProxyDestinationHost = null;
                }
            }
            //
        }

        if (redirect)
        {
            client.ProxyDestinationHost = IPAddress.Loopback.ToString();
            client.ProxyDestinationPort = this.mLocalAuthServerPort;
            client.ProxyRemoteConnectionPostProcess = this.ProxyConnectionPostProcess;
        }
        return true;
    }

    private bool ProxyLogin(string postData)
    {
        DateTime now = DateTime.Now;

        string salt = string.Empty;
        string hashedPasswd = string.Empty;
        string ipAddr = string.Empty;
        int sessionAliveTime = 0;

        string[] inputs = postData.Split('&');
        foreach (string input in inputs)
        {
            int pos = input.IndexOf('=');
            if (pos < 0)
            {
                continue;
            }

            string name = input.Substring(0, pos).Trim();
            string value = input.Substring(pos + 1).Trim();
            if (name.Equals("PasswdSALT", StringComparison.Ordinal))
            {
                salt = value;
            }
            else if (name.Equals("HashedPasswd", StringComparison.Ordinal))
            {
                hashedPasswd = value.ToLower();
            }
            else if (name.Equals("IpAddress", StringComparison.Ordinal))
            {
                ipAddr = value.ToLower();
            }
            else if (name.Equals("SessionAliveTime", StringComparison.Ordinal))
            {
                if (!int.TryParse(value, out sessionAliveTime))
                {
                    sessionAliveTime = -1;
                }
                //
            }
            //
        }

        if (sessionAliveTime < 0)
        {
            return false;
        }

        DateTime t = DateTime.MinValue;
        if (!DateTime.TryParseExact(salt, DateTimeSaltFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t))
        {
            return false;
        }

        TimeSpan dt = now - t;
        long dtInSec = (long)dt.TotalSeconds;
        if (dtInSec < 0 || dtInSec > 90)
        {
            return false;
        }

        string passwd = PasswordSALT1 + "-" + salt + "-" + this.mPasswd + "-" + PasswordSALT2;
        byte[] passwdBuf = Encoding.ASCII.GetBytes(passwd);

        MD5 hashFunc = MD5.Create();
        byte[] hashBytes = hashFunc.ComputeHash(passwdBuf);

        string hashedPasswd0 = Byte2HexString(hashBytes).ToLower();
        if (!hashedPasswd0.Equals(hashedPasswd, StringComparison.Ordinal))
        {
            return false;
        }

        SessionInfo session = new SessionInfo();
        session.aliveTimeInMinute = sessionAliveTime;
        lock (gDicIpSession)
        {
            gDicIpSession[ipAddr] = session;
        }

        return true;
    }

    //
}



