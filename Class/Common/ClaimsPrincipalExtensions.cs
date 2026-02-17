using System.Security.Claims;

namespace ClassIn.Common;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Missing user id claim.");

        if (!int.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user id claim.");
        }

        return userId;
    }
}

