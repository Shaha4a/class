using ClassIn.Application.Contracts;
using ClassIn.Application.Dtos.Auth;
using ClassIn.Domain.Entities;
using ClassIn.Domain.Enums;
using ClassIn.Infrastructure.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ClassIn.Infrastructure.Services;

public sealed class AuthService(ISqlConnectionFactory connectionFactory, IConfiguration configuration) : IAuthService
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @Email);",
            new { Email = normalizedEmail },
            cancellationToken: cancellationToken));

        if (exists)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        if (!Enum.IsDefined(typeof(UserRole), request.Role))
        {
            throw new InvalidOperationException("Role must be 1 (Student) or 2 (Teacher).");
        }

        var role = (UserRole)request.Role;
        var user = new User
        {
            Name = request.Name.Trim(),
            Email = normalizedEmail,
            Role = role
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        var created = await connection.QuerySingleAsync<UserRow>(new CommandDefinition(
            @"INSERT INTO users (name, email, password_hash, role)
              VALUES (@Name, @Email, @PasswordHash, @Role)
              RETURNING id, name, email, role;",
            new
            {
                Name = user.Name,
                Email = user.Email,
                PasswordHash = user.PasswordHash,
                Role = (int)role
            },
            cancellationToken: cancellationToken));

        return BuildAuthResponse(created.Id, created.Name, created.Email, created.Role);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserWithHashRow>(new CommandDefinition(
            @"SELECT id, name, email, role, password_hash AS passwordhash
              FROM users
              WHERE email = @Email;",
            new { Email = normalizedEmail },
            cancellationToken: cancellationToken));

        if (user is null)
        {
            throw new InvalidOperationException("Invalid email or password.");
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(
            new User
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = (UserRole)user.Role,
                PasswordHash = user.PasswordHash
            },
            user.PasswordHash,
            request.Password);

        if (verifyResult == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException("Invalid email or password.");
        }

        return BuildAuthResponse(user.Id, user.Name, user.Email, user.Role);
    }

    private AuthResponseDto BuildAuthResponse(int userId, string name, string email, int role)
    {
        var token = GenerateJwt(userId, name, email, role);
        return new AuthResponseDto(token, userId, name, email, role);
    }

    private string GenerateJwt(int userId, string name, string email, int role)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.");
        var issuer = configuration["Jwt:Issuer"] ?? "ClassInClassMvp";
        var audience = configuration["Jwt:Audience"] ?? "ClassInClassClient";

        var roleName = Enum.IsDefined(typeof(UserRole), role) ? ((UserRole)role).ToString() : UserRole.Student.ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, roleName)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed record UserRow(int Id, string Name, string Email, int Role);
    private sealed record UserWithHashRow(int Id, string Name, string Email, int Role, string PasswordHash);
}

