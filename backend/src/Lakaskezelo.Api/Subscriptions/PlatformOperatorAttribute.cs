using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lakaskezelo.Api.Subscriptions;

// Csak a platform üzemeltetőjének (Platform:OperatorCompanyId cég) adminisztrátorai — a platform
// beállításai (díjak, Barion/Billingo kulcsok) és a cégek áttekintése.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PlatformOperatorAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var companyId = services.GetRequiredService<ICompanyContext>().CompanyId;
        var isOperator = services.GetRequiredService<PlatformSettingsService>().IsOperator(companyId);
        if (!isOperator || !context.HttpContext.User.IsInRole(nameof(UserRole.Admin)))
        {
            context.Result = new ObjectResult(new { message = "Ehhez a platform üzemeltetőjének jogosultsága kell." }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
