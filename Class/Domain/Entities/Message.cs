namespace ClassIn.Domain.Entities;

public class Message
{
    public int Id { get; set; }
    public int ClassRoomId { get; set; }
    public int UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }

    public ClassRoom? ClassRoom { get; set; }
    public User? User { get; set; }
}

