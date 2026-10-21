using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;
public sealed class BffOrganizationTests
{
    private sealed class Fixture:IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api=new(){CurrentRole="Admin"};
        public WebApplication App=null!;public InProcessServer Server=null!;public Browser Browser=null!;
        public readonly Guid Member=Guid.NewGuid(),Invitation=Guid.NewGuid(),Event=Guid.NewGuid(),Assignment=Guid.NewGuid(),Submission=Guid.NewGuid();
        public bool Owns=true,ForeignMember,ForeignInvitation,ForeignReport,WrongReview,WrongOwner,ForeignAudit;
        public long Denominator=6;public int? WriteStatus;public string WriteMessage="PRIVATE diagnostic";public bool Ambiguous;
        public string Metadata="{\"oldRole\":\"Student\",\"newRole\":\"Instructor\"}";
        public readonly List<(string Path,string Method,string Body)> Writes=[];public readonly List<string> Reads=[];
        public string Org=>"/bff/organization";public string Reports=>"/bff/staff/reports/courses";
        public static async Task<Fixture> Create(){var f=new Fixture();f.Api.StudentResponse=f.Respond;(f.App,f.Server)=await InProcessMvcTests.Host(f.Api);f.Browser=new(f.Server);f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App,f.Api,"Student"));return f;}
        private static HttpResponseMessage Data(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data=value})};
        private MemberView MemberRow()=>new(ForeignMember?Guid.NewGuid():Api.Organization,Member,2,"<script>Name</script>","Member","member@example.test");
        private object Course()=>new {courseId=Api.Course,organizationId=ForeignReport?Guid.NewGuid():Api.Organization,instructorId=Owns?Api.User:Guid.NewGuid(),title="<script>Course</script>",published=false,enrollments=3,active=1,completed=1,cancelled=1,publishedLessons=2,progressDenominator=Denominator,completedPublishedLearnerLessons=2,currentPercentage=33.33m,quizStarted=1,quizSubmitted=2,quizExpired=1,quizPassed=1,quizFailed=0,quizUnavailable=1,assignments=2,submissions=3,graded=1,ungraded=2};
        private AuditViewRow Audit()=>new(Event,DateTimeOffset.Parse("2026-10-10T00:00:00Z"),Api.User,ForeignAudit?Guid.NewGuid():Api.Organization,"organization","membership.role-changed","membership",Member,"succeeded",Guid.NewGuid().ToString("N"),Metadata);
        private HttpResponseMessage? Respond(HttpRequestMessage r)
        {
            var path=r.RequestUri!.AbsolutePath;var q=QueryHelpers.ParseQuery(r.RequestUri.Query);
            Assert.Equal(Api.AccessToken,r.Headers.Authorization!.Parameter);
            if(path=="/api/account/antiforgery"){var csrf=new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {token="SERVER-ONLY-CSRF"})};csrf.Headers.Add("Set-Cookie","api-csrf=SERVER-COOKIE; Secure; HttpOnly");return csrf;}
            Assert.Equal(Api.Organization.ToString(),Assert.Single(r.Headers.GetValues("X-Organization-Id")));
            if(r.Method!=HttpMethod.Get)
            {
                Writes.Add((path,r.Method.Method,r.Content?.ReadAsStringAsync().GetAwaiter().GetResult()??""));
                if(path.Contains("/invitations")){Assert.Equal("SERVER-ONLY-CSRF",Assert.Single(r.Headers.GetValues("X-CSRF-TOKEN")));Assert.Contains("api-csrf=SERVER-COOKIE",Assert.Single(r.Headers.GetValues("Cookie")));}
                if(Ambiguous)throw new HttpRequestException("PRIVATE uncertain outcome");
                if(WriteStatus.HasValue)return new((HttpStatusCode)WriteStatus.Value){Content=JsonContent.Create(new {message=WriteMessage})};
                if(path.EndsWith("/revoke"))return new(HttpStatusCode.OK){Content=new StringContent("")};
                return Data(new {token="MUST-NOT-RETURN",link="https://secret.invalid/",hash="MUST-NOT-RETURN"});
            }
            Reads.Add(r.RequestUri.PathAndQuery);int Page()=>q.TryGetValue("page",out var v)?int.Parse(v.ToString()):1;int Size()=>q.TryGetValue("pageSize",out var v)?int.Parse(v.ToString()):20;
            if(path==$"/api/organizations/{Api.Organization}/members")return Data(new AdministrationPage<MemberView>(Api.Organization,Page(),Size(),1,[MemberRow()]));
            if(path==$"/api/organizations/{Api.Organization}/members/{Member}")return Data(MemberRow());
            if(path==$"/api/Courses/{Api.Course}")return Data(new CourseView(Api.Course,"Course","",false,Owns?Api.User:Guid.NewGuid()));
            if(path==$"/api/Courses/{Api.Course}/sections")return Data(new[]{new SectionView(Api.Section,Api.Course,"Section",1)});
            if(path==$"/api/account/organizations/{Api.Organization}/invitations")return new(HttpStatusCode.OK){Content=JsonContent.Create(new {page=Page(),pageSize=Size(),total=1,items=new[]{new {id=Invitation,organizationId=ForeignInvitation?Guid.NewGuid():Api.Organization,email="invite@example.test",role=2,createdAt=DateTimeOffset.UtcNow,expiresAt=DateTimeOffset.UtcNow.AddDays(2),acceptedAt=(DateTimeOffset?)null,revokedAt=(DateTimeOffset?)null,deliveredAt=(DateTimeOffset?)null,token="SECRET",link="SECRET",hash="SECRET"}}})};
            if(path=="/api/Reports/courses")return Data(new {organizationId=Api.Organization,ownerUserId=(Guid?)null,page=Page(),pageSize=Size(),total=1,items=new[]{Course()}});
            if(path==$"/api/Reports/courses/{Api.Course}/learners")return Data(new {course=Course(),learners=new {organizationId=Api.Organization,ownerUserId=(Guid?)null,page=Page(),pageSize=Size(),total=1,items=new[]{new {courseId=Api.Course,userId=Member,firstName="Learner",lastName="Name",enrollmentStatus=2,enrollmentCompletedAt=DateTime.UtcNow,storedEnrollmentPercentage=100,publishedLessons=2,completedPublishedLessons=1,currentPercentage=50,currentContentComplete=false,recentAssignmentReview=new ReportSubmissionLink(Api.Course,Api.Section,Api.Lesson,Assignment,Submission)}}}});
            if(path==$"/api/audit/organizations/{Api.Organization}")return Data(new AuditViewPage(Api.Organization,Page(),Size(),1,new AuditViewFilter{FromUtc=DateTimeOffset.Parse("2026-10-01T00:00:00Z"),ToUtc=DateTimeOffset.Parse("2026-10-15T00:00:00Z")},[Audit()]));
            if(path==$"/api/audit/organizations/{Api.Organization}/{Event}")return Data(Audit());
            var a=$"/api/Lessons/{Api.Lesson}/assignments";
            if(path==a+"/context")return Data(new AssignmentContextView(new(Api.Lesson,WrongReview?Guid.NewGuid():Api.Section,Api.Course,Api.Organization,Owns?Api.User:Guid.NewGuid(),false,false),null));
            var row=new AssignmentView(Assignment,Api.Lesson,"Assignment","Work",null,10,false,DateTimeOffset.UtcNow,null,true);
            if(path==a)return Data(new[]{row});if(path==a+"/"+Assignment)return Data(row);
            if(path==a+$"/{Assignment}/submissions/{Submission}")return Data(new AssignmentSubmissionView(Submission,Assignment,WrongOwner?Guid.NewGuid():Member,null,false,null,DateTimeOffset.UtcNow,"Work",10,null));
            throw new InvalidOperationException("Unexpected organization test path "+path);
        }
        public async Task<InProcessServer.Response> Change(string path,object body,string method="POST")=>await Browser.Send(path,method,body,await Browser.Csrf());
        public async ValueTask DisposeAsync(){await App.DisposeAsync();Api.Dispose();}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Admin_and_explicit_selected_platform_reads_are_scoped_bounded_and_secret_free(bool platform)
    {
        await using var f=await Fixture.Create();f.Api.Platform=platform;
        foreach(var path in new[]{f.Org+"/members?page=1&pageSize=20",f.Org+"/invitations",f.Org+$"/courses/{f.Api.Course}/instructor",f.Org+"/audit",f.Org+"/audit/"+f.Event,f.Reports,f.Reports+$"/{f.Api.Course}/learners"})
        {var r=await f.Browser.Send(path);Assert.Equal(200,r.Status);Assert.Contains("no-store",r.Headers.CacheControl.ToString());Assert.DoesNotContain(f.Api.AccessToken,r.Body);Assert.DoesNotContain("SECRET",r.Body);Assert.DoesNotContain("SERVER-ONLY-CSRF",r.Body);}
        Assert.Empty(f.Writes);
    }
    [Theory][InlineData("Student")][InlineData("Instructor")]
    public async Task Members_invitations_and_audit_deny_nonadmins_using_live_role(string role)
    {
        await using var f=await Fixture.Create();f.Api.CurrentRole=role;
        foreach(var path in new[]{f.Org+"/members",f.Org+"/invitations",f.Org+"/audit",f.Org+"/audit/"+f.Event})Assert.Equal(403,(await f.Browser.Send(path)).Status);
        Assert.Equal(403,(await f.Change(f.Org+"/members",new {email="exact@example.test",role=1,confirmed=true})).Status);Assert.Empty(f.Writes);
    }
    [Fact] public async Task Scope_mismatches_and_foreign_records_are_rejected_without_counts_or_payloads()
    {
        await using var f=await Fixture.Create();f.Api.ContextMismatch=true;Assert.Equal(403,(await f.Browser.Send(f.Org+"/audit")).Status);f.Api.ContextMismatch=false;
        f.ForeignMember=true;Assert.Equal(502,(await f.Browser.Send(f.Org+"/members")).Status);f.ForeignMember=false;
        f.ForeignInvitation=true;var invite=await f.Browser.Send(f.Org+"/invitations");Assert.Equal(502,invite.Status);Assert.DoesNotContain("invite@example",invite.Body);
        f.ForeignReport=true;Assert.Equal(403,(await f.Browser.Send(f.Reports)).Status);f.ForeignReport=false;
        f.ForeignAudit=true;Assert.Equal(502,(await f.Browser.Send(f.Org+"/audit/"+f.Event)).Status);f.Api.InactiveOrganization=true;f.Api.OrganizationDenied=true;Assert.Equal(403,(await f.Browser.Send(f.Org+"/members")).Status);Assert.Empty(f.Writes);
    }
    [Fact] public async Task Membership_writes_require_exact_email_CSRF_confirmation_and_existing_scoped_member()
    {
        await using var f=await Fixture.Create();var form=new {email="exact@example.test",role=1,confirmed=true};
        Assert.Equal(400,(await f.Browser.Send(f.Org+"/members","POST",form)).Status);
        Assert.Equal(400,(await f.Change(f.Org+"/members",new {email="Name <exact@example.test>",role=1,confirmed=true})).Status);
        Assert.Equal(400,(await f.Change(f.Org+"/members",new {email="exact@example.test",role=1,confirmed=false})).Status);Assert.Empty(f.Writes);
        var r=await f.Change(f.Org+"/members",form);Assert.Equal(204,r.Status);Assert.Empty(r.Body);
        Assert.Equal(204,(await f.Change(f.Org+"/members/"+f.Member,new {role=3,confirmed=true},"PUT")).Status);
        Assert.Equal(new[]{"POST","PUT"},f.Writes.Select(x=>x.Method));Assert.DoesNotContain("userId",f.Writes[0].Body);f.ForeignMember=true;
        Assert.Equal(404,(await f.Change(f.Org+"/members/"+f.Member,new {role=1,confirmed=true},"PUT")).Status);Assert.Equal(2,f.Writes.Count);
    }
    [Theory][InlineData("The last organization Admin cannot be demoted. Assign another Admin first.")][InlineData("Reassign all courses before changing this Instructor's role.")][InlineData("Resolve all Active enrollments before changing this Student's role.")][InlineData("Account unavailable or membership conflicts with current state.")]
    public async Task Existing_role_safeguards_are_relayed_as_safe_conflicts_without_replay(string message)
    {
        await using var f=await Fixture.Create();f.WriteStatus=409;f.WriteMessage=message;var r=await f.Change(f.Org+"/members/"+f.Member,new {role=1,confirmed=true},"PUT");Assert.Equal(409,r.Status);Assert.Equal(message,Json(r).GetProperty("message").GetString());Assert.Single(f.Writes);
    }
    [Fact] public async Task Instructor_reassignment_revalidates_stored_membership_and_course()
    {
        await using var f=await Fixture.Create();Assert.Equal(204,(await f.Change(f.Org+$"/courses/{f.Api.Course}/instructor",new {instructorId=f.Member,confirmed=true},"PUT")).Status);
        Assert.Equal($"/api/Courses/{f.Api.Course}/instructor",Assert.Single(f.Writes).Path);f.ForeignMember=true;
        Assert.Equal(404,(await f.Change(f.Org+$"/courses/{f.Api.Course}/instructor",new {instructorId=f.Member,confirmed=true},"PUT")).Status);Assert.Single(f.Writes);
    }
    [Fact] public async Task Invitations_use_two_independent_CSRF_boundaries_and_never_return_tokens_or_links()
    {
        await using var f=await Fixture.Create();Assert.Equal(400,(await f.Browser.Send(f.Org+"/invitations","POST",new {email="invite@example.test",role=2,confirmed=true})).Status);
        var r=await f.Change(f.Org+"/invitations",new {email="invite@example.test",role=2,reissue=true,confirmed=true});Assert.Equal(204,r.Status);Assert.Empty(r.Body);Assert.DoesNotContain("SERVER",r.Headers.SetCookie.ToString());
        Assert.True(JsonDocument.Parse(f.Writes.Last().Body).RootElement.GetProperty("reissue").GetBoolean());
        Assert.Equal(204,(await f.Change(f.Org+"/invitations/"+f.Invitation+"/revoke",new {confirmed=true})).Status);
        Assert.DoesNotContain("SECRET",(await f.Browser.Send(f.Org+"/invitations")).Body);
    }
    [Theory][InlineData("unknown")][InlineData("duplicate")][InlineData("value")]
    public async Task Audit_metadata_is_a_per_action_allowlist_not_a_raw_payload(string invalid)
    {
        await using var f=await Fixture.Create();f.Metadata=invalid switch {"unknown"=>"{\"email\":\"SECRET\"}","duplicate"=>"{\"newRole\":\"Admin\",\"newRole\":\"Admin\"}",_=>"{\"newRole\":\"SuperAdmin\"}"};
        var r=await f.Browser.Send(f.Org+"/audit/"+f.Event);Assert.Equal(502,r.Status);Assert.DoesNotContain("SECRET",r.Body);Assert.DoesNotContain("SuperAdmin",r.Body);
    }
    [Fact] public async Task Audit_UTC_filters_and_pagination_are_bounded_and_allowlisted()
    {
        await using var f=await Fixture.Create();foreach(var query in new[]{"?page=0","?pageSize=51","?action=arbitrary","?fromUtc=2026-09-01T00:00:00Z&toUtc=2026-10-15T00:00:00Z","?fromUtc=2026-10-01T00:00:00%2B05:30&toUtc=2026-10-15T00:00:00Z"})Assert.Equal(400,(await f.Browser.Send(f.Org+"/audit"+query)).Status);
        var r=await f.Browser.Send(f.Org+"/audit?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-15T00:00:00Z&action=membership.role-changed&outcome=succeeded");Assert.Equal(200,r.Status);Assert.Equal(JsonValueKind.Object,Json(r).GetProperty("items")[0].GetProperty("metadata").ValueKind);Assert.Empty(f.Writes);
    }
    [Fact] public async Task Report_filters_counts_and_current_vs_historical_values_are_preserved()
    {
        await using var f=await Fixture.Create();f.Api.CurrentRole="Instructor";
        var r=await f.Browser.Send(f.Reports+"?page=2&pageSize=20&published=false&enrollmentStatus=3&search=Course");Assert.Equal(200,r.Status);var row=Json(r).GetProperty("courses").GetProperty("items")[0];Assert.Equal("6",row.GetProperty("progressDenominator").GetString());Assert.Equal(3,row.GetProperty("enrollments").GetInt32());Assert.Contains(f.Reads,x=>x.Contains("enrollmentStatus=3")&&x.Contains("published=false"));
        var learner=Json(await f.Browser.Send(f.Reports+$"/{f.Api.Course}/learners")).GetProperty("report").GetProperty("learners").GetProperty("items")[0];Assert.Equal(100,learner.GetProperty("storedEnrollmentPercentage").GetInt32());Assert.Equal(50,learner.GetProperty("currentPercentage").GetInt32());Assert.False(learner.GetProperty("currentContentComplete").GetBoolean());
        f.Denominator=9007199254740993;Assert.Equal("9007199254740993",Json(await f.Browser.Send(f.Reports)).GetProperty("courses").GetProperty("items")[0].GetProperty("progressDenominator").GetString());
        f.Owns=false;Assert.Equal(403,(await f.Browser.Send(f.Reports)).Status);Assert.Equal(403,(await f.Browser.Send(f.Reports+$"/{f.Api.Course}/learners")).Status);Assert.Empty(f.Writes);
    }
    [Fact] public async Task Assessment_links_require_stored_nested_parents_and_the_report_learner_owner()
    {
        await using var f=await Fixture.Create();var path=f.Reports+$"/{f.Api.Course}/learners/{f.Member}/assignment-review?sectionId={f.Api.Section}&lessonId={f.Api.Lesson}&assignmentId={f.Assignment}&submissionId={f.Submission}";
        Assert.Equal(200,(await f.Browser.Send(path)).Status);f.WrongReview=true;Assert.Equal(404,(await f.Browser.Send(path)).Status);f.WrongReview=false;f.WrongOwner=true;Assert.Equal(404,(await f.Browser.Send(path)).Status);Assert.Empty(f.Writes);
    }
    [Fact] public async Task BFF_and_upstream_rate_limits_remain_sanitized_JSON()
    {
        await using var f=await Fixture.Create();f.WriteStatus=429;var form=new {email="exact@example.test",role=1,confirmed=true};
        Assert.Equal(429,(await f.Change(f.Org+"/members",form)).Status);f.WriteStatus=null;
        for(var i=1;i<10;i++)Assert.Equal(400,(await f.Change(f.Org+"/members",new {email="exact@example.test",role=1,confirmed=false})).Status);
        var limited=await f.Change(f.Org+"/members",form);Assert.Equal(429,limited.Status);Assert.Equal("rate_limited",Json(limited).GetProperty("code").GetString());Assert.DoesNotContain("PRIVATE",limited.Body);Assert.Single(f.Writes);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Delivery_or_transport_uncertainty_does_not_replay_or_expose_diagnostics(bool delivery)
    {
        await using var f=await Fixture.Create();f.Ambiguous=!delivery;f.WriteStatus=delivery?503:null;
        var r=await f.Change(f.Org+"/invitations",new {email="invite@example.test",role=1,confirmed=true});Assert.Equal(502,r.Status);Assert.DoesNotContain("PRIVATE",r.Body);await f.Browser.Send(f.Org+"/invitations");Assert.Single(f.Writes);
    }
    [Fact] public async Task Unselected_platform_session_cannot_select_a_tenant_by_header_or_query()
    {
        await using var f=await Fixture.Create();f.Api.Platform=true;await f.Browser.Send("/bff/login","POST",new {email="admin@example.test",password="password"},await f.Browser.Csrf());
        Assert.Equal(403,(await f.Server.Send(f.Org+"/audit?organizationId="+f.Api.Organization,cookie:f.Browser.Cookie,organizationHeader:f.Api.Organization.ToString())).Status);Assert.Empty(f.Writes);
    }
}
