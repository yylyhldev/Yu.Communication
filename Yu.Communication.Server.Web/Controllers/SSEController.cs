using Microsoft.AspNetCore.Mvc;

namespace Yu.Communication.Server.Web.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SSEController : ControllerBase
    {
        private readonly ILogger<SSEController> _logger;
        public IConfiguration Configuration { get; }
        public SSEController(ILogger<SSEController> logger, IConfiguration configuration)
        {
            _logger = logger;
            Configuration = configuration;
        }

        /// <summary>
        /// EventSource
        /// </summary>
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet]
        //[Produces("text/event-stream")]
        //[ApiExplorerSettings(IgnoreApi = true)]
        public async Task ServerSendEvents(CancellationToken cancellationToken = default)
        {
            int userId = 0;
            try
            {
                //http://localhost:7000/api/sse/ServerSendEvents
                Response.Headers.Append("Content-Type", "text/event-stream");
                //Response.Headers.Append("Cache-Control", "no-cache");
                //Response.Headers.Append("Connection", "keep-alive");
                var msg = string.Empty;
                var threadId = Environment.CurrentManagedThreadId;
                msg = $"data:{DateTime.Now} [{userId}:来了老弟] [{threadId}]\n\n";
                Console.WriteLine(msg);
                await Response.WriteAsync(msg, cancellationToken: cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                await Task.Delay(1000, cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    msg = $"data:{DateTime.Now} [{userId}:{DateTime.Now:HH:mm:ss.fff}] [{threadId}]\n\n";
                    Console.WriteLine(msg);
                    var byteData = System.Text.Encoding.Default.GetBytes(msg).AsMemory(0, msg.Length);
                    await Response.Body.WriteAsync(byteData, cancellationToken);
                    //await response.WriteAsync(msg, cancellationToken: cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    await Task.Delay(2000, cancellationToken);
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ServerSendEvents 出错");
            }
        }

        void TestSSE(int userId)
        {
            var scheme = $"http";
            var host = $"localhost";
            var port = 8002;
            //https://www.cnblogs.com/optimo/p/15221847.html
            #region WebClient方式
            var url = $"{scheme}://{host}:{port}/Trade/ServerSendEvents?userId={userId}";
            System.Net.WebClient web = new();
            web.OpenReadAsync(new Uri(url));
            web.OpenReadCompleted += async (object obj, System.Net.OpenReadCompletedEventArgs e) =>
            {
                using var sr = new StreamReader(e.Result);
                string line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    Console.WriteLine("收：" + line);
                }
            };
            #endregion
            while (true) { }
        }
    }
}
