using Microsoft.AspNetCore.Mvc;

namespace ModManager.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MyController : ControllerBase
    {
        [HttpGet(Name = "Get")]
        public IEnumerable<string> Get()
        {
            return [];
        }
    }
}
