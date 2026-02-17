using ClassIn.Application.Dtos.Messages;

namespace ClassIn.Application.Contracts;

public interface IMessageService
{
    Task<MessageDto> SaveMessageAsync(int userId, SendMessageRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(int classId, int userId, CancellationToken cancellationToken = default);
}

