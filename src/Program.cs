using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Unicode;

namespace socksy;

internal class Program
{
    public static async Task Main(string[] args)
    {
        const int WSL_PROXY_PORT = 8888;
        const string WINDOWS_SERVICE_URL = "http://localhost:5000/";

        //The Wsl proxy waits for incoming connection from windows client proxy A
        _ = Task.Run(async () =>
        {

            Console.WriteLine("WslProxy: listens on port " + WSL_PROXY_PORT);

            using var listener = new TcpListener(IPAddress.Loopback, WSL_PROXY_PORT);
            listener.Start();

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                using var networkStream = client.GetStream();
                var bytes = Encoding.UTF8.GetBytes("WslProxy_Hello\n");
                await networkStream.WriteAsync(bytes);
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WslProxy: task faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WslProxy: task ended");
            }
        }).ConfigureAwait(false);

        //The windows client proxy initiates the connection to the WslProxy (the listener in this code).
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);

            using var client = new TcpClient();
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, WSL_PROXY_PORT));
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream);
            Console.WriteLine("WindowsClientProxy: Connection established to wsl");

            var httpProxyClient = new HttpClient();

            while (true)
            {
                var text = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    Console.WriteLine("WindowsClientProxy: relaying " + text);
                    var response = await httpProxyClient.GetAsync(WINDOWS_SERVICE_URL + text);
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("WindowsClientProxy: received \"" + content + "\"");

                    await writer.WriteAsync(content);
                }
                await Task.Delay(100);
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WindowsClientProxy: task faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WindowsClientProxy: task ended");
            }
        }).ConfigureAwait(false);

        //Example http server on windows that wsl want to reach
        _ = Task.Run(async () =>
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(WINDOWS_SERVICE_URL);
            listener.Start();
            Console.WriteLine("WindowsService: listening on " + WINDOWS_SERVICE_URL);

            while (true)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                Console.WriteLine("WindowsService got request url: " + request.Url);

                var response = context.Response;

                string responseText = $"WindowsService: hello, you requested {request.Url}";
                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
        })
        .ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Console.WriteLine("WindowsService: faulted");
                Console.WriteLine(task.Exception);
            }
            else
            {
                Console.WriteLine("WindowsService: task ended");
            }
        }).ConfigureAwait(false);

        while (true)
        {
            await Task.Delay(100);
        }
    }
}