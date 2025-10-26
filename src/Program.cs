using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace socksy;

internal class Program
{
    const int WSL_RELAY_PORT = 8888;
    const string WINDOWS_SERVICE_URL = "http://localhost:5000/";
    private static Thread? WslRealayThread;
    private static Thread? WinRelayThread;

    public static async Task Main(string[] args)
    {
        RunWslRelayThread();
        RunWinRelayThread();

        //Example http server on windows that wsl want to reach
        _ = Task.Run(async () =>
        {
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add(WINDOWS_SERVICE_URL);
            httpListener.Start();
            Console.WriteLine("WinService: listening on " + WINDOWS_SERVICE_URL);

            while (true)
            {
                var context = await httpListener.GetContextAsync();
                var request = context.Request;
                var message = request?.Url?.AbsoluteUri.Split('/')[^1] ?? "no message";
                Console.WriteLine("WinService: " + message);

                var response = context.Response;

                byte[] buffer = Encoding.UTF8.GetBytes(message);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
        })
        .ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WinService: faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WinService: task ended");
            }
        }).ConfigureAwait(false);

        while (true)
        {
            await Task.Delay(100);
        }
    }

    private static void RunWinRelayThread()
    {
        void WinRelayStart()
        {
            Thread.Sleep(3000);

            using var wslRelayClient = new TcpClient();
            wslRelayClient.Connect(new IPEndPoint(IPAddress.Loopback, WSL_RELAY_PORT));

            using var wslStream = wslRelayClient.GetStream();
            using var wslReader = new StreamReader(wslStream);
            using var wslWriter = new StreamWriter(wslStream);

            var winServiceClient = new HttpClient();

            try
            {
                while (true)
                {
                    var messageToWinService = wslReader.ReadLine();
                    if (!string.IsNullOrEmpty(messageToWinService))
                    {
                        Console.WriteLine("WinRelay: " + messageToWinService);
                        var response = winServiceClient.GetAsync(WINDOWS_SERVICE_URL + messageToWinService).GetAwaiter().GetResult();
                        response.EnsureSuccessStatusCode();

                        var winServiceContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        Console.WriteLine("WinRelay: " + winServiceContent);

                        wslWriter.Write(winServiceContent + '\n');
                        wslWriter.Flush(); //NOTE (robin): without flushing it does not send theh bytes
                        Task.Delay(1000);
                    }
                    Thread.Sleep(30);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("WinRelay: proc faulted");
                Console.WriteLine(ex);
            }
            Console.WriteLine("WinRelay: proc ended");
        }

        WinRelayThread = new Thread(WinRelayStart);
        WinRelayThread.Name = "Win Realay thread";
        WinRelayThread.Start();
    }

    private static void RunWslRelayThread()
    {
        //The WslRelay waits for incoming connection from WindowsRelay

        void WslRelayStart()
        {
            Console.WriteLine("WslRelay: listens on port " + WSL_RELAY_PORT);

            using var listener = new TcpListener(IPAddress.Loopback, WSL_RELAY_PORT);
            listener.Start();

            try
            {
                while (true)
                {
                    var client = listener.AcceptTcpClient();

                    using var networkStream = client.GetStream();
                    Console.WriteLine("WslRelay: hello_world");
                    var bytes = Encoding.UTF8.GetBytes("hello_world\n");
                    networkStream.Write(bytes);
                    networkStream.ReadExactly(bytes); //TODO (robin) why dont we get to this line?
                    var incoming = Encoding.UTF8.GetString(bytes);
                    Console.WriteLine("WslRelay: " + incoming);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("WslRelay: task faulted");
                Console.WriteLine(ex);
            }

            Console.WriteLine("WslRelay: task ended");
        }

        WslRealayThread = new Thread(new ThreadStart(WslRelayStart));

        WslRealayThread.Name = "WslRelay Proc";
        WslRealayThread.Start();
    }
}