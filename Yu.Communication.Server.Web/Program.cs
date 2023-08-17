using System.Text;
using Yu.Communication.Server;
using Yu.Communication.Server.Web;

var builder = WebApplication.CreateBuilder(args);

//builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), reloadOnChange: true, optional: false);
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", reloadOnChange: true, optional: false);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

RedisClients.Initialization(builder.Configuration["Redis:ConnectionString"], builder.Services);//for SignalR+WebSocket

#region SignalR 注册&鉴权jwt
//context.Request.Headers["Authorization"]
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        NameClaimType = IdentityModel.JwtClaimTypes.Id,//HttpContext.User.Identity.Name值源自ClaimsIdentity.JwtClaimTypes.Id;
        RoleClaimType = IdentityModel.JwtClaimTypes.Role,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes("jwtSecretKey")),
        ValidateIssuerSigningKey = true,
        ValidIssuer = "jwtIssuer",
        ValidAudience = "jwtAudience",
        ValidateIssuer = true,
        ValidateAudience = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(60)
    };
});
builder.Services.AddSignalR();//.AddMessagePackProtocol();//Microsoft.AspNetCore.SignalR.Protocols.MessagePack
builder.Services.AddSingleton<SingalRHandler>();//api
#endregion
#region mqtt 注册，监听端口
builder.Services.AddSingleton<MqttHandler>();//方式一
////方式二
//builder.Services.AddMqttServer(new MqttHandler().MqttServerOptionsBuilderOptions).AddMqttTcpServerAdapter().AddConnections();
#endregion
builder.Services.AddSingleton<SocketIOHandler>();//SocketIO 注册
#region SuperSocket
builder.Services.AddSingleton<SuperSocketHandler>();//SuperSocket 注册
//var ssHost = WebSocketHostBuilder.Create()
//    .UseWebSocketMessageHandler(async (session, message) =>
//    {
//    await session.SendAsync(message.Message);
//    })
//    .ConfigureAppConfiguration((hostCtx, configApp) =>
//    {
//        configApp.AddInMemoryCollection(new Dictionary<string, string> {
//            { "serverOptions:name", "SuperSocketServer.Web" },
//            { "serverOptions:listeners:0:ip", "Any" },
//            { "serverOptions:listeners:0:port", "4040" }
//        });
//    })
//    .ConfigureLogging((hostCtx, loggingBuilder) => loggingBuilder.AddConsole())
//    .Build();
//await ssHost.RunAsync(); 
#endregion
builder.Services.AddSingleton<SocketHandler>();//Socket 注册

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
//app.UseAuthorization();

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "Hello World!");

#region WebSockets [http+https]
app.UseWebSockets();
app.Map(WebsocketHandler.Pattern, builder => builder.UseMiddleware<WebsocketHandler>());
#endregion
#region SingalR 启用&鉴权Middleware [http+https]
app.MapHub<SingalRHandler>(SingalRHandler.Pattern);
//app.UseMiddleware<SingalRAuthorizationMiddleware>();//鉴权需与 builder.Services.AddJwtConfigure 二选一
#endregion

#region mqtt 启用 [http+https]
//方式一
await app.Services.GetRequiredService<MqttHandler>().StartServer();
////方式二 
//app.MapMqtt("/mqtt");
////app.MapConnectionHandler<MqttConnectionHandler>("/mqtt", options => config.WebSockets.SubProtocolSelector = protocolList => protocolList.FirstOrDefault() ?? string.Empty);
//app.UseMqttServer(mqtt => mqttServerHandler.ConfigureHandler(mqtt));
#endregion

#region SocketIO 启用 [http+https]
app.UseServ<SocketIOHandler>(async socketIO => await socketIO.StartServer(app.Lifetime.ApplicationStopping));
//var socketIO = app.Services.GetRequiredService<SocketIOHandler>();
//await socketIO.StartServer(app.Lifetime.ApplicationStopping);
#endregion

#region SuperSocket 启用 [http+https]
app.UseServ<SuperSocketHandler>(async superSocket => await superSocket.StartServer(app.Lifetime.ApplicationStopping));
//var superSocket = app.Services.GetRequiredService<SuperSocketHandler>();
//await superSocket.StartServer(app.Lifetime.ApplicationStopping);
#endregion

#region Socket 启用 [http+https]
app.UseServ<SocketHandler>(async socket => await socket.StartServer(app.Lifetime.ApplicationStopping));
//var socket = app.Services.GetRequiredService<SocketHandler>();
//await socket.StartServer(app.Lifetime.ApplicationStopping);
#endregion

app.Run();


public static class AppBuilderExtensions
{
    /// <summary>
    /// configure(app.ApplicationServices.GetRequiredService&lt;ServiceType&gt;());
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="app"></param>
    /// <param name="configure"></param>
    /// <returns>IApplicationBuilder</returns>
    public static IApplicationBuilder UseServ<T>(this IApplicationBuilder app, Action<T> configure)
    {
        var server = app.ApplicationServices.GetRequiredService<T>();
        configure(server);
        return app;
    }
}