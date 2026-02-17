using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Classes;
using ClassIn.Domain.Enums;
using ClassIn.Infrastructure.Data;
using Dapper;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Data;

namespace ClassIn.Infrastructure.Services;

public sealed class ClassService(ISqlConnectionFactory connectionFactory, IConfiguration configuration) : IClassService
{
    private static readonly HttpClient _httpClient = new();
    private const string BbbProvider = "BigBlueButton";
    private const string AttendeePassword = "ap";
    private const string ModeratorPassword = "mp";

    private readonly string _videoProvider = configuration["Video:Provider"] ?? string.Empty;
    private readonly string _bbbUrl = configuration["Video:BigBlueButton:Url"] ?? string.Empty;
    private readonly string _bbbSecret = configuration["Video:BigBlueButton:Secret"] ?? string.Empty;
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

        var roomUrl = await BuildRoomUrlAsync(
            userId: teacherId,
            userDisplayName: $"Teacher {teacherId}",
            classId: created.Id,
            className: created.Name,
            isModerator: true,
            cancellationToken);

        return new ClassDto(created.Id, created.Name, created.TeacherId, roomUrl);
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

        var result = new List<ClassDto>();
        foreach (var row in rows)
        {
            var isModerator = row.TeacherId == userId;
            var roomUrl = await BuildRoomUrlAsync(
                userId: userId,
                userDisplayName: $"User {userId}",
                classId: row.Id,
                className: row.Name,
                isModerator: isModerator,
                cancellationToken);

            result.Add(new ClassDto(row.Id, row.Name, row.TeacherId, roomUrl));
        }

        return result;
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

    private async Task<string> BuildRoomUrlAsync(
        int userId,
        string userDisplayName,
        int classId,
        string className,
        bool isModerator,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_videoProvider, BbbProvider, StringComparison.OrdinalIgnoreCase))
        {
            return _videoRoomTemplate.Replace("{classId}", classId.ToString(CultureInfo.InvariantCulture));
        }

        if (string.IsNullOrWhiteSpace(_bbbUrl) || string.IsNullOrWhiteSpace(_bbbSecret))
        {
            throw new InvalidOperationException("BigBlueButton is enabled, but Url/Secret are not configured.");
        }

        await EnsureBbbMeetingAsync(classId, className, cancellationToken);

        var joinPassword = isModerator ? ModeratorPassword : AttendeePassword;
        return BuildBbbJoinUrl(classId, userId, userDisplayName, joinPassword);
    }

    private async Task EnsureBbbMeetingAsync(int classId, string className, CancellationToken cancellationToken)
    {
        var meetingId = $"class-{classId}";
        var queryParams = new Dictionary<string, string>
        {
            ["meetingID"] = meetingId,
            ["name"] = className,
            ["attendeePW"] = AttendeePassword,
            ["moderatorPW"] = ModeratorPassword
        };

        var uri = BuildBbbApiUri("create", queryParams);
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        var status = document.Root?.Element("returncode")?.Value;

        if (!string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            var message = document.Root?.Element("message")?.Value ?? "Unknown BigBlueButton error.";
            throw new InvalidOperationException($"BigBlueButton create meeting failed: {message}");
        }
    }

    private string BuildBbbJoinUrl(int classId, int userId, string userDisplayName, string password)
    {
        var fullName = string.IsNullOrWhiteSpace(userDisplayName) ? $"User {userId}" : userDisplayName;
        var queryParams = new Dictionary<string, string>
        {
            ["meetingID"] = $"class-{classId}",
            ["fullName"] = fullName,
            ["password"] = password,
            ["redirect"] = "true"
        };

        return BuildBbbApiUri("join", queryParams).ToString();
    }

    private Uri BuildBbbApiUri(string apiCall, IReadOnlyDictionary<string, string> queryParams)
    {
        var encoded = queryParams
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}");
        var queryString = string.Join("&", encoded);
        var checksum = ComputeSha1($"{apiCall}{queryString}{_bbbSecret}");

        var baseUrl = _bbbUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/api/{apiCall}?{queryString}&checksum={checksum}");
    }

    private static string ComputeSha1(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var hashBytes = SHA1.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed record ClassRoomRow(int Id, string Name, int TeacherId);
    private sealed record ClassListRow(int Id, string Name, int TeacherId);
}

