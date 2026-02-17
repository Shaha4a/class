namespace ClassIn.Application.Dtos.Auth;

public sealed record RegisterRequestDto(string Name, string Email, string Password, int Role);

