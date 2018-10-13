using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Socks5Proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Proxy Server";

            string bindAddr = ":9080";
            string proxyAuthHost = "8.8.8.8:80";
            string password = "YourPassword";

            if (args != null)
            {
                if (args.Length > 0)
                {
                    bindAddr = args[0];
                }

                if (args.Length > 1)
                {
                    proxyAuthHost = args[1];
                }

                if (args.Length > 2)
                {
                    password = args[2];
                }
                //
            }

            PrivateProxyServer server = new PrivateProxyServer(bindAddr, proxyAuthHost, password);
            server.Start();

            while (true)
            {
                Thread.Sleep(1000);
            }
            //
        }


        //
    }
}
