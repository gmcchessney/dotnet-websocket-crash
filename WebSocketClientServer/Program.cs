using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;

namespace WebSocketClientServer
{
    public class Program
    {
        private const int SERVER_PORT = 12345;

        private static readonly AutoResetEvent _clientResetEvent;
        private static int _consecutiveCancellationCount = 0;
        private static int _currentCancellationMs = 100;
        private static int _sendCharCount = 10_000_000;

        static Program()
        {
            _clientResetEvent = new AutoResetEvent(initialState: true);
        }

        public static async Task Main(string[] args)
        {
            SetupUnhandledExceptionHandler();

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.UseWebSockets();

            app.Use(async (context, next) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    Console.WriteLine($"CURRENT CANCELLATION MS: {_currentCancellationMs}");

                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    RunServer(webSocket);

                    // Wait a bit so that the web socket send is in
                    // progress when dispose is called.
                    await Task.Delay(50);

                    webSocket.Dispose();
                    _clientResetEvent.Set();
                }
                else
                {
                    await next();
                }
            });

            var runAsyncTask = app.RunAsync();

            // Give the server some time to start.
            await Task.Delay(1_000);

            var runClientTask = RunClientAsync();

            await Task.WhenAny(runAsyncTask, runClientTask);
        }

        private static async Task RunClientAsync()
        {
            while (true)
            {
                _clientResetEvent.WaitOne();

                try
                {
                    using ClientWebSocket webSocket = new();
                    Uri serverUri = new($"wss://localhost:{SERVER_PORT}");

                    await webSocket.ConnectAsync(serverUri, CancellationToken.None);

                    while (true)
                    {
                        WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), CancellationToken.None);

                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CLIENT: {ex.Message}");
                }
            }
        }

        private static void RunServer(WebSocket webSocket)
        {
            string message = new('A', _sendCharCount += 100_000);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            try
            {
                _ = Task.Run(async () =>
                {
                    CancellationTokenSource cts = new(Math.Max(_currentCancellationMs, 0));

                    try
                    {
                        await webSocket.SendAsync(
                            messageBytes,
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            cts.Token);

                        _currentCancellationMs--;
                        _consecutiveCancellationCount = 0;
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                        {
                            _consecutiveCancellationCount++;
                            _currentCancellationMs += _consecutiveCancellationCount / 2;
                        }
                        else
                        {
                            _currentCancellationMs--;
                            _consecutiveCancellationCount = 0;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SERVER: {ex.Message}");
            }
        }

        private static void SetupUnhandledExceptionHandler()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
            {
                Exception ex = (Exception)args.ExceptionObject;
                Console.WriteLine($"Unhandled exception occurred {ex}");

                ExceptionDispatchInfo.Capture(ex);
                ExceptionDispatchInfo.Throw(ex);
            }
        }
    }
}