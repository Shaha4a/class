using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Messages;
using ClassIn.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClassIn.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class MessageController(IMessageService messageService) : ControllerBase
{
    [HttpGet("{classId:int}")]
    public async Task<ActionResult<IReadOnlyCollection<MessageDto>>> GetByClass(int classId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        return Ok(await messageService.GetMessagesAsync(classId, userId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<MessageDto>> Send([FromBody] SendMessageRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        return Ok(await messageService.SaveMessageAsync(userId, request, cancellationToken));
    }
}

