namespace ClassIn.Application.Dtos.Whiteboard;

public sealed record DrawEventDto(int ClassId, double X1, double Y1, double X2, double Y2, string Color, double LineWidth);

