using System.Data;
using Npgsql;

namespace ClassIn.Infrastructure.Data;

public sealed class NpgsqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

