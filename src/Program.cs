using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Unicode;

namespace socksy;

internal class Program
{
    public static async Task Main(string[] args)
    {
        const int WSL_RELAY_PORT = 8888;
        const string WINDOWS_SERVICE_URL = "http://localhost:5000/";

        //The WslRelay waits for incoming connection from WindowsRelay
        _ = Task.Run(async () =>
        {

            Console.WriteLine("WslRelay: listens on port " + WSL_RELAY_PORT);

            using var listener = new TcpListener(IPAddress.Loopback, WSL_RELAY_PORT);
            listener.Start();

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();

                using var networkStream = client.GetStream();
                Console.WriteLine("WslRelay: hello_world");
                var bytes = Encoding.UTF8.GetBytes("hello_world\n");
                await networkStream.WriteAsync(bytes);
                await networkStream.ReadExactlyAsync(bytes); //TODO (robin) why dont we get to this line?
                var incoming = Encoding.UTF8.GetString(bytes);
                Console.WriteLine("WslRelay: " + incoming);
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WslRelay: task faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WslRelay: task ended");
            }
        }).ConfigureAwait(false);

        //The WinRelay initiates the connection to the WslRelay (the listener in this code).
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);

            using var wslRelayClient = new TcpClient();
            await wslRelayClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, WSL_RELAY_PORT));

            using var wslStream = wslRelayClient.GetStream();
            using var wslReader = new StreamReader(wslStream);
            using var wslWriter = new StreamWriter(wslStream);

            var winServiceClient = new HttpClient();

            while (true)
            {
                var messageToWinService = await wslReader.ReadLineAsync();
                if (!string.IsNullOrEmpty(messageToWinService))
                {
                    Console.WriteLine("WinRelay: " + messageToWinService);
                    var response = await winServiceClient.GetAsync(WINDOWS_SERVICE_URL + messageToWinService);
                    response.EnsureSuccessStatusCode();
                    
                    var winServiceContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("WinRelay: " + winServiceContent);

                    await wslWriter.WriteAsync(winServiceContent + '\n');
                    await wslWriter.FlushAsync(); //NOTE (robin): without flushing it does not send theh bytes
                    await Task.Delay(1000);
                }
                await Task.Delay(100);
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WinRelay: task faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WinRelay: task ended");
            }
        }).ConfigureAwait(false);

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
}