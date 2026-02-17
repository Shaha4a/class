namespace ClassIn.Application.Dtos.Messages;

public sealed record MessageDto(int Id, int ClassId, int UserId, string UserName, string Text, DateTime SentAt);

