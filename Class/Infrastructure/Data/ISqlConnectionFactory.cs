using System.Data;

namespace ClassIn.Infrastructure.Data;

public interface ISqlConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

