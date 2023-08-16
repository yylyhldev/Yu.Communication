using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace Yu.Communication.Server
{
    public class SocketHandler
    {
        public static ConcurrentDictionary<string, Socket> ConnectionDic = new();
        public static ConcurrentDictionary<string, TcpClient> ConnectionDic2 = new();
        private readonly ILogger<SocketHandler> _logger;
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        /// <summary>
        /// 直接实例化方式：await new SocketHandler().StartServer(port, certName);
        /// </summary>
        public SocketHandler()
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SocketHandler>();
            Configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
            _rdbs = RedisClients.Dbs;
        }

        /// <summary>
        /// 依赖注入方式
        /// </summary>
        /// <remarks>
        /// IServiceCollection.AddSingleton<SocketHandler>();
        /// private readonly SocketHandler _socketHandler;
        /// public Worker(SocketHandler socketHandler){ _socketHandler = socketHandler; }
        /// await _socketHandler.StartServer(port, certName);
        /// </remarks>
        public SocketHandler(ILogger<SocketHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] rdbs)
        {
            _logger = logger;
            Configuration = configuration;
            _rdbs = rdbs;
        }
        public async Task StartServer(CancellationToken cancellationToken = default)
        {
            try
            {
                var port = Configuration.GetValue<int>("SocketPort");
                var portSsl = Configuration.GetValue<int>("SocketPortSsl");
                var certData = new CertData
                {
                    CertName = Configuration.GetValue<string>("CertName"),
                    CertFile = Configuration.GetValue<string>("CertFile"),
                    CertPwd = Configuration.GetValue<string>("CertPwd")
                };
                IPEndPoint ipEndPoint = new(IPAddress.Any, certData.UseSsl ? portSsl : port);

                #region new TcpListener(ipEndPoint).Start()
                var tcpListener = new TcpListener(ipEndPoint);
                tcpListener.Start();
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server TcpListener 启动了[{tcpListener.LocalEndpoint}，{tcpListener.Server}]");

                new Thread(AcceptTcpClient) { IsBackground = true }.Start(new Tuple<TcpListener, CertData>(tcpListener, certData));
                //new Thread(async delegate () { await AcceptTcpClientAsync(listener, certData, cancellationToken); }) { IsBackground = true }.Start();
                //await Task.Factory.StartNew(async () => await AcceptTcpClientAsync(listener, certData, cancellationToken), TaskCreationOptions.LongRunning);
                #endregion

                #region new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                //Socket socketListener = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                //socketListener.Bind(ipEndPoint);
                //socketListener.Listen(10);
                //_logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server 启动了[{socketListener.LocalEndPoint},{socketListener.Connected}]");

                ////new Thread(AcceptClient) { IsBackground = true }.Start(socketListener);
                ////new Thread(async delegate () { await AcceptClientAsync(socketListener, cancellationToken); }) { IsBackground = true }.Start();
                //await Task.Factory.StartNew(async () => await AcceptClientAsync(socketListener), TaskCreationOptions.LongRunning);
                #endregion
            }
            catch when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{DateTime.Now:HH:mm:ss.fff} Socket Server启动出错");
                throw;
            }
        }

        #region 接受客户端连接 TcpListener-TcpClient
        /// <summary>
        /// 接受客户端连接 new Thread().Start()
        /// </summary>
        /// <param name="obj">监听器</param>
        private async void AcceptTcpClient(object obj)
        {
            var listener = (Tuple<TcpListener, CertData>)obj;
            await AcceptTcpClientAsync(listener.Item1, listener.Item2);
        }
        /// <summary>
        /// 接受客户端连接 await AcceptTcpClientAsync() / await Task.Factory.StartNew(async () => await AcceptTcpClientAsync());
        /// </summary>
        /// <param name="listener">监听器</param>
        private async Task AcceptTcpClientAsync(TcpListener? listener, CertData certData, CancellationToken cancellationToken = default)
        {
            if (listener == null)
            {
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server TcpListener is null");
                return;
            }
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server TcpListener 开始监听连接-AcceptTcpClient");
            var useSsl = certData.UseSsl;
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    //AcceptTcpClient()执行过后 当前线程会阻塞 只有在有客户端连接时才会继续执行
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                    var stream = client.GetStream();// 创建用于发送和接受数据的NetworkStream
                    var sslStream = useSsl ? new SslStream(stream, true, userCertificateValidationCallback: CertificateHelper.ValidateRemoteCertificate) : null;
                    //var sslStream = useSsl ? new SslStream(stream, false) : null;
                    try
                    {
                        if (useSsl)
                        {
                            //创建自签名证书：
                            //https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-2.0/bfsktky3(v=vs.80)?redirectedfrom=MSDN
                            //https://learn.microsoft.com/zh-cn/windows/win32/seccrypto/makecert [已弃用，改用 Powershell Cmdlet：New-SelfSignedCertificate]
                            //makecert -r -pe -n "CN=TestSocketServer" -sky exchange -ss Root -e 12/31/2099 -b 11/20/2022 -sr LocalMachine/CurrentUser
                            //certmgr.msc
                            var certCommonName = certData.CertName;//.Split(",")[0].Split("=")[1];
                            //certCommonName = "TestSocketServer";
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} 证书公用名[{certCommonName}]");
                            var certificate = CertificateHelper.GetCertificateFromStore(certCommonName, true);
                            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} 证书[{certificate}]");

                            ////SslStream.AuthenticateAsClient里的targetHost对应证书中公用名
                            ////new SslStream()中RemoteCertificateValidationCallback? userCertificateValidationCallback启用验证时客户端也要证书：
                            ////    方式一：sslStream.AuthenticateAsServer(..., bool clientCertificateRequired, ...)
                            ////        参数ClientCertificateRequired必须为true；
                            ////    方式二：AuthenticateAsServer(SslServerAuthenticationOptions sslServerAuthenticationOptions)
                            ////        参数SslServerAuthenticationOptions.ClientCertificateRequired必须为true
                            await sslStream.AuthenticateAsServerAsync(certificate, true, SslProtocols.Tls12, true);
                            var sslAuthOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificate = certificate,
                                ClientCertificateRequired = true,
                                EnabledSslProtocols = SslProtocols.Tls12,
                                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                            };
                            //await sslStream.AuthenticateAsServerAsync(sslAuthOptions, cancellationToken);
                        }
                        //获取客户端ip和端口号
                        var iphost = client?.Client.RemoteEndPoint?.ToString();
                        #region 同一个客户端以最后一个连接为准 todo：多实例/集群部署时
                        //ConnectionDic2.TryRemove(iphost, out var oldClient);
                        //ConnectionDic2[iphost] = client;
                        TcpClient? oldClient = null;
                        if (!ConnectionDic2.TryAdd(iphost, client))
                        {
                            oldClient = ConnectionDic2[iphost];
                            ConnectionDic2[iphost] = client;
                        }
                        if (oldClient != null)
                        {
                            if (useSsl) await sslStream.WriteAsync(Encoding.UTF8.GetBytes($"已在别处上线"), cancellationToken);
                            else await stream.WriteAsync(Encoding.UTF8.GetBytes($"已在别处上线"), cancellationToken);
                            oldClient.Close();
                            oldClient.Dispose();
                        }
                        #endregion
                        //ConnectionDic2.TryAdd(iphost, client);
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient [{iphost}]已连接[{client?.Client.RemoteEndPoint}/{client?.Connected}]({ConnectionDic2.Count})");
                        if (useSsl) await sslStream.WriteAsync(Encoding.UTF8.GetBytes($"来了老弟{iphost}"), cancellationToken);
                        else await stream.WriteAsync(Encoding.UTF8.GetBytes($"来了老弟{iphost}"), cancellationToken);

                        //新线程监控接收新客户端数据
                        new Thread(StartReceiveTcp) { IsBackground = true }.Start(new Tuple<TcpClient, SslStream>(client, sslStream));
                        //await Task.Factory.StartNew(async () => await StartReceiveTcpAsync(client, sslStream, cancellationToken));
                    }
                    catch (AuthenticationException aex)
                    {
                        _logger.LogWarning($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient Authentication failed - closing the connection.");
                        sslStream?.Close();
                    }
                    catch (Exception aex)
                    {
                        stream.Close();
                        client.Close();
                        client.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    var baseException = ex.GetBaseException();
                    if (baseException is not TaskCanceledException && baseException is not SocketException)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient 异常:{ex.Message}.[{client?.Client.RemoteEndPoint}/{client?.Connected}]({ConnectionDic2.Count})");
                    }
                }
            }
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient 结束连接监听[{listener.LocalEndpoint}，{listener.Server}]");
            listener.Stop();
        }
        #endregion

        #region 异步接收消息 TcpListener-TcpClient
        /// <summary>
        /// 异步接收消息 new Thread().Start()
        /// </summary>
        /// <param name="obj">socket连接</param>
        private async void StartReceiveTcp(object obj)
        {
            var client = (Tuple<TcpClient, SslStream>)obj;
            await StartReceiveTcpAsync(client.Item1, client.Item2);
        }
        /// <summary>
        /// 异步接收消息 await StartReceiveTcpAsync()/await Task.Factory.StartNew(async () => await StartReceiveTcpAsync());
        /// </summary>
        /// <param name="client">socket连接</param>
        private async Task StartReceiveTcpAsync(TcpClient? client, SslStream? sslStream = null, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server TcpListener 开始监听消息-StartReceiveTcp");
            var buffer = new byte[1024];
            if (client == null)
            {
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient is null");
                return;
            }
            var stream = client.GetStream();// 创建用于发送和接受数据的NetworkStream
            var iphost = client?.Client.RemoteEndPoint?.ToString();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    //Read(Byte[]) 从绑定的 Socket 套接字接收数据，将数据存入接收缓冲区。
                    //该方法执行过后同 AcceptTcpClient()方法一样  当前线程会阻塞 等到客户端下一次发来数据时继续执行
                    var count = sslStream != null ? await sslStream.ReadAsync(buffer, cancellationToken) : await stream.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        ConnectionDic2.TryRemove(iphost, out _);
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient [{iphost}]已断开连接[{client?.Connected}]({ConnectionDic2.Count})");
                        client?.Close();
                        client?.Dispose();
                        break;
                    }
                    var str = Encoding.Default.GetString(buffer, 0, count);
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient 收到{iphost}数据: {str}");
                }
                catch (Exception ex)
                {
                    var baseException = ex.GetBaseException();
                    if (baseException is not TaskCanceledException && baseException is not SocketException)
                    {
                        ConnectionDic2.TryRemove(iphost, out _);
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient Msg 异常:{ex.Message}|||{ex.InnerException}.[{client?.Connected}]({ConnectionDic2.Count})");
                    }
                    sslStream?.Close();
                    stream?.Close();
                    client?.Close();
                    client?.Dispose();
                    break;
                }
            }
            ConnectionDic2.TryRemove(iphost, out var oldClient);
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket TcpClient 结束消息监听[{client?.Connected}，{iphost}]({ConnectionDic2.Count})");
            if (!cancellationToken.IsCancellationRequested)
            {
                oldClient?.Close();
                oldClient?.Dispose();
            }
        }
        #endregion

        #region 接受客户端连接 Socket
        /// <summary>
        /// 接受客户端连接 new Thread().Start()
        /// </summary>
        /// <param name="obj">监听器</param>
        private async void AcceptClient(object obj)
        {
            var listener = (Socket)obj;
            await AcceptClientAsync(listener);
        }
        /// <summary>
        /// 接受客户端连接 await AcceptClientAsync() / await Task.Factory.StartNew(async () => await AcceptClientAsync());
        /// </summary>
        /// <param name="listener">监听器</param>
        private async Task AcceptClientAsync(Socket? listener, CancellationToken cancellationToken = default)
        {
            if (listener == null)
            {
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server Listener is null");
                return;
            }
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server 开始监听连接-AcceptClient");
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket? client = null;
                try
                {
                    //Accept()执行过后 当前线程会阻塞 只有在有客户端连接时才会继续执行
                    client = await listener.AcceptAsync(cancellationToken);
                    //获取客户端ip和端口号
                    var iphost = client?.RemoteEndPoint?.ToString();
                    #region 同一个客户端以最后一个连接为准
                    //ConnectionDic.TryRemove(iphost, out var oldClient);
                    //ConnectionDic[iphost] = client;
                    Socket? oldClient = null;
                    if (!ConnectionDic.TryAdd(iphost, client))
                    {
                        oldClient = ConnectionDic[iphost];
                        ConnectionDic[iphost] = client;
                    }
                    if (oldClient != null)
                    {
                        await oldClient.SendAsync(Encoding.UTF8.GetBytes($"已在别处上线"), cancellationToken);
                        oldClient.Shutdown(SocketShutdown.Both);
                        oldClient.Close();
                    }
                    #endregion
                    //ConnectionDic.TryAdd(iphost, client);
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client [{iphost}]已连接[{listener?.Connected}/{client?.Connected}]({ConnectionDic.Count})");
                    await client.SendAsync(Encoding.UTF8.GetBytes($"来了老弟{iphost}"), cancellationToken);

                    //新线程监控接收新客户端数据
                    //new Thread(StartReceive) { IsBackground = true }.Start(client);
                    await Task.Factory.StartNew(async () => await StartReceiveAsync(client));
                }
                catch (Exception ex)
                {
                    var baseException = ex.GetBaseException();
                    if (baseException is not TaskCanceledException && baseException is not SocketException)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 异常:{ex.Message}.[{listener?.Connected}/{client?.Connected}]({ConnectionDic.Count})");
                    }
                }
            }
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client 结束连接监听[{listener?.Connected}]");
        }
        #endregion

        #region 异步接收消息 Socket
        /// <summary>
        /// 异步接收消息 new Thread().Start()
        /// </summary>
        /// <param name="obj">socket连接</param>
        private async void StartReceive(object obj)
        {
            var client = (Socket)obj;
            await StartReceiveAsync(client);
        }
        /// <summary>
        /// 异步接收消息 await StartReceiveAsync()/await Task.Factory.StartNew(async () => await StartReceiveAsync());
        /// </summary>
        /// <param name="client">socket连接</param>
        private async Task StartReceiveAsync(Socket? client, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Server 开始监听消息-StartReceive");
            var buffer = new byte[1024];
            if (client == null)
            {
                _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client is null");
                return;
            }
            var iphost = client?.RemoteEndPoint?.ToString();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    //Receive(Byte[]) 从绑定的 Socket 套接字接收数据，将数据存入接收缓冲区。
                    //该方法执行过后同 Accept()方法一样  当前线程会阻塞 等到客户端下一次发来数据时继续执行
                    var count = await client.ReceiveAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        ConnectionDic.TryRemove(iphost, out _);
                        _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client [{iphost}]已断开连接[{client?.Connected}]({ConnectionDic.Count})");
                        client?.Shutdown(SocketShutdown.Both);
                        client?.Close();
                        break;
                    }
                    var str = Encoding.Default.GetString(buffer, 0, count);
                    _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client 收到{iphost}数据: {str}");
                }
                catch (Exception ex)
                {
                    var baseException = ex.GetBaseException();
                    if (baseException is not TaskCanceledException && baseException is not SocketException)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 异常:{ex.Message}.[{client?.Connected}]({ConnectionDic.Count})");
                    }
                    break;
                }
            }
            ConnectionDic.TryRemove(iphost, out var oldClient);
            _logger.LogInformation($"{DateTime.Now:HH:mm:ss.fff} Socket Client 结束消息监听[{client?.Connected}，{iphost}]({ConnectionDic.Count})");
            if (!cancellationToken.IsCancellationRequested)
            {
                oldClient?.Shutdown(SocketShutdown.Both);
                oldClient?.Close();
            }
        } 
        #endregion
    }
}
