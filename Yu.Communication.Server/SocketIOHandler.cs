using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocketIOSharp.Common;
using SocketIOSharp.Server;
using SocketIOSharp.Server.Client;
using System.Collections.Concurrent;

namespace Yu.Communication.Server
{
    public class SocketIOHandler
    {
        public static ConcurrentDictionary<string, SocketIOSocket> ConnectionDic = new();
        /// <summary>
        /// 当前服务实例需忽略的队列消息
        /// </summary>
        public static ConcurrentQueue<string> Ignoreds = new();
        /// <summary>
        /// 当前服务实例Id
        /// </summary>
        private static string ServerId => $"Server{Guid.NewGuid().ToString().Replace("-", string.Empty)}";
        private static SocketIOServer Server;
        private readonly ILogger<SocketIOHandler> _logger;
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        /// <summary>
        /// await new SocketIOHandler().StartServer();
        /// </summary>
        public SocketIOHandler()
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SocketIOHandler>();
            Configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
            _rdbs = RedisClients.Dbs;
        }

        /// <summary>
        /// IServiceCollection.AddSingleton&lt;SocketIOHandler&gt;();<br/>
        /// private readonly SocketIOHandler _socketIOHandler;<br/>
        /// public Worker(SocketIOHandler socketIOHandler){ _socketIOHandler = socketIOHandler; }<br/>
        /// await _socketIOHandler.StartServer();<br/>
        /// </summary>
        public SocketIOHandler(ILogger<SocketIOHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] rdbs)
        {
            _logger = logger;
            Configuration = configuration;
            _rdbs = rdbs;
        }

        #region 启动
        private static ConcurrentQueue<int> WaitConnectUsers = new();
        public async Task StartServer(CancellationToken cancellationToken = default)
        {
            try
            {
                var port = Configuration.GetValue<ushort>("SocketIOPort");
                var portSSl = Configuration.GetValue<ushort>("SocketIOPortSsl");
                var conf = new CertData
                {
                    CertName = Configuration.GetValue<string>("CertName"),
                    CertFile = Configuration.GetValue<string>("CertFile"),
                    CertPwd = Configuration.GetValue<string>("CertPwd")
                };
                var options = new SocketIOServerOption(conf.UseSsl ? portSSl : port, Secure: false, ServerCertificate: CertificateHelper.GetCertificateFromStore(conf.CertName), ClientCertificateValidationCallback: CertificateHelper.ValidateRemoteCertificate, VerificationTimeout: 500, AllowEIO3: true);
                //var options = new SocketIOServerOption(conf.UseSsl ? portSSl : port, Secure: conf.UseSsl, ServerCertificate: CertificateHelper.GetCertificate(conf.CertFile,conf.CertPwd), ClientCertificateValidationCallback: ValidateRemoteCertificate);
                Server = new SocketIOServer(options);
                #region 鉴权处理 [server.OnConnecting]方式
                Server.OnConnecting((headerAndId) =>
                {
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO Client 来了[{headerAndId.Item2}]");
                    var token = headerAndId.Item1["token"];
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        Server.AddVerificationResult(headerAndId.Item2, true);
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} 缺少token");
                        return;
                    }
                    Server.AddVerificationResult(headerAndId.Item2, false);
                    WaitConnectUsers.Enqueue(111);
                }); 
                #endregion
                Server.OnConnection(async (socket) =>
                {
                    var key = DateTime.Now.Ticks.ToString();
                    //socket.Emit("message", "走你，断开");
                    #region 鉴权后 [server.OnConnecting]方式
                    if (!WaitConnectUsers.TryDequeue(out var userId) || userId < 1)
                    {
                        _logger.LogInformation($"走你：<{userId}>");
                        await Task.Delay(30, cancellationToken);
                        socket.Emit("close", "走你，断开");
                        await Task.Delay(1000);
                        socket.Close();
                        return;
                    }
                    #endregion
                    socket.Off(SocketIOEvent.ERROR, () => { });
                    socket.Off(SocketIOEvent.DISCONNECT, () => { });
                    socket.Off("message", () => { });
                    socket.On(SocketIOEvent.ERROR, (data) =>
                    {
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>Error：[{data[0]}]");
                    });
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO Client 已连接");
                    await AfterAuth(socket, key, cancellationToken);
                    //socket.Off("auth", () => { });
                    //HandleAuth(socket, key, cancellationToken);//无[server.OnConnecting]方式
                });
                //await Task.Factory.StartNew(async () => await AuthCheck(cancellationToken: cancellationToken), TaskCreationOptions.LongRunning);//无[server.OnConnecting]方式
                Server.Start();
                KickedOffline();
                _logger.LogInformation($"SocketIO Server 启动了[{Server.Option.Port},{Server.Option.Path},{Server.Option.Secure}]");
            }
            catch when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SocketIO Server启动出错");
                throw;
            }
        }
        #endregion

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
                if (!Ignoreds.TryDequeue(out _)) await Offline(msg + string.Empty, "已在别处上线");
            });
            //_ = _rdbs[15].Subscribe($"__keyevent@15__:expired", async (chan, msg) =>
            //{
            //    var key = msg + string.Empty;//$"{ServerId}_off_{wsKey}"
            //    if (!string.IsNullOrWhiteSpace(key))
            //    {
            //        var sid = key.Split('_')[0];
            //        if (!string.IsNullOrWhiteSpace(sid) && sid != ServerId)
            //        {
            //            await Offline(key.Split('_')[2], "已在别处上线");
            //        }
            //    }
            //});
        }
        #endregion

        private async Task Offline(string key, string? msg = null)
        {
            if (ConnectionDic.TryRemove(key, out var oldClient))
            {
                _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] [{key}]{msg}({ConnectionDic.Count})");
                oldClient.Emit("message", msg);
                oldClient.Close();
            }
        }

        #region 校验通过后 - 无[server.OnConnecting]方式
        private async Task AfterAuth(SocketIOSocket socket, string wsKey, CancellationToken cancellationToken)
        {
            socket.On(SocketIOEvent.DISCONNECT, (data) =>
            {
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIOServer>Disconnected：[{data[0]}]");
            });
            socket.On("message", (data) =>
            {
                _logger.LogInformation($"SocketIO Server On-mesage:[{data[0]}]");
            });
            #region 同一个客户端以最后一个连接为准
            //if (ConnectionDic.TryRemove(wsKey, out var oldClient))
            //{
            //    oldClient.Close();
            //}
            //ConnectionDic[wsKey] = oldClient;
            SocketIOSocket oldClient = null;
            if (!ConnectionDic.TryAdd(wsKey, socket))
            {
                oldClient = ConnectionDic[wsKey];
                ConnectionDic[wsKey] = socket;
            }
            if (oldClient != null && socket.GetHashCode() != oldClient.GetHashCode())
            {
                oldClient.Close();
            }
            else
            {
                //await _rdbs[2].SetAsync($"{ServerId}_off_{wsKey}", 0, 1);//监听过期
                Ignoreds.Enqueue($"{ServerId}_off_{wsKey}");
                await _rdbs[2].PublishAsync($"evt_KickedOff", wsKey);
            }
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] {socket.ReadyState}");
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] 连接数：{ConnectionDic.Count}");
            #endregion
            await Task.Delay(30, cancellationToken);
            socket.Emit("message", "来了老弟");
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] {socket.ReadyState}");
            //await Task.Delay(50000, cancellationToken);
            //socket.Emit("close", "走你");
            //socket.Close();//Client-SocketIOEvent.DISCONNECT：EngineIOSharp.Common.EngineIOException: Transport close
            //socket.Dispose(); 
        }
        #endregion

        #region 鉴权处理 - 无[server.OnConnecting]方式
        SocketIOSocket? oldSocket = null;
        /// <summary>
        /// 鉴权处理
        /// </summary>
        private void HandleAuth(SocketIOSocket socket, string key, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO Server 走起[{key}]");
            UnAuths.TryAdd(key, new DataSocketNotAuth(socket, DateTime.UtcNow));
            socket.On("auth", async (data) =>
            {
                if (oldSocket == socket)
                {
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO Server auth 重复触发[{key}]");
                    return;
                }
                oldSocket = socket;
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff}>SocketIO Server 收到校验信息[{key}]");
                UnAuths.TryRemove(key, out _);
                var wsKey = data[0] + string.Empty;
                #region 校验token
                try
                {
                    _logger.LogInformation($"开始校验token[{key}][{data[0]}]");
                    socket.Emit("message", "开始校验token");
                    string token = data[0] + string.Empty;
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        _logger.LogInformation($"缺少token，断开");
                        socket.Emit("auth", "缺少token，断开");
                        await Task.Delay(1000, cancellationToken);
                        socket.Close();
                        return;
                    }
                    //var userId = await RedisClients.Dbs[0].GetAsync<int>($"Token{token}");
                    //wsKey = userId.ToString();
                    //if (userId < 1)
                    //{
                    //    _logger.LogInformation($"token无效，断开[{token}]");
                    //    socket.Emit("auth", $"token无效，断开:{token}");
                    //    await Task.Delay(1000, cancellationToken);
                    //    socket.Close();
                    //    return;
                    //}
                    await AfterAuth(socket, wsKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"token校验出错：<{data[0]}>");
                    socket.Emit("auth", "token校验出错");
                    await Task.Delay(1000, cancellationToken);
                    socket.Close();
                    return;
                }
                #endregion
            });
        }
        #endregion
        #region 授权超时监测 - 无[server.OnConnecting]方式
        /// <summary>
        /// 授权超时监测
        /// </summary>
        private async Task AuthCheck(CancellationToken cancellationToken)
        {
            var AuthTimeoutSeconds = Math.Max(5, Configuration.GetSection("GameRule").GetValue<int>("AuthWaitSecond"));//认证超时秒数
            var sleep = 1000 * AuthTimeoutSeconds - 1000;
            while (true)
            {
                try
                {
                    if (UnAuths.IsEmpty)
                    {
                        await Task.Delay(sleep, cancellationToken);
                        continue;
                    }
                    AuthTimeoutSeconds = Math.Max(5, Configuration.GetSection("GameRule").GetValue<int>("AuthWaitSecond"));
                    var min = UnAuths.OrderBy(d => d.Key).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(min.Key) || DateTime.UtcNow.AddSeconds(-AuthTimeoutSeconds) <= min.Value.ActivityTime)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }
                    UnAuths.TryRemove(min.Key, out _);
                    _logger.LogInformation($"认证超时，断开[{min.Key}，{min.Value.ActivityTime}]");
                    min.Value.Socket?.Emit("auth", "认证超时，断开");
                    min.Value.Socket?.Close();
                }
                catch when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, $"认证超时监测出错");
                }
                finally
                {
                    Thread.Sleep(10);
                }
            }
        }
        #endregion
        #region 未认证的连接 - 无[server.OnConnecting]方式
        /// <summary>
        /// 未认证的连接.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DataSocketNotAuth> UnAuths = new();
        /// <summary>
        /// socket未认证连接
        /// </summary>
        struct DataSocketNotAuth
        {
            /// <summary>
            /// 连接实例
            /// </summary>
            public SocketIOSocket Socket { get; set; }

            /// <summary>
            /// 活动时间
            /// </summary>
            public DateTime ActivityTime { get; set; }

            /// <summary>
            /// socket未认证连接.
            /// </summary>
            public DataSocketNotAuth(SocketIOSocket socket, DateTime activityTime)
            {
                this.Socket = socket;
                this.ActivityTime = activityTime;
            }
        }
        #endregion
    }
}
