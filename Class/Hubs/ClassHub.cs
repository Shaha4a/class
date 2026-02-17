using System.Collections.Concurrent;
using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Messages;
using ClassIn.Application.Dtos.Whiteboard;
using ClassIn.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ClassIn.Hubs;

[Authorize]
public sealed class ClassHub(IMessageService messageService, IClassService classService) : Hub
{
    private static readonly ConcurrentDictionary<string, (int UserId, int ClassId)> Connections = new();

    public override async Task OnConnectedAsync()
    {
        var classIdRaw = Context.GetHttpContext()?.Request.Query["classId"].ToString();
        if (int.TryParse(classIdRaw, out var classId))
        {
            var userId = Context.User!.GetUserId();
            if (await classService.IsMemberAsync(userId, classId))
            {
                Connections[Context.ConnectionId] = (userId, classId);
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(classId));
                await Clients.Group(GroupName(classId)).SendAsync("UserOnline", userId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Connections.TryRemove(Context.ConnectionId, out var state))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(state.ClassId));
            await Clients.Group(GroupName(state.ClassId)).SendAsync("UserOffline", state.UserId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinClass(int classId)
    {
        var userId = Context.User!.GetUserId();
        if (!await classService.IsMemberAsync(userId, classId))
        {
            throw new HubException("You are not a member of this class.");
        }

        Connections[Context.ConnectionId] = (userId, classId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(classId));
        await Clients.Group(GroupName(classId)).SendAsync("UserOnline", userId);
    }

    public async Task SendMessage(int classId, string text)
    {
        var userId = Context.User!.GetUserId();
        var message = await messageService.SaveMessageAsync(userId, new SendMessageRequestDto(classId, text));
        await Clients.Group(GroupName(classId)).SendAsync("ReceiveMessage", message);
    }

    public async Task Draw(DrawEventDto drawEvent)
    {
        var userId = Context.User!.GetUserId();
        if (!await classService.IsMemberAsync(userId, drawEvent.ClassId))
        {
            throw new HubException("You are not a member of this class.");
        }

        await Clients.OthersInGroup(GroupName(drawEvent.ClassId)).SendAsync("Draw", drawEvent);
    }

    private static string GroupName(int classId) => $"class-{classId}";
}

