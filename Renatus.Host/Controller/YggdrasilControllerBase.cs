using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
public abstract class YggdrasilControllerBase : ControllerBase
{
    protected ObjectResult YggdrasilError(YggdrasilException exception)
    {
        return StatusCode(exception.StatusCode, exception.ToResponse());
    }
}
