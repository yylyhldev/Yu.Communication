namespace Yu.Communication.Server.WorkerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private IConfiguration Configuration { get; }
        private readonly MqttHandler _mqttHandler;
        private readonly SocketIOHandler _socketIOHandler;
        private readonly SuperSocketHandler _superSocketHandler;
        private readonly SocketHandler _socketHandler;

        public Worker(ILogger<Worker> logger, IConfiguration configuration, 
            MqttHandler mqttHandler, 
            SocketIOHandler socketIOHandler, 
            SuperSocketHandler superSocketHandler, 
            SocketHandler socketHandler)
        {
            _logger = logger;
            _mqttHandler = mqttHandler;
            Configuration = configuration;
            _socketIOHandler = socketIOHandler;
            _superSocketHandler = superSocketHandler;
            _socketHandler = socketHandler;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _socketHandler.StartServer(stoppingToken);

            await _superSocketHandler.StartServer(stoppingToken);

            //await new MqttHandler().StartServer();
            await _mqttHandler.StartServer();

            //await new SocketIOHandler().StartServer(stoppingToken);
            await _socketIOHandler.StartServer(stoppingToken);

            await Task.Delay(500, stoppingToken);
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            //    await Task.Delay(1000, stoppingToken);
            //}
        }
    }
}