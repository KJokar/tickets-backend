using Database.Enums.Identity;
using Infrastructure.Extension;
using Microsoft.AspNetCore.Mvc;

namespace TicketingApi
{
    [ApiController]
    public abstract class TicketingApiBaseController : ControllerBase
    {
        protected int CurrentUserId => User.GetUserId() ??
            throw new UnauthorizedAccessException("A valid user id claim is required.");

        protected UserRole CurrentUserRole
        {
            get
            {
                if (Enum.TryParse(
                        User.GetUserRole(),
                        ignoreCase: true,
                        out UserRole role) && Enum.IsDefined(role))
                {
                    return role;
                }

                throw new UnauthorizedAccessException("A valid user role claim is required.");
            }
        }
    }
}
