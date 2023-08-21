// See https://aka.ms/new-console-template for more information
using MQTTnet.Client;
using MQTTnet;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.WebSockets;
using System.Text;
using SocketIOSharp.Client;
using SocketIOSharp.Common;
using EngineIOSharp.Common.Enum;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using System.Net;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration;
using System.Security.Authentication;
using Yu.Communication.Server;

Console.WriteLine("Hello, World!");
await Task.Delay(3000);
Console.WriteLine("开始了......");
var tokenVal = "eyJhbGciOiJodHRwOi8vd3d3LnczLm9yZy8yMDAxLzA0L3htbGRzaWctbW9yZSNobWFjLXNoYTI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6IjkwMjU1RDA3ODVGRUFDOTdGMkFGRjU0OEE5RjdENEQyOTg4MzY4NjAiLCJuYW1lIjoiQzBGRkU3N0ExM0NGMUMwREQ3NTYyQTdEQUVCQTk2QUM2NTYwOTYiLCJwaG9uZV9udW1iZXIiOiJDMEZGRTc3QTEzQ0YxQzBERDc1NjJBN0RBRUJBOTZBQzY1NjA5NiIsIm5iZiI6MTY4NDczMTkxOSwiZXhwIjoxNzEwNjUxOTE5LCJpc3MiOiJodHRwczovL2xvY2FsaG9zdDo0NDM3NiIsImF1ZCI6Imh0dHBzOi8vbG9jYWxob3N0OjQ0Mzc2In0.EmpsSWxLUIi5LZqkMRyicktkRB30nwrRhD6KXcxiNzE";

DateTime authTime = DateTime.Now;
var SigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes("jwtSecretKey0000000000"));
var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
    issuer: "https://localhost:8080",
    audience: "https://localhost:8080",
    claims: new System.Security.Claims.Claim[1] { new System.Security.Claims.Claim(IdentityModel.JwtClaimTypes.Expiration, authTime.AddMinutes(5).ToString()) },
    notBefore: authTime,
    expires: authTime.AddMinutes(5),
    signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(SigningKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
);
tokenVal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
//tokenVal += "222";
var Configuration = new ConfigurationBuilder().Add(new JsonConfigurationSource { Path = "appsettings.json", ReloadOnChange = true }).Build();
var host = Configuration.GetValue<string>("CommunicationServers:Host") + string.Empty;
var certName = Configuration.GetValue<string>("CommunicationServers:CertName");
var useSsl = !string.IsNullOrWhiteSpace(certName);
var portSsl = useSsl ? "Ssl" : null;
Console.WriteLine($"开始[{host} | {useSsl} | {certName}]");

//await BatchExec(1, async () => await TestWebSocket(host, useSsl, Configuration.GetValue<int>($"CommunicationServers:WebPort{portSsl}"), tokenVal));//WebSocket
//await BatchExec(1, async () => await TestSignalR(host, useSsl, Configuration.GetValue<int>($"CommunicationServers:WebPort{portSsl}"), tokenVal));//SignalR
//await BatchExec(1, async () => await TestMqtt(host, useSsl, Configuration.GetValue<int>($"CommunicationServers:MqttPort{portSsl}"), tokenVal));//Mqtt
//await BatchExec(1, () => TestSocketIO(host, useSsl, Configuration.GetValue<ushort>($"CommunicationServers:SocketIOPort{portSsl}")));//SocketIO

//await BatchExec(1, async () => await TestSocketTcpClient(host, useSsl, Configuration.GetValue<int>($"CommunicationServers:SuperSocketPort{portSsl}")));//SuperSocket
//await BatchExec(1, async () => await TestSocketTcpClient(host, useSsl, Configuration.GetValue<int>($"CommunicationServers:SocketPort{portSsl}")));//Socket
Thread.Sleep(1000);
Console.ReadLine();
Environment.Exit(0);

async Task BatchExec(int count, Action act)
{
    for (int i = 0; i < count; i++)
    {
        await Task.Delay(1000);
        Console.WriteLine(string.Empty);
        await Task.Factory.StartNew(() => act(), TaskCreationOptions.LongRunning);
    }
}

#region WebSocket
async Task TestWebSocket(string host, bool useSsl, int serverPort, string tokenVal)
{
    using (var webSocketClient = new ClientWebSocket())
    {
        webSocketClient.Options.AddSubProtocol("Sec-WebSocket-Protocol");
        webSocketClient.Options.SetRequestHeader("Sec-WebSocket-Protocol", "Bearer " + tokenVal);
        if (useSsl)
        {
            //var certCommonName = certName.Split(",")[0].Split("=")[1];
            //webSocketClient.Options.ClientCertificates = CertificateHelper.GetCertificatesFromStore(certCommonName, false);
            webSocketClient.Options.RemoteCertificateValidationCallback = CertificateHelper.ValidateRemoteCertificate2;
        }
        var url = $"{(useSsl ? "wss" : "ws")}://{host}:{serverPort}/ws?token={tokenVal}";
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} WebSocket 连接服务端......：{url}");
        await webSocketClient.ConnectAsync(new Uri(url), CancellationToken.None);
        while (webSocketClient.State == WebSocketState.Open)
        {
            //Thread.Sleep(1000);
            //await webSocketClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "再见", CancellationToken.None);
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = null;
            try
            {
                result = await webSocketClient.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var msg = Encoding.UTF8.GetString(buffer);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    string wsMsg = result.CloseStatusDescription;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} WebSocket Client 收到关闭消息：{wsMsg}");
                    if (webSocketClient.State == WebSocketState.CloseReceived) await webSocketClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    break;
                }
                msg = (msg + "").TrimEnd('\0');
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} WebSocket Client 收：{msg}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} WebSocket Client 出错[{webSocketClient.State}]:{ex.Message}", ex);
                if (webSocketClient.State != WebSocketState.Aborted) await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                Thread.Sleep(1000);
            }
            //string message = Console.ReadLine();
            //string message = "999";
            //byte[] requestData = Encoding.UTF8.GetBytes(message);
            //var requestBuffer = new ArraySegment<byte>(requestData);
            //await webSocketClient.SendAsync(requestBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
#endregion

#region SignalR
async Task TestSignalR(string host, bool useSsl, int serverPort, string tokenVal)
{
    var url = $"http{(useSsl ? "s" : null)}://{host}:{serverPort}/signalr";
    var signalRClient = new HubConnectionBuilder()
    //.WithUrl($"{url}?token=Bearer {tokenVal}")
    .WithUrl(url, options =>
    {
        //options.Headers.Add("Authorization", $"Bearer {tokenVal}");
        options.AccessTokenProvider = () => Task.FromResult(tokenVal);//等效 Authorization: Bearer 123
        if (useSsl)
        {
            var certCommonName = certName.Split(",")[0].Split("=")[1];
            options.ClientCertificates = CertificateHelper.GetCertificatesFromStore(certCommonName, false);
            var x509Certificate = options.ClientCertificates[0];
            //var x509Certificate = new X509Certificate2(new FileInfo("certificate.cer").FullName);
            #region 针对https连接的ssl证书验证,此配置必须
            options.HttpMessageHandlerFactory = (msgHandler) =>
            {
                if (msgHandler is HttpClientHandler httpClientHandler)
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback += (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        return x509Certificate.Equals(certificate);//判断服务器的公开证书是否和客户端自带的证书相同
                    };
                }
                return msgHandler;
            };
            #endregion
            #region 针对websocket连接的ssl证书验证,使用websocket连接时必须配置
            options.WebSocketConfiguration = (websocketOption) =>
            {
                websocketOption.RemoteCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) =>
                {
                    return x509Certificate.Equals(certificate);//判断服务器的公开证书是否和客户端自带的证书相同
                };
            }; 
            #endregion
        }
    })
    .Build();
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 开始[{signalRClient.State}] {url}");
    SignalREventListener(signalRClient);
    await signalRClient.StartAsync();
    if (signalRClient.State == HubConnectionState.Connected)
    {
        await signalRClient.SendAsync("ToServer", "message", "我来也");
    }
    await Task.Delay(60000);
    await signalRClient.DisposeAsync();
}
void SignalREventListener(HubConnection signalRClient)
{
    signalRClient.On("connected", (object arg) =>
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 上线成功[{arg}]");
    });
    signalRClient.On("close", async (object arg) =>
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 收到关闭消息[{arg}]");
        await signalRClient.DisposeAsync();
    });
    signalRClient.Closed += (Exception? e) =>
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 监测到关闭[{e?.Message}]");
        return Task.CompletedTask;
    };
    signalRClient.Reconnecting += (Exception? e) =>
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 重连中......[{e?.Message}]");
        return Task.CompletedTask;
    };
    signalRClient.Reconnected += (string? msg) =>
    {
        SignalREventListener(signalRClient);
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} signalRClient 已重连[{msg}]");
        return Task.CompletedTask;
    };
}
#endregion

#region Mqtt
async Task TestMqtt(string host, bool useSsl, int serverPort, string tokenVal)
{
    var mqttFactory = new MqttFactory();
    using (var mqttClient = mqttFactory.CreateMqttClient())
    {
        MqttEventListener(mqttClient);
        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(host, serverPort)
            //.WithWebSocketServer($"{host}:{serverPort}/mqtt")
            .WithClientId("aaaaaaa")
            .WithCredentials("Authorization", tokenVal)
            .WithTls(new MqttClientOptionsBuilderTlsParameters
            {
                UseTls = useSsl,
                AllowUntrustedCertificates = true,
                IgnoreCertificateChainErrors = true,
                IgnoreCertificateRevocationErrors = true,
                CertificateValidationHandler = ValidateMqttServerCertificate,
                Certificates = new[] {
                            new X509Certificate2("D:\\Deploy\\domain.com.pfx", "123456"),
                            new X509Certificate(@"D:\\Deploy\\\domain.com_bundle.crt")
                    },
                SslProtocol = SslProtocols.Tls12
            })
            .Build();//1883//8100
        await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);
        await Task.Delay(500);
        var applicationMessage = new MqttApplicationMessage
        {
            Topic = "aaa",
            PayloadSegment = Encoding.UTF8.GetBytes("sdhsdkghsdg"),
            QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
            Retain = false
        };
        if (mqttClient.IsConnected) await mqttClient?.PublishAsync(applicationMessage, CancellationToken.None);
        await Task.Delay(50000);
        // This will send the DISCONNECT packet. Calling _Dispose_ without DisconnectAsync the 
        // connection is closed in a "not clean" way. See MQTT specification for more details.
        if (mqttClient.IsConnected) await mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build());
    }
}
void MqttEventListener(IMqttClient mqttClient)
{
    mqttClient.ConnectingAsync += (MqttClientConnectingEventArgs e) =>
    {
        Console.WriteLine("连接中......");
        return Task.CompletedTask;
    };
    mqttClient.ConnectedAsync += (MqttClientConnectedEventArgs e) =>
    {
        Console.WriteLine($"Mqtt Server已连接：{e.ConnectResult.MaximumQoS}");
        return Task.CompletedTask;
    };
    mqttClient.DisconnectedAsync += (MqttClientDisconnectedEventArgs e) =>
    {
        Console.WriteLine($"Mqtt Server已断开：{e.Reason},{e.ReasonString}");
        return Task.CompletedTask;
    };
    mqttClient.ApplicationMessageReceivedAsync += (MqttApplicationMessageReceivedEventArgs e) =>
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
        var Qos = e.ApplicationMessage.QualityOfServiceLevel;
        var Retain = e.ApplicationMessage.Retain;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]收到Mqtt Server消息>>Topic：[{topic}] Payload：[{payload}] Qos：[{Qos}] Retain：[{Retain}]");
        return Task.CompletedTask;
    };
}
bool ValidateMqttServerCertificate(MqttClientCertificateValidationEventArgs args)
{
    if (args.SslPolicyErrors == SslPolicyErrors.None)
        return true;
    Console.WriteLine($"MQTT Client Ignore Certificate error: {args.SslPolicyErrors}");
    return true;//允许该客户端与未经身份验证的服务器通信。
}
#endregion

#region SocketIO
void TestSocketIO(string host, bool useSsl, ushort serverPort)
{
    try
    {
        useSsl = false;
        var ExtraHeaders = new System.Collections.Generic.Dictionary<string, string>()
    {
        { "token", "tokenVal-" },
        { "matchId", "123456-" }
    };
        RemoteCertificateValidationCallback zs = (a, b, c, d) => true;
        zs = new RemoteCertificateValidationCallback(CertificateHelper.ValidateRemoteCertificate);
        X509CertificateCollection? certificates = null;
        if (useSsl)
        {
            var certCommonName = certName.Split(",")[0].Split("=")[1];
            certificates = CertificateHelper.GetCertificatesFromStore(certCommonName, false);
        }
        var options = new SocketIOClientOption(useSsl ? EngineIOScheme.https : EngineIOScheme.http, host, serverPort, ServerCertificateValidationCallback: zs, ClientCertificates: certificates, Reconnection: false, ExtraHeaders: ExtraHeaders);
        var socketIoClient = new SocketIOClient(options);
        socketIoClient.On("close", () =>
        {
            Console.WriteLine($"SocketIO>close-server通知断开");
            socketIoClient.Close();//Client-SocketIOEvent.DISCONNECT：EngineIOSharp.Common.EngineIOException: Forced close
        });
        socketIoClient.On(SocketIOEvent.CONNECTING, (data) =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>连接中{socketIoClient}，{data == Array.Empty<JToken>()}");
        });
        socketIoClient.On(SocketIOEvent.CONNECTION, (data) =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>已连上{socketIoClient}，{data == Array.Empty<JToken>()}");
            socketIoClient.Emit("auth", new object[] { "tokenval" + DateTime.Now.Ticks, 123 });
            socketIoClient.Emit("message", "我来也");
        });
        socketIoClient.On(SocketIOEvent.DISCONNECT, (data) =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>Disconnected：[{data[0]}]");
            if (data[0].Contains("Forced close"))
            {
                socketIoClient.Dispose();//Client-SocketIOEvent.DISCONNECT：EngineIOSharp.Common.EngineIOException: Forced close
            }
        });
        socketIoClient.On(SocketIOEvent.ERROR, (JToken[] data) =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>Error：[{data[0]}]");
        });
        socketIoClient.On("message", (data) =>
        {
            foreach (JToken token in data)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}>SocketIO>message：" + (token.Type == JTokenType.Bytes ? BitConverter.ToString(token.ToObject<byte[]>()) : token));
            }
        });
        socketIoClient.Connect();
    }
    catch (Exception ex)
    {
        Console.WriteLine(11);
    }
}
#endregion

#region Socket/SuperSocket-TcpClient
async Task TestSocketTcpClient(string host, bool useSsl, int serverPort)
{
    if (!useSsl)
    {
        await TestSocketNonSsl(host, serverPort);
        return;
    }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Connecting......");
    //var client = new TcpClient(host, serverPort);
    var client = new TcpClient();
    var ipaddr = IPAddress.Loopback;
    _ = IPAddress.TryParse(host, out ipaddr);
    await client.ConnectAsync(new IPEndPoint(ipaddr, serverPort));
    while (!client.Connected) { continue; }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Connected.");

    #region 用于向远程主机读写数据的流
    var stream = client.GetStream();
    var sslStream = useSsl ? new SslStream(stream, false, userCertificateValidationCallback: CertificateHelper.ValidateRemoteCertificate) : null;
    try
    {
        if (useSsl)
        {
            var certCommonName = certName.Split(",")[0].Split("=")[1];
            //certCommonName = "TestSocketServer";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} 证书公用名[{certCommonName}]");
            X509CertificateCollection? certificates = null;
            certificates = CertificateHelper.GetCertificatesFromStore(certCommonName, false);
            //var cerFilePath = Path.Combine("D:", "Deploy", "SSL", $"{certCommonName.Replace("*.", string.Empty).Replace(".cer", string.Empty)}.cer");
            //Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{cerFilePath}]");
            //certificates = new X509CertificateCollection() { X509Certificate.CreateFromCertFile(cerFilePath) };//从导出证书文件里加载证书
            if (certificates == null || certificates.Count == 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} 单向认证：只认证服务器端的合法性");
                await sslStream.AuthenticateAsClientAsync(certCommonName);//单向认证：只认证服务器端的合法性
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} 双向认证：同时认证服务端和客户端");
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} 证书[{certificates.Count},{certificates[0].Subject}]");
                await sslStream.AuthenticateAsClientAsync(certCommonName, certificates, SslProtocols.Tls12, true);//双向认证：同时认证服务端和客户端
                var sslAuthOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = certCommonName,
                    ClientCertificates = certificates,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = CertificateHelper.ValidateRemoteCertificate,
                };
                //await sslStream?.AuthenticateAsClientAsync(sslAuthOptions);//双向认证：同时认证服务端和客户端
            }
        }
    }
    catch (AuthenticationException ex)
    {
        Console.WriteLine("AuthenticationException: {0}", ex.Message);
        if (ex.InnerException != null)
        {
            Console.WriteLine("Inner AuthenticationException: {0}", ex.InnerException.Message);
        }
        Console.WriteLine("Authentication failed - closing the connection.");
        sslStream?.Close();
        stream?.Close();
        client.Close();
        client.Dispose();
        return;
    }
    catch (Exception ex)
    {
        Console.WriteLine("Exception: {0}", ex.Message);
        if (ex.InnerException != null)
        {
            Console.WriteLine("Inner Exception: {0}", ex.InnerException.Message);
        }
        sslStream?.Close();
        stream?.Close();
        client.Close();
        client.Dispose();
        return;
    }
    #endregion

    #region 异步接收消息
    new Thread(SocketReceiveTcpClient) { IsBackground = true }.Start(new Tuple<TcpClient, SslStream>(client, sslStream));
    //await Task.Factory.StartNew(async () =>
    //{
    //    await SocketReceiveTcpClientAsync(client, useSsl);
    //    sslStream?.Close();
    //    stream?.Close();
    //    client.Close();
    //    client.Dispose();
    //}, TaskCreationOptions.LongRunning);
    #endregion
    #region 发消息
    await Task.Delay(100);
    if (!client.Connected) return;
    var sendMsg = $"LOGIN aaa3 ppp3";
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Send [{sendMsg}] {client.Client.LocalEndPoint}");
    if (useSsl) await sslStream.WriteAsync(Encoding.UTF8.GetBytes($"{sendMsg} {client.Client.LocalEndPoint}\r\n"));
    else await stream.WriteAsync(Encoding.UTF8.GetBytes($"{sendMsg} {client.Client.LocalEndPoint}\r\n"));
    await Task.Delay(100);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Send MSG hello world {client.Client.LocalEndPoint}");
    if (useSsl) await sslStream.WriteAsync(Encoding.UTF8.GetBytes($"MSG hello world {client.Client.LocalEndPoint}\r\n"));
    else await stream.WriteAsync(Encoding.UTF8.GetBytes($"MSG hello world {client.Client.LocalEndPoint}\r\n"));
    //await Task.Delay(50000);
    //if (client.Connected)
    //{
    //    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 主动断开连接.");
    //    client?.Close();
    //    client?.Dispose();
    //} 
    #endregion
}

async void SocketReceiveTcpClient(object obj)
{
    var client = (Tuple<TcpClient, SslStream>)obj;
    await SocketReceiveTcpClientAsync(client.Item1, client.Item2);
}
async Task SocketReceiveTcpClientAsync(TcpClient client, SslStream? sslStream = null)
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 开始异步接收消息-SocketReceive");
    byte[] buffer = new byte[1024];
    var stream = client.GetStream();// 创建用于发送和接受数据的NetworkStream
    while (client.Connected)
    {
        try
        {
            var received = sslStream != null ? await sslStream.ReadAsync(buffer) : await stream.ReadAsync(buffer);
            if (received <= 0) continue;
            var response = Encoding.Default.GetString(buffer);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 收: {response}");
            if (response == "<|ACK|>")
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 收 acknowledgment: \"{response}\"");
                break;
            }
        }
        catch (Exception ex)
        {
            var baseException = ex.GetBaseException();
            if (baseException is not TaskCanceledException && baseException is not SocketException)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 服务器异常:{ex.Message}[{client?.Connected}]");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Server 关掉了:{ex.Message}[{client?.Connected}]");
            }
            break;
        }
    }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 连接关闭，停止接收消息:{client?.Connected}.");
    sslStream?.Close();
    stream?.Close();
    client?.Close();
    client?.Dispose();
}
#endregion
#region Socket/SuperSocket-Socket
async Task TestSocketNonSsl(string host, int serverPort)
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Connecting......");
    var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, serverPort));
    while (!client.Connected) { continue; }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Connected.[{host}:{serverPort}");
    #region 异步接收消息
    var thread = new Thread(SocketReceive) { IsBackground = true };
    thread.Start(client);
    //await Task.Factory.StartNew(async () =>
    //{
    //    await SocketReceiveAsync(client);
    //    client.Shutdown(SocketShutdown.Both);
    //    client.Close();
    //}, TaskCreationOptions.LongRunning);
    #endregion
    await Task.Delay(1000);
    //发消息
    var sendMsg = $"LOGIN aaa ppp";
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Send [{sendMsg}] {client.LocalEndPoint}");
    await client.SendAsync(Encoding.UTF8.GetBytes($"{sendMsg} {client.LocalEndPoint}\r\n"));
    await Task.Delay(100);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client Send MSG hello world {client.LocalEndPoint}");
    await client.SendAsync(Encoding.UTF8.GetBytes($"MSG hello world {client.LocalEndPoint}\r\n"));
    //await Task.Delay(50000);
    //if (client.Connected)
    //{
    //    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 主动断开连接.");
    //    client?.Shutdown(SocketShutdown.Both);
    //    client?.Close();
    //}
}
async void SocketReceive(object obj)
{
    await SocketReceiveAsync(obj as Socket);
}
async Task SocketReceiveAsync(Socket client)
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 开始异步接收消息-SocketReceive");
    byte[] buffer = new byte[1024];
    //Socket client = obj as Socket;
    while (client.Connected)
    {
        try
        {
            int received = await client.ReceiveAsync(buffer);
            if (received <= 0) continue;
            var response = Encoding.Default.GetString(buffer);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 收: [{client.Connected}]{response}");
            if (response == "<|ACK|>")
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 收 acknowledgment: {response}");
                break;
            }
        }
        catch (Exception ex)
        {
            var baseException = ex.GetBaseException();
            if (baseException is not TaskCanceledException && baseException is not SocketException)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 服务器异常:{ex.Message}.[{client?.Connected}]");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Server 关掉了:{ex.Message}.[{client?.Connected}]");
                client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
            break;
        }
    }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Socket Client 连接关闭，停止接收消息:{client?.Connected}.");
}
#endregion

