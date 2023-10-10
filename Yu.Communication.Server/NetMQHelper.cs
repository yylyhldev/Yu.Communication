using NetMQ;

namespace Yu.Communication.Server
{
    public class NetMQHelper
    {
        #region Request-Response
        public static void Response(Action<string> func, string address = "tcp://*:5556", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.ResponseSocket();
            socket.Bind(address);
            Console.WriteLine("NetMQ-Request/Response-Bind");
            while (!cts.IsCancellationRequested)
            {
                var msg = socket.ReceiveFrameString();
                func($"收到：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
                func($"回复：{msg}\n");
                socket.SendFrame($"回复：Response-{DateTime.Now}");
            }
        }
        public static void Request(Action<string> func, string address = "tcp://127.0.0.1:5556", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.RequestSocket();
            socket.Connect(address);
            Console.WriteLine("NetMQ-Request/Response-Connect");
            while (!cts.IsCancellationRequested)
            {
                var msg = $"消息-Request：{DateTime.Now.Ticks}";
                func($"发送：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
                socket.SendFrame(msg);
                msg = socket.ReceiveFrameString();
                func($"收到回复：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
                Thread.Sleep(2000);
            }
        }
        #endregion

        #region Pull-Push
        public static void Pull(Action<string> func, string address = "tcp://127.0.0.1:5557", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.PullSocket();
            socket.Bind(address);
            Console.WriteLine("NetMQ-Pull/Push-Bind");
            while (!cts.IsCancellationRequested)
            {
                var msg = socket.ReceiveFrameString();
                func($"收到：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
            }
        }
        public static void Push(Action<string> func, string address = "tcp://127.0.0.1:5557", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.PushSocket();
            socket.Connect(address);
            Console.WriteLine("NetMQ-Pull/Push-Connect");
            while (!cts.IsCancellationRequested)
            {
                var msg = $"消息-Push：{DateTime.Now.Ticks}";
                func($"发送：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
                socket.SendFrame(msg);
                Thread.Sleep(2000);
            }
        }
        #endregion

        #region Publish-Subscribe
        public static void Publish(Action<string> func, string address = "tcp://*:5558", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.PublisherSocket();
            socket.Bind(address);
            Console.WriteLine("NetMQ-Publish/Subscribe-Bind");
            while (!cts.IsCancellationRequested)
            {
                var msg = $"消息-Publish：{DateTime.Now.Ticks}";
                func($"发送：{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
                socket.SendMoreFrame("PubSub").SendFrame(msg);
                Thread.Sleep(2000);
            }
        }
        public static void Subscribe(Action<string> func, string address = "tcp://127.0.0.1:5558", CancellationToken cts = default)
        {
            using var socket = new NetMQ.Sockets.SubscriberSocket();
            socket.Connect(address);
            //socket.SubscribeToAnyTopic();
            socket.Subscribe("PubSub");
            Console.WriteLine("NetMQ-Publish/Subscribe-Connect");
            while (!cts.IsCancellationRequested)
            {
                var topic = socket.ReceiveFrameString();
                var msg = socket.ReceiveFrameString();
                func($"收到：{topic}__{msg}---{DateTime.Now:HH:mm:ss.fff}\n");
            }
        }
        #endregion
    }
}
