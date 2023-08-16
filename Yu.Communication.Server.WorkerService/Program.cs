global using Yu.Communication.Server;
using Yu.Communication.Server.WorkerService;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<SocketHandler>();
        services.AddSingleton<SuperSocketHandler>();
        services.AddSingleton<SocketIOHandler>();
        services.AddSingleton<MqttHandler>();
        services.AddHostedService<Worker>();
    })
    .Build();
var configuration = host.Services.GetRequiredService<IConfiguration>();
RedisClients.Initialization(configuration["Redis:ConnectionString"], new ServiceCollection());

//var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

//#region Socket Æô¶¯
//var socket = host.Services.GetRequiredService<SocketHandler>();
//await socket.StartServer(cancellationToken);
//#endregion

//#region SuperSocket Æô¶¯
//var superSocket = host.Services.GetRequiredService<SuperSocketHandler>();
//await superSocket.StartServer(cancellationToken); 
//#endregion

//#region SocketIO Æô¶¯
//var socketIO = host.Services.GetRequiredService<SocketIOHandler>();
//await socketIO.StartServer(cancellationToken);
//#endregion
await host.RunAsync();