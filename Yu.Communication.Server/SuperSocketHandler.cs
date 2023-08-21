using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperSocket;
using SuperSocket.Channel;
using SuperSocket.Command;
using SuperSocket.ProtoBase;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Reflection;

namespace Yu.Communication.Server
{
    public class SuperSocketHandler //: SuperSocket.Server.AppSession
    {
        public static ConcurrentDictionary<string, IAppSession> ConnectionDic = new();
        private static IHost host;
        private readonly ILogger<SuperSocketHandler> _logger;
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        /// <summary>
        /// await new SuperSocketHandler().StartServer(port, certName, certPwd);
        /// </summary>
        public SuperSocketHandler()
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SuperSocketHandler>();
            Configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
            _rdbs = RedisClients.Dbs;
        }

        /// <summary>
        /// IServiceCollection.AddSingleton&lt;SuperSocketHandler&lt;();<br/>
        /// private readonly SuperSocketHandler _superSocketHandler;<br/>
        /// public Worker(SuperSocketHandler superSocketHandler){ _superSocketHandler = superSocketHandler; }<br/>
        /// await _superSocketHandler.StartServer(port, certName);<br/>
        /// </summary>
        public SuperSocketHandler(ILogger<SuperSocketHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] rdbs)
        {
            _logger = logger;
            Configuration = configuration;
            _rdbs = rdbs;
        }
        private void BuildServerHost()
        {
            var serverOptions = Configuration.GetSection("SuperSocketOptions").Get<ServerOptions>() ?? new ServerOptions();
            host = SuperSocketHostBuilder.Create<StringPackageInfo, CommandLinePipelineFilter>()
                //.UseHostedService<SsService<StringPackageInfo>>()
                //.UseSession<SsAppSession>()
                //.UseSession<SuperSocketHandler>()
                .UseSessionHandler(SessionConnectedAsync, SessionClosedAsync)
                .UsePackageHandler(OnReceivedMessage)
                .UseCommand((commandOptions) => 
                {
                    //commandOptions.AddCommand<SuperSocketHandler>();//注册单个命令
                    //commandOptions.AddCommandAssembly(typeof(SuperSocketHandler).GetTypeInfo().Assembly);//注册程序集中的所有命令
                })
                .ConfigureSuperSocket(options =>
                {
                    //配置服务器信息
                    options.Name = serverOptions.Name;
                    #region 监听器配置 ip/port/ssl
                    options.Listeners = serverOptions.Listeners;
                    foreach (var certOptions in options.Listeners.Where(l => l.CertificateOptions != null && l.CertificateOptions.ClientCertificateRequired))
                    {
                        //certOptions.CertificateOptions.RemoteCertificateValidationCallback = CertificateHelper.remoteCertValidationCallback;
                        certOptions.CertificateOptions.RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback(CertificateHelper.ValidateRemoteCertificate);
                    }
                    #region 监听器配置 ip/port/ssl
                    //var listenerOptions = options.Listeners.Where(l => l.CertificateOptions != null).FirstOrDefault();
                    //var certificateOptions = listenerOptions?.CertificateOptions;
                    //var port = certificateOptions != null ? listenerOptions.Port : options.Listeners.Select(l => l.Port).FirstOrDefault();
                    //if (certificateOptions != null)
                    //{
                    //    certificateOptions = new CertificateOptions
                    //    {
                    //        //RemoteCertificateValidationCallback = CertificateHelper.remoteCertValidationCallback,
                    //        RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback(CertificateHelper.ValidateRemoteCertificate),
                    //        Certificate = string.IsNullOrWhiteSpace(certificateOptions.Password) ? CertificateHelper.GetCertificateFromStore(certificateOptions.FilePath) : CertificateHelper.GetCertificate(certificateOptions.FilePath, certificateOptions.Password)
                    //    };
                    //}
                    //options.Listeners = new List<ListenOptions>
                    //{
                    //    new ListenOptions
                    //    {
                    //        Ip = "Any",
                    //        Port = port,
                    //        Security = System.Security.Authentication.SslProtocols.None,
                    //        CertificateOptions = certificateOptions
                    //    }
                    //}; 
                    #endregion
                    #endregion
                })
                .ConfigureLogging((hostCtx, loggingBuilder) => loggingBuilder.AddConsole())
                .Build();
        }
        public async Task StartServer(CancellationToken cancellationToken = default)
        {
            try
            {
                BuildServerHost();
                var server = host.AsServer();
                _logger.LogInformation($"SuperSocket Server 开始启动......[{string.Join(",", server.Options.Listeners.Select(d => d.Port))},{server.Name}]");
                await host.StartAsync(cancellationToken);
                _logger.LogInformation($"SuperSocket Server 启动完成[{string.Join(",", server.Options.Listeners.Select(d => d.Port))},{server.Name}]");
            }
            catch when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SuperSocket Server启动出错");
                throw;
            }
        }

        public async Task StopServer(CancellationToken cancellationToken = default)
        {
            try
            {
                var server = host.AsServer();
                if (server == null) return;
                if (host.AsServer().State == ServerState.Starting || host.AsServer().State == ServerState.Stopping) await Task.Delay(2000, cancellationToken);
                if (host.AsServer().State != ServerState.Started) return;
                _logger.LogInformation($"SuperSocket Server 开始停止......[{string.Join(",", server.Options.Listeners.Select(d => d.Port))},{server.Name}]");
                await host.StopAsync(cancellationToken);
                _logger.LogInformation($"SuperSocket Server 停止了");
            }
            catch when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SuperSocket Server停止出错");
                throw;
            }
        }

        #region 处理接收到的数据包
        /// <summary>
        /// 处理接收到的数据包
        /// </summary>
        /// <param name="appSession">连接</param>
        /// <param name="package">数据包</param>
        /// <returns></returns>
        private async ValueTask OnReceivedMessage(IAppSession appSession, StringPackageInfo package)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Server 收：{package.Body}");
            if (appSession.State != SessionState.Connected || (string.Compare(package.Key, "LOGIN") != 0 && !ConnectionDic.Any(d => d.Value.SessionID == appSession.SessionID))) return;
            //注册用于处理接收到的数据包处理器
            var result = 0;
            switch (package.Key.ToUpper())
            {
                case "ADD":
                    result = package.Parameters.Select(d => Convert.ToInt32(d)).Sum();
                    break;
                case "SUB":
                    result = package.Parameters.Select(d => Convert.ToInt32(d)).Aggregate((x, y) => x - y);
                    break;
                case "MULT":
                    result = package.Parameters.Select(d => Convert.ToInt32(d)).Aggregate((x, y) => x * y);
                    break;
                case "DIV":
                    result = package.Parameters.Select(d => Convert.ToInt32(d)).Aggregate((x, y) => x / y);
                    break;
                case "LOGIN":
                    var name = package.Parameters[0];
                    var pwd = package.Parameters.Length > 1 ? package.Parameters[1] : string.Empty;
                    if (name != "aaa" || pwd != "ppp")
                    {
                        await appSession.SendAsync(System.Text.Encoding.UTF8.GetBytes($"账密有误，关闭连接\r\n"));
                        _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Server 账密有误，关闭连接：{name},{pwd}");
                        await appSession.CloseAsync(CloseReason.ServerShutdown);
                        appSession.Reset();
                    }
                    else
                    {
                        #region 同一个客户端以最后一个连接为准 todo：多实例/集群部署时
                        var userId = DateTime.Now.Ticks.ToString();
                        //ConnectionDic.TryRemove(userId, out var oldClient);
                        //ConnectionDic[userId] = appSession;
                        IAppSession oldClient = null;
                        if (!ConnectionDic.TryAdd(userId, appSession))
                        {
                            oldClient = ConnectionDic[userId];
                            ConnectionDic[userId] = appSession;
                        }
                        if (oldClient != null)
                        {
                            await oldClient.SendAsync(System.Text.Encoding.UTF8.GetBytes($"已在别处上线"));
                            await oldClient.CloseAsync(CloseReason.ServerShutdown);
                        }
                        #endregion
                    }
                    break;
                case "MSG":
                    //todo:handle msg
                    break;
            }
        }
        #endregion

        #region 处理会话事件-通过UseSessionHandler注册：SuperSocketHostBuilder.UseSessionHandler(OnSessionConnectedAsync, OnSessionClosedAsync)
        private ValueTask SessionConnectedAsync(IAppSession appSession)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已连接[{appSession.Server.SessionCount},{appSession.SessionID}, {appSession.StartTime}, {appSession.RemoteEndPoint}]");
            return default;
        }
        private ValueTask SessionClosedAsync(IAppSession appSession, CloseEventArgs e)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已断开[{appSession.Server.SessionCount},{appSession.SessionID}, {appSession.StartTime}~{appSession.LastActiveTime}, {appSession.RemoteEndPoint}：{e.Reason}]");
            return ValueTask.CompletedTask;
        }
        #endregion

        #region 处理会话事件-通过扩展SuperSocket.Server.AppSession：重写会话事件，SuperSocketHostBuilder.UseSession<SuperSocketHandler>();
        //protected override ValueTask OnSessionConnectedAsync()
        //{
        //    _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已连接[{Server.SessionCount},{SessionID}, {StartTime}, {RemoteEndPoint}]");
        //    return base.OnSessionConnectedAsync();
        //}
        //protected override ValueTask OnSessionClosedAsync(CloseEventArgs e)
        //{
        //    _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已断开[{Server.SessionCount},{SessionID}, {StartTime}~{LastActiveTime}, {RemoteEndPoint}：{e.Reason}]");
        //    return base.OnSessionClosedAsync(e);
        //    return ValueTask.CompletedTask;
        //}
        #endregion
    }

    #region 处理会话事件-通过扩展SuperSocket.Server.AppSession：重写会话事件，SuperSocketHostBuilder.UseSession<MyAppSession>();
    /// <summary>
    /// 处理会话事件-通过扩展SuperSocket.Server.AppSession
    /// </summary>
    internal class SsAppSession : SuperSocket.Server.AppSession
    {
        private readonly ILogger<SuperSocketHandler> logger;
        private IConfiguration Configuration { get; }
        public SsAppSession(ILogger<SuperSocketHandler> _logger, IConfiguration configuration)
        {
            logger = _logger;
            Configuration = configuration;
        }
        protected override ValueTask OnSessionConnectedAsync()
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已连接[{Server.SessionCount},{SessionID}, {StartTime}, {RemoteEndPoint}]");
            return base.OnSessionConnectedAsync();
        }
        protected override ValueTask OnSessionClosedAsync(CloseEventArgs e)
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已断开[{Server.SessionCount},{SessionID}, {StartTime}~{LastActiveTime}, {RemoteEndPoint}：{e.Reason}]");
            return base.OnSessionClosedAsync(e);
            //return ValueTask.CompletedTask;
        }
    }
    #endregion

    #region 处理会话事件-通过扩展SuperSocketService：重写会话事件，SuperSocketHostBuilder.UseHostedService<SsService<StringPackageInfo>>()
    /// <summary>
    /// 处理会话事件-通过扩展SuperSocketService
    /// </summary>
    /// <typeparam name="TReceivePackageInfo"></typeparam>
    internal class SsService<TReceivePackageInfo> : SuperSocket.Server.SuperSocketService<TReceivePackageInfo> where TReceivePackageInfo : class
    {
        private readonly ILogger<SuperSocketHandler> logger;
        private IConfiguration Configuration { get; }

        public SsService(IServiceProvider serviceProvider, IOptions<ServerOptions> serverOptions, ILogger<SuperSocketHandler> _logger, IConfiguration configuration) : base(serviceProvider, serverOptions) {
            logger = _logger;
            Configuration = configuration;
        }

        protected override async ValueTask OnSessionConnectedAsync(IAppSession appSession)
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已连接[{SessionCount},{appSession.SessionID}, {appSession.StartTime}, {appSession.RemoteEndPoint}]");
            await appSession.SendAsync(System.Text.Encoding.UTF8.GetBytes($"来了老弟"));
            //return base.OnSessionConnectedAsync(appSession);
        }

        protected override ValueTask OnSessionClosedAsync(IAppSession appSession, CloseEventArgs e)
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Client 已断开[{SessionCount},{appSession.SessionID}, {appSession.StartTime}~{appSession.LastActiveTime}, {appSession.RemoteEndPoint}：{e.Reason}]");
            return base.OnSessionClosedAsync(appSession, e);
            //return ValueTask.CompletedTask;
        }
        protected override async ValueTask OnStartedAsync()
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Server 已启动[{Name}]");
            await base.OnStartedAsync();
        }
        protected override async ValueTask OnStopAsync()
        {
            logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]SuperSocket Server 已停止[{Name}]");
            await base.OnStopAsync();
        }
    }
    #endregion
}
