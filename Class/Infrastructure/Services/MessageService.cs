using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Messages;
using ClassIn.Infrastructure.Data;
using Dapper;

namespace ClassIn.Infrastructure.Services;

public sealed class MessageService(ISqlConnectionFactory connectionFactory, IClassService classService) : IMessageService
{
    public async Task<MessageDto> SaveMessageAsync(int userId, SendMessageRequestDto request, CancellationToken cancellationToken = default)
    {
        var isMember = await classService.IsMemberAsync(userId, request.ClassId, cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("You are not a member of this class.");
        }

        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var userName = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT name FROM users WHERE id = @UserId;",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("User not found.");
        }

        var inserted = await connection.QuerySingleAsync<MessageRow>(new CommandDefinition(
            @"INSERT INTO messages (class_room_id, user_id, text, sent_at)
              VALUES (@ClassId, @UserId, @Text, @SentAt)
              RETURNING id, class_room_id AS classid, user_id AS userid, text, sent_at AS sentat;",
            new
            {
                ClassId = request.ClassId,
                UserId = userId,
                Text = request.Text.Trim(),
                SentAt = DateTime.UtcNow
            },
            cancellationToken: cancellationToken));

        return new MessageDto(inserted.Id, inserted.ClassId, inserted.UserId, userName, inserted.Text, inserted.SentAt);
    }

    public async Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(int classId, int userId, CancellationToken cancellationToken = default)
    {
        var isMember = await classService.IsMemberAsync(userId, classId, cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("You are not a member of this class.");
        }

        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<MessageDto>(new CommandDefinition(
            @"SELECT
                m.id AS id,
                m.class_room_id AS classid,
                m.user_id AS userid,
                u.name AS username,
                m.text AS text,
                m.sent_at AS sentat
              FROM messages m
              JOIN users u ON u.id = m.user_id
              WHERE m.class_room_id = @ClassId
              ORDER BY m.sent_at;",
            new { ClassId = classId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private sealed record MessageRow(int Id, int ClassId, int UserId, string Text, DateTime SentAt);
}

