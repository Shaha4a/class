using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Classes;
using ClassIn.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClassIn.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ClassController(IClassService classService) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ClassDto>> Create([FromBody] CreateClassRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var classRoom = await classService.CreateClassAsync(userId, request, cancellationToken);
        return Ok(classRoom);
    }

    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinClassRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        await classService.JoinClassAsync(userId, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyCollection<ClassDto>>> MyClasses(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        return Ok(await classService.GetUserClassesAsync(userId, cancellationToken));
    }
}

