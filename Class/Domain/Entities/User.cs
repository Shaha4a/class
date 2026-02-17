using ClassIn.Domain.Enums;

namespace ClassIn.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public ICollection<ClassMember> ClassMemberships { get; set; } = new List<ClassMember>();
    public ICollection<ClassRoom> OwnedClasses { get; set; } = new List<ClassRoom>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

