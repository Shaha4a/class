using ClassIn.Application.Dtos.Classes;

namespace ClassIn.Application.Contracts;

public interface IClassService
{
    Task<ClassDto> CreateClassAsync(int teacherId, CreateClassRequestDto request, CancellationToken cancellationToken = default);
    Task JoinClassAsync(int userId, JoinClassRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ClassDto>> GetUserClassesAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(int userId, int classId, CancellationToken cancellationToken = default);
}

