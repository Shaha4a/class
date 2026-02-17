using Dapper;

namespace ClassIn.Infrastructure.Data;

public static class DatabaseInitializer
{
    private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    email VARCHAR(200) NOT NULL,
    password_hash TEXT NOT NULL,
    role INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email ON users (email);

CREATE TABLE IF NOT EXISTS class_rooms (
    id SERIAL PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    teacher_id INTEGER NOT NULL,
    CONSTRAINT fk_class_rooms_teacher_id
        FOREIGN KEY (teacher_id)
        REFERENCES users (id)
        ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_class_rooms_teacher_id ON class_rooms (teacher_id);

CREATE TABLE IF NOT EXISTS class_members (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL,
    class_room_id INTEGER NOT NULL,
    CONSTRAINT fk_class_members_user_id
        FOREIGN KEY (user_id)
        REFERENCES users (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_class_members_class_room_id
        FOREIGN KEY (class_room_id)
        REFERENCES class_rooms (id)
        ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_class_members_user_id_class_room_id ON class_members (user_id, class_room_id);
CREATE INDEX IF NOT EXISTS ix_class_members_class_room_id ON class_members (class_room_id);

CREATE TABLE IF NOT EXISTS messages (
    id SERIAL PRIMARY KEY,
    class_room_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    text VARCHAR(2000) NOT NULL,
    sent_at TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    CONSTRAINT fk_messages_user_id
        FOREIGN KEY (user_id)
        REFERENCES users (id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_messages_class_room_id
        FOREIGN KEY (class_room_id)
        REFERENCES class_rooms (id)
        ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_messages_user_id ON messages (user_id);
CREATE INDEX IF NOT EXISTS ix_messages_class_room_id ON messages (class_room_id);
";

    public static async Task EnsureCreatedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var connectionFactory = services.GetRequiredService<ISqlConnectionFactory>();
        using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(SchemaSql, cancellationToken: cancellationToken));
    }
}

