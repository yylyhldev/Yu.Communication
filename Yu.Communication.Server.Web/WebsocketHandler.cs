using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;

namespace Yu.Communication.Server.Web
{
    /// <summary>
    /// Websocket Server
    /// </summary>
    public class WebsocketHandler
    {
        private static ConcurrentDictionary<string, WebSocket> ConnectionDic = new();
        /// <summary>
        /// 当前服务实例需忽略这条订阅消息
        /// </summary>
        private static ConcurrentQueue<string> SkipKickedOffline = new();
        /// <summary>
        /// 当前服务实例Id
        /// </summary>
        private static string ServerId => $"Server{Guid.NewGuid().ToString().Replace("-", string.Empty)}";
        public static string Pattern => "/ws";
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        private readonly ILogger<WebsocketHandler> _logger;
        private readonly RequestDelegate _next;
        private static byte[] BufferSize => new byte[1024];

        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="next"></param>
        public WebsocketHandler(RequestDelegate next, ILogger<WebsocketHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] _rdbs)
        {
            _next = next;
            _logger = logger;
            Configuration = configuration;
            this._rdbs = _rdbs;
            KickedOffline();
        }

        #region 入口
        /// <summary>
        /// Invoke
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context)
        {
            string wsKey = string.Empty;
            WebSocket? socket = null;
            try
            {
                var wsValid = context.WebSockets.IsWebSocketRequest && string.Compare(context.Request.PathBase + string.Empty, Pattern, true) == 0;
                _logger.LogInformation($"WebSocket Server>{DateTime.Now:HH:mm:ss.fff}>begin {wsValid}，{context.Request.Path}，{context.Request.PathBase}");
                if (!wsValid)
                {
                    await _next(context);
                    return;
                }
                #region 握手鉴权时client/server的处理方式
                //子协议方式:
                //server：       HttpContext.WebSockets.AcceptWebSocketAsync("Sec-WebSocket-Protocol");
                //client-js:     socket = new WebSocket("http://localhost:port/ws", ["Sec-WebSocket-Protocol", tokenvalue]);
                //client/Csharp: client.Options.AddSubProtocol("Sec-WebSocket-Protocol");
                //               client.Options.SetRequestHeader("Sec-WebSocket-Protocol", "Bearer " + tokenVal);

                //url参数方式:
                //server：       HttpContext.WebSockets.AcceptWebSocketAsync();
                //client-js:     socket = new WebSocket("http://localhost:port/ws?token=tokenvalue");
                //client-Csharp: client.ConnectAsync(new Uri($"ws://localhost:port/ws?token=tokenvalue"), CancellationToken.None); 
                #endregion

                var subProtocol = "Sec-WebSocket-Protocol";
                var token = context.Request.Headers[subProtocol] + string.Empty;
                if (string.IsNullOrWhiteSpace(token))
                {
                    subProtocol = null;//不能是空字符串
                    token = context.Request.Query["token"] + string.Empty;
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>InvokeAsync 缺少token");
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await _next(context);
                        return;
                    }
                }
                token = token.Replace($"Bearer ", "");
                ///todo:校验token&取出用户数据
                wsKey = token;
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>InvokeAsync token:{token}");
                //socket = await context.WebSockets.AcceptWebSocketAsync();
                socket = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>connected");
                #region 同一个客户端以最后一个连接为准
                //if (ConnectionDic.TryRemove(wsKey, out var oldClient))
                //{
                //    await oldClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "已在别处上线", context.RequestAborted);
                //}
                //ConnectionDic[wsKey] = client;
                WebSocket oldClient = null;
                if (!ConnectionDic.TryAdd(wsKey, socket))
                {
                    oldClient = ConnectionDic[wsKey];
                    ConnectionDic[wsKey] = socket;
                }
                if (oldClient != null)
                {
                    await oldClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "已在别处上线", context.RequestAborted);
                    oldClient.Abort();
                }
                else
                {
                    SkipKickedOffline.Enqueue($"{ServerId}_off_{wsKey}");//当前实例忽略这条订阅消息
                    //await _rdbs[2].SetAsync($"{ServerId}_off_{wsKey}", 0, 3);//当前实例忽略这条订阅消息
                    await _rdbs[2].PublishAsync($"evt_KickedOff", wsKey);
                }
                #endregion
                await socket.SendAsync(Encoding.UTF8.GetBytes("来了老弟"), WebSocketMessageType.Text, true, CancellationToken.None);
                await EchoLoop(socket, wsKey, context.RequestAborted);
            }
            catch when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{wsKey}][{socket?.State}][{socket?.CloseStatus}][{socket?.CloseStatusDescription}]");
                await socket?.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, ex.Message, context.RequestAborted);
                socket?.Abort();
            }
            finally { socket?.Dispose(); }
        } 
        #endregion

        #region 多实例/集群部署时：用Redis发布订阅实现踢下线
        /// <summary>
        /// 订阅频道---多实例/集群部署时：用Redis发布订阅实现踢下线
        /// </summary>
        /// <remarks>1.服务端实例订阅[频道A]；<br/>2.客户端上线时，当前服务实例先做检查，无重复的再向[频道A]发送客户端信息；<br/>3.其他订阅[频道A]的服务实例检查客户端信息，如有则踢下线；</remarks>
        private void KickedOffline()
        {
            _rdbs[2].Subscribe($"evt_KickedOff", async (chan, msg) =>
            {
                var key = msg + string.Empty;
                var has = SkipKickedOffline.TryDequeue(out _);
                //var has = await _rdbs[2].ExistsAsync($"{ServerId}_off_{key}");
                if (!has && ConnectionDic.TryRemove(key, out var oldClient))
                {
                    _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] Client {key}已在别处上线]({ConnectionDic.Count})");
                    await oldClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "已在别处上线",CancellationToken.None);
                    oldClient.Abort();
                }
            });
        }
        #endregion

        #region 循环监听客户端
        private async Task EchoLoop(WebSocket socket, string wsKey, CancellationToken cancellationToken = default)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(BufferSize), cts.Token);
                    }
                    catch when (socket.State == WebSocketState.Aborted)
                    {
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>超时断开了[{socket.State}/{socket.CloseStatus}]");
                        cts.Dispose();//超时or客户端未正常关闭
                        break;
                    }
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>Closing WebSocket...[{socket.State}/{socket.CloseStatus}]");
                        if (socket.State == WebSocketState.CloseReceived) await socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "再见", cancellationToken);
                    }
                    else
                    {
                        var msg = Encoding.ASCII.GetString(BufferSize, 0, result.Count);
                        msg = (msg + "").TrimEnd('\0');
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>Received[{result.Count}]{msg}");
                        await socket.SendAsync(new ArraySegment<byte>(BufferSize, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
                    }
                }
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>结束了[{socket.State}/{socket.CloseStatus}]");
                //todo:关闭后逻辑
            }
            catch when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{DateTime.Now:HH:mm:ss.fff}>WebSocket Server>监听出错[{wsKey}][{socket.State}/{socket.CloseStatus}/{socket.CloseStatusDescription}]");
            }
            finally
            {
                ConnectionDic.TryRemove(wsKey, out _);
                socket.Dispose();
            }
        }
        #endregion

        #region Method
        public static int OnlineCount()
        {
            return ConnectionDic.Count;
        }
        public static string[] Onlines()
        {
            return ConnectionDic.Select(d => d.Key).ToArray();
        }
        public static async Task<bool> Offline(string key, CancellationToken cancellationToken = default)
        {
            if (ConnectionDic.TryRemove(key, out var oneVal))
            {
                await oneVal.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "走你", cancellationToken);
                oneVal.Abort();
                return true;
            }
            return false;
        }
        public static async Task SendMsgAll(string type, string msg, CancellationToken cancellationToken = default)
        {
            var sendData = Encoding.UTF8.GetBytes($"{DateTime.Now:HH:mm:ss.fff} [{type}][{msg}]");
            foreach (var one in ConnectionDic)
            {
                await one.Value.SendAsync(sendData, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        public static async Task<bool> SendMsg(string key, string type, string msg, CancellationToken cancellationToken = default)
        {
            if (ConnectionDic.TryGetValue(key, out var oneVal))
            {
                var sendData = Encoding.UTF8.GetBytes($"{DateTime.Now:HH:mm:ss.fff} [{type}][{msg}]");
                await oneVal.SendAsync(sendData, WebSocketMessageType.Text, true, cancellationToken);
                return true;
            }
            return false;
        }
        #endregion
    }
}
