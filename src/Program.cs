using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace socksy;

internal enum ROLE
{
    Connector,
    Relay
}

internal class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "connector" && args[1] != "relay"))
        {
            Console.WriteLine("socksy connector|relay");
            Environment.Exit(1);
        }

        var state = args[0] == "connector" ? ROLE.Connector : ROLE.Relay;

        if (state == ROLE.Connector)
        {
            var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, 8088);
            var stream = tcpClient.GetStream();

            //var bytes = new byte[8192];
            int b = stream.ReadByte();
            var targetClient = new TcpClient();
            targetClient.Connect(IPAddress.Loopback, 8077); //Python http server on 8077
            var targetSteam = targetClient.GetStream();

            var writeLoop = Task.Run(() =>
            {
                for(int b; (b = stream.ReadByte()) != 0;)
                {
                    targetSteam.WriteByte((byte)b);
                }
            });

            var readLoop = Task.Run(() =>
            {
                for (int b; (b = targetSteam.ReadByte()) != -1;)
                {
                    stream.WriteByte((byte)b);
                }
            });

            var any = await Task.WhenAny(writeLoop, readLoop);

            if (any.IsFaulted)
            {
                throw any.Exception;
            }
        }

        if (state == ROLE.Relay)
        {
            var connectListener = new TcpListener(IPAddress.Loopback, 8088);
            connectListener.Start();

            var forwardListener = new TcpListener(IPAddress.Loopback, 8066);
            forwardListener.Start();

            var connectTask = connectListener.AcceptTcpClientAsync();
            var relayeeTask = forwardListener.AcceptTcpClientAsync();

            var anyFinished = await Task.WhenAny(connectTask, relayeeTask);

            if (anyFinished.IsFaulted)
            {
                throw anyFinished.Exception;
            }

            if (anyFinished == connectTask)
            {
                Console.WriteLine("Connected");
            }

            await Task.WhenAll(connectTask, relayeeTask);

            var connectorClient = await connectTask;
            var relayee = await relayeeTask;

            var forwardWrite = Task.Run(() =>
            {
                for (int b; (b = stream.ReadByte()) != 0;)
                {
                    targetSteam.WriteByte((byte)b);
                }
            });

            var forwardRead = Task.Run(() =>
            {
                for (int b; (b = targetSteam.ReadByte()) != -1;)
                {
                    stream.WriteByte((byte)b);
                }
            });
        }
    }

    //private static void RunWinRelayThread()
    //{
    //    void WinRelayStart()
    //    {
    //        Thread.Sleep(3000);

    //        using var wslRelayClient = new TcpClient();
    //        wslRelayClient.Connect(new IPEndPoint(IPAddress.Loopback, WaiterPort));

    //        using var wslStream = wslRelayClient.GetStream();
    //        using var wslReader = new StreamReader(wslStream);
    //        using var wslWriter = new StreamWriter(wslStream);

    //        var winServiceClient = new HttpClient();

    //        try
    //        {
    //            while (true)
    //            {
    //                var messageToWinService = wslReader.ReadLine();
    //                if (!string.IsNullOrEmpty(messageToWinService))
    //                {
    //                    Console.WriteLine("WinRelay: " + messageToWinService);
    //                    var response = winServiceClient.GetAsync(WINDOWS_SERVICE_URL + messageToWinService).GetAwaiter().GetResult();
    //                    response.EnsureSuccessStatusCode();

    //                    var winServiceContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    //                    Console.WriteLine("WinRelay: " + winServiceContent);

    //                    wslWriter.Write(winServiceContent + '\n');
    //                    wslWriter.Flush(); //NOTE (robin): without flushing it does not send theh bytes
    //                    Task.Delay(1000);
    //                }
    //                Thread.Sleep(30);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine("WinRelay: proc faulted");
    //            Console.WriteLine(ex);
    //        }
    //        Console.WriteLine("WinRelay: proc ended");
    //    }

    //    WinRelayThread = new Thread(WinRelayStart);
    //    WinRelayThread.Name = "Win Realay thread";
    //    WinRelayThread.Start();
    //}

    //private static void RunWslRelayThread()
    //{
    //    //The WslRelay waits for incoming connection from WindowsRelay

    //    void WslRelayStart()
    //    {
    //        Console.WriteLine($"WslRelay: listens on port {WaiterPort}");

    //        using var listener = new TcpListener(IPAddress.Loopback, WaiterPort);
    //        listener.Start();

    //        try
    //        {
    //            while (true)
    //            {
    //                var client = listener.AcceptTcpClient();

    //                using var networkStream = client.GetStream();
    //                Console.WriteLine("WslRelay: hello_world");
    //                var bytes = Encoding.UTF8.GetBytes("hello_world\n");
    //                networkStream.Write(bytes);
    //                networkStream.ReadExactly(bytes); //TODO (robin) why dont we get to this line?
    //                var incoming = Encoding.UTF8.GetString(bytes);
    //                Console.WriteLine("WslRelay: " + incoming);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine("WslRelay: task faulted");
    //            Console.WriteLine(ex);
    //        }

    //        Console.WriteLine("WslRelay: task ended");
    //    }

    //    WslRealayThread = new Thread(new ThreadStart(WslRelayStart));

    //    WslRealayThread.Name = "WslRelay Proc";
    //    WslRealayThread.Start();
    //}
}