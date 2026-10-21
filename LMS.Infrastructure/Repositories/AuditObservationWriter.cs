using LMS.Application.Features.Auditing;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
namespace LMS.Infrastructure.Repositories;
public sealed class AuditObservationWriter(IServiceScopeFactory scopes) : IAuditObservationWriter
{
    public async Task AppendDelivery(AuditEvent observedEvent,CancellationToken token)
    {
        if(observedEvent.Action!=AuditEvent.Code(AuditAction.InvitationDelivery))throw new ArgumentException("Only external delivery observations use this writer.");
        observedEvent.Validate();
        await using var scope=scopes.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<LMSDbContext>();
        db.AuditEvents.Add(observedEvent);
        await db.SaveChangesAsync(token); // Clean transaction/context, entirely separate from the email/business transaction.
    }
}
