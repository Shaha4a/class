namespace ClassIn.Application.Dtos.Auth;

public sealed record AuthResponseDto(string Token, int UserId, string Name, string Email, int Role);

