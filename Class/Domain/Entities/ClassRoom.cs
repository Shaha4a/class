namespace ClassIn.Domain.Entities;

public class ClassRoom
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TeacherId { get; set; }

    public User? Teacher { get; set; }
    public ICollection<ClassMember> Members { get; set; } = new List<ClassMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

