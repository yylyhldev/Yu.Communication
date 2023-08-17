using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Yu.Communication.Server.Web
{
    #region SingalR Authorization Middleware
    /// <summary>
    /// 非jwt方式鉴权，需与 builder.Services.AddJwtConfigure 二选一 <br/>
    /// 此方式SingalRHandler不可启用[Microsoft.AspNetCore.Authorization.Authorize]
    /// </summary>
    public class SingalRAuthorizationMiddleware
    {
        private IConfiguration Configuration { get; }
        private readonly ILogger<SingalRAuthorizationMiddleware> _logger;
        private readonly RequestDelegate _next;

        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="next"></param>
        public SingalRAuthorizationMiddleware(RequestDelegate next, ILogger<SingalRAuthorizationMiddleware> logger, IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            Configuration = configuration;
        }

        /// <summary>
        /// Invoke
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context)
        {
            _logger.LogInformation($"SingalR Server>{DateTime.Now:HH:mm:ss.fff}>来了 {context.Request.Path}，{context.Request.PathBase}");//触发两次：signalr/negotiate + /signalr/negotiate
            if (string.Compare(context.Request.Path, SingalRHandler.Pattern, true) != 0)
            {
                await _next(context);
                return;
            }
            #region 握手鉴权时client/server的处理方式 https://learn.microsoft.com/zh-cn/aspnet/core/signalr/authn-and-authz?view=aspnetcore-7.0
            //Header参数方式:
            //server：       context.Request.Headers["Authorization"];
            //client-js:     .withUrl("http://localhost:port/signalr", { accessTokenFactory: () => loginToken })
            //client/Csharp: .WithUrl("http://localhost:port/signalr", options =>{ options.AccessTokenProvider = () => Task.FromResult("loginToken"); })
            //client/Csharp: .WithUrl("http://localhost:port/signalr", options =>{ options.Headers.Add("Authorization", "Bearer loginToken"); })

            //url参数方式:
            //server：       context.Request.Query["token"]
            //client-js:     .withUrl("http://localhost:port/signalr?token=tokenvalue")
            //client-Csharp: .WithUrl("http://localhost:port/signalr?token=tokenvalue")
            #endregion
            var token = context.Request.Query["token"] + string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = (context.Request.Headers["Authorization"] + string.Empty).Replace($"Bearer ", "");
                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogInformation("SingalR Server>InvokeAsync 缺少token");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await _next(context);
                    return;
                }
            }
            _logger.LogInformation($"SingalR Server>InvokeAsync token:{token}");
            SingalRHandler.WaitConnectUsers.Enqueue(123);
            await Task.Delay(0);
            await _next(context);
        }
    }
    #endregion

    /// <summary>
    /// SingalR Server
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class SingalRHandler : Hub
    {
        private readonly RequestDelegate _next;
        /// <summary>
        /// "/signalr"
        /// </summary>
        public static string Pattern => "/signalr";
        /// <summary>
        /// 待连接userId队列：[InvokeAsync校验通过后入队(WaitConnectUsers.Enqueue(userId)) ==》 OnConnectedAsync出队建立连接(WaitConnectUsers.TryDequeue(out var userId))]
        /// </summary>
        internal static ConcurrentQueue<int> WaitConnectUsers = new();
        private static ConcurrentDictionary<int, HubCallerContext> ConnectionDic = new();
        /// <summary>
        /// 当前服务实例需忽略的队列消息
        /// </summary>
        private static ConcurrentQueue<string> Ignoreds => new();
        /// <summary>
        /// 当前服务实例Id
        /// </summary>
        private static string ServerId => $"Server{Guid.NewGuid().ToString().Replace("-", string.Empty)}";
        private readonly ILogger<SingalRHandler> _logger;
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        //public SingalRHandler()
        //{
        //    _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SingalRHandler>();
        //    Configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
        //    _rdbs = RedisClients.Dbs;
        //    KickedOffline();
        //}
        public SingalRHandler(ILogger<SingalRHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] rdbs)
        {
            _logger = logger;
            Configuration = configuration;
            _rdbs = rdbs;
            KickedOffline();
        }
        
        #region 连接事件
        //[Microsoft.AspNetCore.Authorization.Authorize]
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($">{DateTime.Now:HH:mm:ss.fff} SingalR Server> 已连接[{Context.ConnectionId}]");
            WaitConnectUsers.TryDequeue(out var userId);
            #region 同一个客户端以最后一个连接为准
            //ConnectionDic.TryRemove(userId, out var oldClient);
            //ConnectionDic[userId] = Context;
            HubCallerContext oldClient = null;
            if (!ConnectionDic.TryAdd(userId, Context))
            {
                oldClient = ConnectionDic[userId];
                ConnectionDic[userId] = Context;
            }
            if (oldClient != null)
            {
                await Clients.Client(oldClient.ConnectionId).SendAsync("close", "已在别处上线");
                await Offline(oldClient);
            }
            else
            {
                //await _rdbs[2].SetAsync($"{ServerId}_off_{userId}", 0, 1);//监听过期
                Ignoreds.Enqueue($"{ServerId}_off_{userId}");
                await _rdbs[2].PublishAsync($"evt_KickedOff", userId.ToString());
            }
            #endregion
            await Clients.Client(Context.ConnectionId).SendAsync("connected", "来了老弟");
            await base.OnConnectedAsync();
        }
        #endregion
        private async Task Offline(HubCallerContext oldClient)
        {
            await Groups.RemoveFromGroupAsync(oldClient.ConnectionId, "GroupName");
            oldClient.Abort();
        }
        private async Task Offline(int userId, string? msg = null)
        {
            if (ConnectionDic.TryRemove(userId, out var oldClient))
            {
                _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}][{userId}]{msg}]({ConnectionDic.Count})");
                await Clients.Client(oldClient.ConnectionId).SendAsync("close", msg);
                await Offline(oldClient);
            }
        }
        #region 多实例/集群部署时：用Redis[发布订阅/过期监听]实现踢下线
        /// <summary>
        /// 订阅频道---多实例/集群部署时：用Redis[发布订阅/过期监听]实现踢下线
        /// </summary>
        /// <remarks>
        /// 【发布订阅】<br/>
        /// 1.服务端实例订阅[频道A]；<br/>
        /// 2.客户端上线时，当前服务实例先做检查，无重复的再向[频道A]发送客户端信息；<br/>
        /// 3.订阅[频道A]的服务实例收到[非自身发出的]消息后，检查客户端信息，如有则踢下线；<br/>
        /// <br/>
        /// 【过期监听】<br/>
        /// 1.服务端实例订阅[过期事件]；<br/>
        /// 2.客户端上线时，当前服务实例先做检查，无重复的再向[写客户端缓存]；<br/>
        /// 3.订阅[过期事件]的服务实例收到[非自身发出的]消息后，检查客户端信息，如有则踢下线；<br/>
        /// </remarks>
        public void KickedOffline()
        {
            _rdbs[2].Subscribe($"evt_KickedOff", async (chan, msg) =>
            {
                if (!Ignoreds.TryDequeue(out _))
                {
                    _ = int.TryParse(msg + string.Empty, out int userId);
                    await Offline(userId, "已在别处上线");
                }
            });
            //_ = _rdbs[15].Subscribe($"__keyevent@15__:expired", async (chan, msg) =>
            //{
            //    var key = msg + string.Empty;//$"{ServerId}_off_{userId}"
            //    if (!string.IsNullOrWhiteSpace(key))
            //    {
            //        var sid = key.Split('_')[0];
            //        if (!string.IsNullOrWhiteSpace(sid) && sid != ServerId)
            //        {
            //            _ = int.TryParse(key.Split('_')[2], out int userId);
            //            await Offline(userId, "已在别处上线");
            //        }
            //    }
            //});
        }
        #endregion

        #region 断开连接
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var keyId = ConnectionDic.Where(d => d.Value.ConnectionId == Context.ConnectionId).Select(d => d.Key).FirstOrDefault();
            ConnectionDic.TryRemove(keyId, out _);
            _logger.LogInformation($">{DateTime.Now:HH:mm:ss.fff} SingalR Server> 已断开[{Context.ConnectionId}：{exception?.Message}]");
            Context.Abort();
            await base.OnDisconnectedAsync(exception);
        } 
        #endregion

        private async Task CloseAll()
        {
            await Clients.All.SendAsync("close", "SingalR Server> 都退下吧");
            Dispose();
        }

        //[Microsoft.AspNetCore.Authorization.Authorize]
        public async Task ToServer(string method, object msg)
        {
            _logger.LogInformation($">{DateTime.Now:HH:mm:ss.fff} SingalR Server> 收到[{Context.ConnectionId}]的消息[{method}]：{msg}");
            await Task.Delay(10, Context.ConnectionAborted);
        }

        private async Task<bool> ToClient(HubCallerContext? hubCallerContext, string method, object arg1, CancellationToken cancellationToken = default)
        {
            if (hubCallerContext == null) return false;
            await Clients.Client(hubCallerContext.ConnectionId).SendCoreAsync(method, new[] { arg1 }, cancellationToken);
            return true;
        }
        private async Task<bool> ToClient(string? connectionId, string method, object arg1, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionId)) return false;
            await Clients.Client(connectionId).SendCoreAsync(method, new[] { arg1 }, cancellationToken);
            return true;
        }

        #region Method
        public int OnlineCount()
        {
            return ConnectionDic.Count;
        }
        public int[] Onlines()
        {
            return ConnectionDic.Select(d => d.Key).ToArray();
        }
        public async Task<bool> Offline(int uid, CancellationToken cancellationToken = default)
        {
            if (ConnectionDic.TryRemove(uid, out var oneVal))
            {
                await ToClient(oneVal, "close", "走你", cancellationToken);
                await Offline(oneVal);
                return true;
            }
            return false;
        }
        public async Task SendMsgAll(string method, string msg, CancellationToken cancellationToken = default)
        {
            await Clients.All.SendAsync(method, msg, cancellationToken);
            //foreach (var one in ConnectionDic)
            //{
            //    await ToClient(one.Value, method, msg, cancellationToken);
            //}
        }
        public async Task<bool> SendMsg(int uid, string method, string msg, CancellationToken cancellationToken = default)
        {
            if (ConnectionDic.TryGetValue(uid, out var oneVal))
            {
                return await ToClient(oneVal, method, msg, cancellationToken);
            }
            return false;
        }
        #endregion
    }
}
