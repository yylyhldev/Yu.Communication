using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Yu.Communication.Server.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WSController : ControllerBase
    {
        private readonly ILogger<WSController> _logger;
        public IConfiguration Configuration { get; }
        public WSController(ILogger<WSController> logger, IConfiguration configuration)
        {
            _logger = logger;
            Configuration = configuration;
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public ActionResult OnlineCount()
        {
            return Ok(WebsocketHandler.OnlineCount());
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public ActionResult Onlines()
        {
            return Ok(WebsocketHandler.Onlines());
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> Offline(string key, CancellationToken cancellationToken = default)
        {
            var result = await WebsocketHandler.Offline(key, cancellationToken);
            return Ok(result);
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> SendMsgAll(string method, string msg, CancellationToken cancellationToken = default)
        {
            await WebsocketHandler.SendMsgAll(method, msg, cancellationToken);
            return Ok("ok");
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> SendMsg(string key, string method, string msg, CancellationToken cancellationToken = default)
        {
            var result = await WebsocketHandler.SendMsg(key, method, msg, cancellationToken);
            return Ok(result);
        }
    }
}
