using LMS.Application.Features.Assignments;
using LMS.Application.Interfaces.Lessons;
namespace LMS.Infrastructure.Storage;

// Same validated staging mechanics; separate directory and separate authorization endpoints.
public sealed class AssignmentStorage(string root,string webRoot):IAssignmentStorage
{
    private readonly LocalPrivateMediaStorage inner=new(root,webRoot);
    public Task<IStagedMedia> StageAsync(Guid id,Stream source,long limit,CancellationToken token)=>inner.StageAsync(id,source,limit,token);
    public Task<Stream> OpenReadAsync(Guid id,CancellationToken token)=>inner.OpenReadAsync(id,token);
}
