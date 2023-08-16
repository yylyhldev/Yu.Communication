using MQTTnet;
using MQTTnet.Server;
using MQTTnet.Protocol;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Security.Authentication;
using System.Collections.Concurrent;

namespace Yu.Communication.Server
{
    public class MqttHandler
    {
        public static ConcurrentDictionary<string, string> ConnectionDic = new();
        /// <summary>
        /// 当前服务实例需忽略这条订阅消息
        /// </summary>
        public static ConcurrentQueue<string> SkipKickedOffline = new();
        /// <summary>
        /// 当前服务实例Id
        /// </summary>
        private static string ServerId => $"Server{Guid.NewGuid().ToString().Replace("-", string.Empty)}";
        private static MqttServer Server = null;

        private readonly ILogger<MqttHandler> _logger;
        private IConfiguration Configuration { get; }
        private FreeRedis.RedisClient[] _rdbs;
        /// <summary>
        /// 直接实例化方式：await new MqttHandler().StartServer();
        /// </summary>
        public MqttHandler()
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MqttHandler>();
            Configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
            _rdbs = RedisClients.Dbs;
        }
        /// <summary>
        /// 依赖注入方式
        /// </summary>
        /// <remarks>
        /// IServiceCollection.AddSingleton<MqttHandler>();
        /// private readonly MqttHandler _mqttHandler;
        /// public Worker(MqttHandler mqttHandler){ _mqttHandler = mqttHandler; }
        /// await _mqttHandler.StartServer();
        /// </remarks>
        public MqttHandler(ILogger<MqttHandler> logger, IConfiguration configuration, FreeRedis.RedisClient[] rdbs)
        {
            _logger = logger;
            Configuration = configuration;
            _rdbs = rdbs;
        }

        public async Task StartServer()
        {
            var mqttFactory = new MqttFactory();
            var mqttServerOptionsBuilder = mqttFactory.CreateServerOptionsBuilder();
            MqttServerOptionsBuilderOptions(mqttServerOptionsBuilder);
            var mqttServerOptions = mqttServerOptionsBuilder.Build();
            var server = mqttFactory.CreateMqttServer(mqttServerOptions);
            ConfigureHandler(server);
            await server.StartAsync();
            _logger.LogInformation($"MQTT Server 启动了[{mqttServerOptions.DefaultEndpointOptions.Port}/{mqttServerOptions.TlsEndpointOptions.Port}]");
        }
        public void MqttServerOptionsBuilderOptions(MqttServerOptionsBuilder mqttServerOptionsBuilder)
        {
            var conf = new PortCert
            {
                Port = Configuration.GetValue<int>("MqttPort"),
                PortSsl = Configuration.GetValue<int>("MqttPortSsl"),
                CertName = Configuration.GetValue<string>("CertName"),
                CertFile = Configuration.GetValue<string>("CertFile"),
                CertPwd = Configuration.GetValue<string>("CertPwd")
            };
            //mqttServerOptionsBuilder.WithoutDefaultEndpoint();//禁用默认的非SSL端口：1883
            //mqttServerOptionsBuilder.WithoutEncryptedEndpoint();//禁用默认的SSL端口：8883

            mqttServerOptionsBuilder.WithMaxPendingMessagesPerClient(1000);
            mqttServerOptionsBuilder.WithDefaultCommunicationTimeout(TimeSpan.FromMilliseconds(1000));

            mqttServerOptionsBuilder.WithDefaultEndpoint();
            mqttServerOptionsBuilder.WithDefaultEndpointPort(conf.Port);
            mqttServerOptionsBuilder.WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Any);

            if (!string.IsNullOrWhiteSpace(conf.CertName) || !string.IsNullOrWhiteSpace(conf.CertFile))
            {
                mqttServerOptionsBuilder.WithEncryptedEndpoint();
                mqttServerOptionsBuilder.WithEncryptedEndpointPort(conf.PortSsl);
                mqttServerOptionsBuilder.WithEncryptedEndpointBoundIPAddress(System.Net.IPAddress.Any);
                mqttServerOptionsBuilder.WithEncryptionSslProtocol(SslProtocols.Tls12);
                //mqttServerOptionsBuilder.WithRemoteCertificateValidationCallback(CertificateHelper.remoteCertValidationCallback);
                mqttServerOptionsBuilder.WithRemoteCertificateValidationCallback(new RemoteCertificateValidationCallback(CertificateHelper.ValidateRemoteCertificate));
                if (!string.IsNullOrWhiteSpace(conf.CertName))
                {
                    mqttServerOptionsBuilder.WithEncryptionCertificate(CertificateHelper.GetCertificateFromStore(conf.CertName));
                }
                else
                {
                    //mqttServerOptionsBuilder.WithEncryptionCertificate(CertificateHelper.GetCertificate2(conf.CertFile, conf.CertPwd));
                    mqttServerOptionsBuilder.WithEncryptionCertificate(CertificateHelper.GetCertificate(conf.CertFile, conf.CertPwd));
                }
            }
            Console.WriteLine($"MQTT Server Options [{conf.Port}/{conf.PortSsl}/{conf.CertFile}]");
        }

        public void ConfigureHandler(MqttServer mqtt)
        {
            Server = mqtt;
            KickedOffline();
            mqtt.StartedAsync += (EventArgs e) => 
            {
                _logger.LogInformation($"MQTT Server 已启动");
                return Task.CompletedTask;
            };
            mqtt.StoppedAsync += (EventArgs e) => 
            {
                _logger.LogInformation($"MQTT Server 已停止");
                return Task.CompletedTask;
            };
            mqtt.ValidatingConnectionAsync += ValidateConnectionAsync;
            mqtt.ClientConnectedAsync += OnClientConnected;
            mqtt.ClientDisconnectedAsync += OnClientDisconnected;

            mqtt.ClientSubscribedTopicAsync += OnClientSubscribedTopic;
            mqtt.ClientUnsubscribedTopicAsync += OnClientUnsubscribedTopic;

            mqtt.InterceptingPublishAsync += OnInterceptingPublish;//消息接收事件

            mqtt.ApplicationMessageNotConsumedAsync += (ApplicationMessageNotConsumedEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 消息未使用事件");
                return Task.CompletedTask;
            };
            mqtt.InterceptingSubscriptionAsync += (InterceptingSubscriptionEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 拦截订阅事件");
                return Task.CompletedTask;
            };
            mqtt.InterceptingUnsubscriptionAsync += (InterceptingUnsubscriptionEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 拦截取消订阅事件");
                return Task.CompletedTask;
            };
            mqtt.ClientAcknowledgedPublishPacketAsync += (ClientAcknowledgedPublishPacketEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 客户端确认发布包事件");
                return Task.CompletedTask;
            };
            mqtt.RetainedMessageChangedAsync += (RetainedMessageChangedEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 保留消息更改事件");
                return Task.CompletedTask;
            };
            mqtt.LoadingRetainedMessageAsync += (LoadingRetainedMessagesEventArgs e) => 
            {
                Console.WriteLine($"MQTT Server 加载保留消息事件");
                return Task.CompletedTask;
            };
        }

        #region 验证连接事件
        /// <summary>
        /// 验证连接事件
        /// </summary>
        public async Task ValidateConnectionAsync(ValidatingConnectionEventArgs con)
        {
            if (string.IsNullOrWhiteSpace(con.UserName) || string.IsNullOrWhiteSpace(con.Password))
            {
                con.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]MQTT validateNot[{con.ClientId}]");
                return;
            }
            con.ReasonCode = MqttConnectReasonCode.Success;
        }
        #endregion

        #region 连接事件
        /// <summary>
        /// 连接事件
        /// </summary>
        public async Task OnClientConnected(ClientConnectedEventArgs e)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]connected:[{e.ClientId}]({ConnectionDic.Count})");
            #region 同一个客户端以最后一个连接为准 
            //ConnectionDic.TryRemove(e.UserName, out var oldClient);
            //ConnectionDic[e.UserName] = e.ClientId;
            string oldClient = null;
            if (!ConnectionDic.TryAdd(e.UserName, e.ClientId))
            {
                oldClient = ConnectionDic[e.UserName];
                ConnectionDic[e.UserName] = e.ClientId;
            }
            if (!string.IsNullOrWhiteSpace(oldClient))
            {
                _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] 将Client {e.UserName}原连接踢下线]({ConnectionDic.Count})");
                await Server.DisconnectClientAsync(oldClient, MqttDisconnectReasonCode.NormalDisconnection);
            }
            else
            {
                SkipKickedOffline.Enqueue($"{ServerId}_off_{e.UserName}");//当前实例忽略这条订阅消息
                //await _rdbs[2].SetAsync($"{ServerId}_off_{e.UserName}", 0, 3);//当前实例忽略这条订阅消息
                await _rdbs[2].PublishAsync($"evt_KickedOff", e.UserName);
            }
            #endregion
            //return Task.CompletedTask;
        }
        #endregion

        #region 多实例/集群部署时：用Redis发布订阅实现踢下线
        /// <summary>
        /// 订阅频道---多实例/集群部署时：用Redis发布订阅实现踢下线
        /// </summary>
        /// <remarks>1.服务端实例订阅[频道A]；<br/>2.客户端上线时，当前服务实例先做检查，无重复的再向[频道A]发送客户端信息；<br/>3.其他订阅[频道A]的服务实例检查客户端信息，如有则踢下线；</remarks>
        public void KickedOffline()
        {
            _rdbs[2].Subscribe($"evt_KickedOff", async (chan, msg) =>
            {
                var key = msg + string.Empty;
                var has = SkipKickedOffline.TryDequeue(out _);
                //var has = await _rdbs[2].ExistsAsync($"{ServerId}_off_{key}");
                if (!has && ConnectionDic.TryRemove(key, out var oldClient))
                {
                    _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}] Client {key}已在别处上线]({ConnectionDic.Count})");
                    await Server.DisconnectClientAsync(oldClient, MqttDisconnectReasonCode.NormalDisconnection);
                }
            });
        }
        #endregion

        #region 断开连接事件
        /// <summary>
        /// 断开连接事件
        /// </summary>
        public Task OnClientDisconnected(ClientDisconnectedEventArgs e)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]disconnect[{e.ClientId}][{e.DisconnectType}]");
            return Task.CompletedTask;
        }
        #endregion

        #region 订阅事件
        /// <summary>
        /// 订阅事件
        /// </summary>
        public Task OnClientSubscribedTopic(ClientSubscribedTopicEventArgs e)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]subscribe[{e.ClientId}]：{e.TopicFilter.Topic}");
            return Task.CompletedTask;
        }
        #endregion

        #region 取消订阅事件
        /// <summary>
        /// 取消订阅事件
        /// </summary>
        public Task OnClientUnsubscribedTopic(ClientUnsubscribedTopicEventArgs e)
        {
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]unSubscribe[{e.ClientId}]：{e.TopicFilter}");
            return Task.CompletedTask;
        }
        #endregion

        #region 接收消息事件
        /// <summary>
        /// 接收消息事件
        /// </summary>
        public async Task OnInterceptingPublish(InterceptingPublishEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            var Qos = e.ApplicationMessage.QualityOfServiceLevel;
            var Retain = e.ApplicationMessage.Retain;
            _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]received>>ClientId：[{e.ClientId}] Topic：[{topic}] Payload：[{payload}] Qos：[{Qos}] Retain：[{Retain}]");
        }
        #endregion

        #region 发布消息
        public async Task<int> PublishAsync(string topic, string msg, bool retain)
        {
            var success = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(topic)) return success;
                if (topic == "all")
                {
                    var clients = await Server.GetClientsAsync();
                    var tasks = new List<Task>();
                    for (var i = 0; i < clients.Count; i++)
                    {
                        var applicationMessage = new MqttApplicationMessage
                        {
                            Topic = clients[i].Id,
                            PayloadSegment = System.Text.Encoding.UTF8.GetBytes(msg),
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.ExactlyOnce,
                            Retain = retain
                        };
                        tasks.Add(Server.InjectApplicationMessage(new InjectedMqttApplicationMessage(applicationMessage) { SenderClientId = ServerId }));
                        success++;
                    }
                    await Task.WhenAll(tasks.ToArray());
                }
                else
                {
                    var MessageBuilder = new MqttApplicationMessageBuilder()
                       .WithTopic(topic)
                       .WithPayload(msg)
                       .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                       .WithRetainFlag(retain)
                       .Build();

                    await Server.InjectApplicationMessage(new InjectedMqttApplicationMessage(MessageBuilder) { SenderClientId = ServerId });
                    success = 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"[{DateTime.Now:HH:mm:ss.fff}]", ex);
            }
            return success;
        }
        #endregion
    }
}
