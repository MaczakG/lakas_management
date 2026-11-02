using Lakaskezelo.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lakaskezelo.Api.Subscriptions;

// Csak a Platform admin külön fiókjával (PlatformAdmin, platform-token) — a cégek felhasználói soha nem
// érik el, akármilyen szerepkörük van a cégükben.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PlatformOperatorAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.IsInRole(PlatformAuthController.PlatformOperatorRole))
        {
            context.Result = new ObjectResult(new { message = "Ehhez Platform admin belépés kell." }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
