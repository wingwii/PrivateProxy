using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;

partial class ProxyServer
{
    public Client.ProxyEventCallback ProxyClientConnectionFilter = null;
    public Client.ProxyEventCallback ProxyRemoteConnectionFilter = null;

    private string mBindName = string.Empty;
    private int mListenBackLog = 8;
    private int mListenPort = 0;
    private IPAddress mListenAddr = IPAddress.Any;
    private Socket mListenSock = null;


    public ProxyServer(string bindName)
    {
        this.mBindName = bindName;
    }

    private static void ThrowException(string msg)
    {
        throw new Exception("[ProxyServer] " + msg);
    }

    private bool ParseBindName()
    {
        int pos = this.mBindName.IndexOf(':');
        if (pos < 0)
        {
            return false;
        }

        string part1 = this.mBindName.Substring(0, pos);
        string part2 = this.mBindName.Substring(pos + 1);

        if (!string.IsNullOrEmpty(part1))
        {
            if (!IPAddress.TryParse(part1, out this.mListenAddr))
            {
                return false;
            }
        }

        if (!int.TryParse(part2, out this.mListenPort))
        {
            this.mListenPort = 0;
        }
        if (this.mListenPort <= 0 || this.mListenPort > 65535)
        {
            return false;
        }

        return true;
    }

    public void Start()
    {
        if (!this.ParseBindName())
        {
            ThrowException("BindName is invalid");
        }

        this.mListenSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        this.mListenSock.Bind(new IPEndPoint(this.mListenAddr, this.mListenPort));
        this.mListenSock.Listen(this.mListenBackLog);

        this.AcceptNext();
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
        //
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

        if (sock != null)
        {
            this.ProcessNewConnectionSafely(sock);
        }

        this.AcceptNext();
    }

    private void ProcessNewConnectionSafely(Socket sock)
    {
        try
        {
            this.ProcessNewConnection(sock);
        }
        catch (Exception)
        {
        }
    }

    private void ProcessNewConnection(Socket sock)
    {
        sock.NoDelay = true;
        Client client = new Client(sock, false);
        client.ProxyClientConnectionFilter = this.ProxyClientConnectionFilter;
        client.Start();
    }

    //
}

