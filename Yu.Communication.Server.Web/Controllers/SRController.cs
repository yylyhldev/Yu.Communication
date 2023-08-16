using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Yu.Communication.Server.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SRController : ControllerBase
    {
        private readonly ILogger<SRController> _logger;
        public IConfiguration Configuration { get; }
        //private readonly IHubContext<SingalRHandler> _hubContext;//using Microsoft.AspNetCore.SignalR;
        private readonly SingalRHandler _msgContext;
        public SRController(ILogger<SRController> logger, IConfiguration configuration, SingalRHandler msgContext)
        {
            _logger = logger;
            Configuration = configuration;
            //_hubContext = hubContext;
            _msgContext = msgContext;
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public ActionResult OnlineCount()
        {
            return Ok(_msgContext.OnlineCount());
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public ActionResult Onlines()
        {
            return Ok(_msgContext.Onlines());
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> Offline(int uid, CancellationToken cancellationToken = default)
        {
            var result = await _msgContext.Offline(uid, cancellationToken);
            return Ok(result);
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> SendMsgAll(string method, string msg, CancellationToken cancellationToken = default)
        {
            await _msgContext.SendMsgAll(method, msg, cancellationToken);
            return Ok("ok");
        }

        [AllowAnonymous]
        //[ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("[action]")]
        public async Task<ActionResult> SendMsg(int uid, string method, string msg, CancellationToken cancellationToken = default)
        {
            var result = await _msgContext.SendMsg(uid, method, msg, cancellationToken);
            return Ok(result);
        }
    }
}