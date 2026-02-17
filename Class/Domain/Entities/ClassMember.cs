namespace ClassIn.Domain.Entities;

public class ClassMember
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ClassRoomId { get; set; }

    public User? User { get; set; }
    public ClassRoom? ClassRoom { get; set; }
}

