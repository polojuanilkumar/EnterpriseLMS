using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace LMS.Infrastructure.Persistence;

// Defense in depth for EF bulk/raw writes, which do not pass through SaveChanges.
// This covers generated SQL statement shapes; it is not a security boundary against a DBA.
public sealed class AuditCommandGuard : DbCommandInterceptor
{
    public static void Validate(string sql)
    {
        foreach(var statement in sql.Split(';'))
            if(Regex.IsMatch(statement,@"(?:\[AuditEvents\]|\bAuditEvents\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)
                &&Regex.IsMatch(statement,@"\b(?:UPDATE|DELETE|MERGE|TRUNCATE|DROP|ALTER)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Audit events are append-only.");
    }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command,CommandEventData data,InterceptionResult<int> result){Validate(command.CommandText);return result;}
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken token=default){Validate(command.CommandText);return ValueTask.FromResult(result);}
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result){Validate(command.CommandText);return result;}
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default){Validate(command.CommandText);return ValueTask.FromResult(result);}
    public override InterceptionResult<object> ScalarExecuting(DbCommand command,CommandEventData data,InterceptionResult<object> result){Validate(command.CommandText);return result;}
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<object> result,CancellationToken token=default){Validate(command.CommandText);return ValueTask.FromResult(result);}
}
