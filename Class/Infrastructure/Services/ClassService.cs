using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Classes;
using ClassIn.Domain.Enums;
using ClassIn.Infrastructure.Data;
using Dapper;
using System.Data;

namespace ClassIn.Infrastructure.Services;

public sealed class ClassService(ISqlConnectionFactory connectionFactory, IConfiguration configuration) : IClassService
{
    private readonly string _videoRoomTemplate =
        configuration["Video:RoomUrlTemplate"] ?? "https://talky.io/classroom-{classId}";

    public async Task<ClassDto> CreateClassAsync(int teacherId, CreateClassRequestDto request, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var role = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT role FROM users WHERE id = @UserId;",
            new { UserId = teacherId },
            transaction,
            cancellationToken: cancellationToken));

        if (role is null)
        {
            throw new InvalidOperationException("User not found.");
        }

        if (role != (int)UserRole.Teacher)
        {
            throw new UnauthorizedAccessException("Only teachers can create classes.");
        }

        var created = await connection.QuerySingleAsync<ClassRoomRow>(new CommandDefinition(
            @"INSERT INTO class_rooms (name, teacher_id)
              VALUES (@Name, @TeacherId)
              RETURNING id, name, teacher_id AS teacherid;",
            new { Name = request.Name.Trim(), TeacherId = teacherId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO class_members (user_id, class_room_id)
              VALUES (@UserId, @ClassRoomId)
              ON CONFLICT (user_id, class_room_id) DO NOTHING;",
            new { UserId = teacherId, ClassRoomId = created.Id },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();

        return new ClassDto(created.Id, created.Name, created.TeacherId, BuildRoomUrl(created.Id));
    }

    public async Task JoinClassAsync(int userId, JoinClassRequestDto request, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM class_rooms WHERE id = @ClassId);",
            new { ClassId = request.ClassId },
            cancellationToken: cancellationToken));

        if (!exists)
        {
            throw new InvalidOperationException("Class not found.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO class_members (user_id, class_room_id)
              VALUES (@UserId, @ClassId)
              ON CONFLICT (user_id, class_room_id) DO NOTHING;",
            new { UserId = userId, ClassId = request.ClassId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<ClassDto>> GetUserClassesAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<ClassListRow>(new CommandDefinition(
            @"SELECT DISTINCT
                c.id AS id,
                c.name AS name,
                c.teacher_id AS teacherid
              FROM class_rooms c
              LEFT JOIN class_members cm ON cm.class_room_id = c.id
              WHERE c.teacher_id = @UserId OR cm.user_id = @UserId
              ORDER BY c.name;",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return rows.Select(x => new ClassDto(x.Id, x.Name, x.TeacherId, BuildRoomUrl(x.Id))).ToList();
    }

    public async Task<bool> IsMemberAsync(int userId, int classId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            @"SELECT EXISTS (
                SELECT 1
                FROM class_rooms c
                LEFT JOIN class_members cm ON cm.class_room_id = c.id
                WHERE c.id = @ClassId
                  AND (c.teacher_id = @UserId OR cm.user_id = @UserId)
              );",
            new { UserId = userId, ClassId = classId },
            cancellationToken: cancellationToken));
    }

    private string BuildRoomUrl(int classId)
    {
        return _videoRoomTemplate.Replace("{classId}", classId.ToString());
    }

    private sealed record ClassRoomRow(int Id, string Name, int TeacherId);
    private sealed record ClassListRow(int Id, string Name, int TeacherId);
}

