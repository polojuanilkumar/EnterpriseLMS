using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// Runs only after Program's exact container ownership and loopback binding checks.
internal static class AssessmentAcceptance
{
    public static async Task Run(string root, JsonElement state, JsonElement secrets)
    {
        string S(string k) => state.GetProperty(k).GetString()!;
        string Secret(string k) => secrets.GetProperty(k).GetString()!;
        using var exclusive = new FileStream(Path.Combine(root,"assessment-acceptance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var cs = new SqlConnectionStringBuilder { DataSource="127.0.0.1,"+S("SqlPort"),InitialCatalog="LMS_Smoke_"+S("Run"),UserID="sa",Password=Secret("SqlPassword"),Encrypt=SqlConnectionEncryptOption.Strict,ServerCertificate=Path.Combine(root,"sql.pem"),HostNameInCertificate="localhost",Pooling=false,ConnectTimeout=5 };
        await using (var sql = new SqlConnection(cs.ConnectionString))
        {
            await sql.OpenAsync();
            await using var cmd=sql.CreateCommand(); cmd.CommandText="SELECT DB_NAME()";
            if ((string?)await cmd.ExecuteScalarAsync()!=cs.InitialCatalog) throw new Exception("Wrong disposable database.");
        }
        var apiUri=new Uri(S("ApiUrl"));
        if(apiUri.Scheme!="https"||apiUri.Host!="localhost")throw new Exception("Unexpected API origin.");
        var report=Path.Combine(root,"assessment-acceptance-http.txt");
        var results=File.Exists(report)?File.ReadAllLines(report).ToList():new List<string>();
        void Record(string line) { results.Add(line); File.WriteAllLines(report,results); Console.WriteLine(line); }
        Record($"Acceptance pass {DateTimeOffset.UtcNow:O}: owned disposable database verified with strict pinned TLS; HTTP observations follow. No browser execution in this report.");
        async Task<(HttpClient Client,string User)> Login(string email)
        {
            var c=new HttpClient { BaseAddress=apiUri,Timeout=TimeSpan.FromSeconds(30) };
            using var r=await c.PostAsJsonAsync("/api/auth/login",new{email,password=Secret("LoginPassword")});
            if(!r.IsSuccessStatusCode)throw new Exception("Synthetic login failed: "+(int)r.StatusCode);
            var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",j.GetProperty("accessToken").GetString());
            var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"fixtures.json"))).RootElement;
            c.DefaultRequestHeaders.Add("X-Organization-Id",fixture.GetProperty("orgA").GetString());
            return(c,j.GetProperty("userId").GetString()!);
        }
        async Task<JsonElement> Request(HttpClient c,string method,string path,object? body=null,int expected=200)
        {
            using var q=new HttpRequestMessage(new HttpMethod(method),path);
            if(body!=null)q.Content=JsonContent.Create(body);
            using var r=await c.SendAsync(q); var text=await r.Content.ReadAsStringAsync();
            Record($"HTTP {method} {path} -> {(int)r.StatusCode}");
            if((int)r.StatusCode!=expected)throw new Exception("Unexpected HTTP status; "+text);
            var j=JsonDocument.Parse(text).RootElement.Clone();
            return expected>=400?j:j.TryGetProperty("data",out var d)?d.Clone():j;
        }
        string Id(JsonElement j)=>j.GetProperty("id").GetString()!;
        var studentLogin=await Login("smoke-student@example.invalid");using var student=studentLogin.Client;
        var f=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"fixtures.json"))).RootElement;
        var original=f.GetProperty("course").GetString();var originalLesson=f.GetProperty("lesson").GetString();
        var enrollments=await Request(student,"GET","/api/Enrollments/me");
        var completed=enrollments.EnumerateArray().Single(x=>x.GetProperty("courseId").GetString()==original);
        if(completed.GetProperty("status").GetInt32()!=2||completed.GetProperty("completedAt").ValueKind!=JsonValueKind.String)throw new Exception("Original enrollment is not completed.");
        var completion=completed.GetProperty("completedAt").GetString();var enrollment=Id(completed);
        Record("Original enrollment Completed; baseline completedAt="+completion);
        var progressBefore=await Request(student,"GET",$"/api/Lessons/{originalLesson}/progress");
        await Request(student,"GET",$"/api/Courses/{original}");
        await Request(student,"GET",$"/api/Lessons/{originalLesson}");
        await Request(student,"GET",$"/api/Lessons/{originalLesson}/content-items");
        await Request(student,"POST",$"/api/Lessons/{originalLesson}/start",new{},403);
        await Request(student,"PATCH",$"/api/Lessons/{originalLesson}/progress",new{progressPercentage=100},403);
        await Request(student,"PATCH",$"/api/Lessons/{originalLesson}/complete",new{},403);
        var conflict=await Request(student,"PATCH",$"/api/Enrollments/{enrollment}/complete",new{},409);
        Record("Repeat-completion conflict: "+conflict.ToString());
        await Request(student,"PATCH",$"/api/Enrollments/{enrollment}/complete",new{},409);
        var unchanged=await Request(student,"GET",$"/api/Enrollments/{enrollment}");
        if(unchanged.GetProperty("completedAt").GetString()!=completion)throw new Exception("Original timestamp changed.");
        var progressAfter=await Request(student,"GET",$"/api/Lessons/{originalLesson}/progress");
        if(progressBefore.GetRawText()!=progressAfter.GetRawText())throw new Exception("Original lesson progress changed.");
        Record("PASS original enrollment completion timestamp and lesson progress/timestamps unchanged.");
        var instructorLogin=await Login("smoke-instructor@example.invalid");using var instructor=instructorLogin.Client;
        var courses=await Request(instructor,"GET","/api/Courses");
        var active=enrollments.EnumerateArray().Where(x=>x.GetProperty("status").GetInt32()==1).ToArray();
        Record("Inspected existing Student enrollments: Active count="+active.Length);
        string? course=null,section=null,lesson=null;
        // Reuse a suitable existing Active fixture, without changing its assessments.
        foreach(var e in active)
        {
            var cid=e.GetProperty("courseId").GetString()!;
            var owner=courses.EnumerateArray().SingleOrDefault(x=>Id(x)==cid);
            if(owner.ValueKind==JsonValueKind.Undefined||owner.GetProperty("instructorId").GetString()!=instructorLogin.User||!owner.GetProperty("isPublished").GetBoolean())continue;
            var sections=await Request(instructor,"GET",$"/api/Courses/{cid}/sections");
            foreach(var sec in sections.EnumerateArray())
            {
                var lessons=await Request(instructor,"GET",$"/api/Sections/{Id(sec)}/lessons");
                foreach(var unit in lessons.EnumerateArray())
                {
                    if(!unit.GetProperty("isPublished").GetBoolean())continue;
                    // The dedicated named lesson is the retry/reuse boundary; unrelated lessons are not altered.
                    if(unit.GetProperty("title").GetString()!="SMOKE Assessment Lesson")continue;
                    course=cid;section=Id(sec);lesson=Id(unit);break;
                }
                if(lesson!=null)break;
            }
            if(lesson!=null)break;
        }
        const string title="SMOKE Assessment Practice";
        if(course==null)
        {
            var matches=courses.EnumerateArray().Where(x=>x.GetProperty("title").GetString()==title||x.GetProperty("code").GetString()=="SMOKE-ASSESSMENT").ToArray();
            if(matches.Length>1)throw new Exception("Ambiguous assessment fixture.");
            if(matches.Length==1)
            {
                if(matches[0].GetProperty("instructorId").GetString()!=instructorLogin.User)throw new Exception("Assessment owner mismatch.");
                course=Id(matches[0]);Record("Reusing existing assessment course "+course);
            }
            else {var created=await Request(instructor,"POST","/api/Courses",new{title,code="SMOKE-ASSESSMENT",description="Disposable assessment acceptance practice; learner performs all attempts and submissions.",level=1,durationInMinutes=30},201);course=created.GetProperty("courseId").GetString();}
        }
        var storedSections=await Request(instructor,"GET",$"/api/Courses/{course}/sections");
        var sectionMatch=storedSections.EnumerateArray().Where(x=>x.GetProperty("title").GetString()=="SMOKE Assessment Section").ToArray();
        if(sectionMatch.Length>1)throw new Exception("Duplicate assessment section.");
        section??=sectionMatch.Length==1?Id(sectionMatch[0]):Id(await Request(instructor,"POST",$"/api/Courses/{course}/sections",new{title="SMOKE Assessment Section",description="Quiz and assignment practice",displayOrder=1},201));
        var storedLessons=await Request(instructor,"GET",$"/api/Sections/{section}/lessons");
        var lessonMatch=storedLessons.EnumerateArray().Where(x=>x.GetProperty("title").GetString()=="SMOKE Assessment Lesson").ToArray();
        if(lessonMatch.Length>1)throw new Exception("Duplicate assessment lesson.");
        lesson??=lessonMatch.Length==1?Id(lessonMatch[0]):Id(await Request(instructor,"POST",$"/api/Sections/{section}/lessons",new{title="SMOKE Assessment Lesson",description="Practice all supported quiz question types, then submit an assignment.",lessonType=1,content="Read each assessment's instructions. Start and submit your own work. Keep this enrollment Active for assessment practice.",durationInMinutes=30,displayOrder=1},201));
        // GET is 404 before the first quiz exists; never blindly repeat creation.
        JsonElement quiz;
        using(var r=await instructor.GetAsync($"/{lesson}/quiz"))
        {
            if(r.StatusCode==System.Net.HttpStatusCode.NotFound)quiz=await Request(instructor,"POST",$"/{lesson}/quiz",new{title="SMOKE Three-Type Quiz",description="Single choice, multiple choice, and true/false practice.",passingPercentage=60,timeLimitInMinutes=20,maxAttempts=3},201);
            else {r.EnsureSuccessStatusCode();quiz=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();}
        }
        var quizId=Id(quiz);var questions=await Request(instructor,"GET",$"/api/Quizzes/{quizId}/questions");
        var specs=new[]{new{Text="Select the even number.",Type=1,Options=new[]{("2",true),("3",false)}},new{Text="Select every primary color in this list.",Type=2,Options=new[]{("Red",true),("Blue",true),("Green",false)}},new{Text="The Earth orbits the Sun.",Type=3,Options=new[]{("True",true),("False",false)}}};
        for(int i=0;i<specs.Length;i++)
        {
            var spec=specs[i];var match=questions.EnumerateArray().Where(x=>x.GetProperty("displayOrder").GetInt32()==i+1).ToArray();
            if(match.Length>1)throw new Exception("Duplicate quiz question.");
            string qid;
            if(match.Length==1){if(match[0].GetProperty("questionText").GetString()!=spec.Text||match[0].GetProperty("questionType").GetInt32()!=spec.Type)throw new Exception("Existing quiz differs from fixture.");qid=Id(match[0]);}
            else qid=(await Request(instructor,"POST",$"/api/Quizzes/{quizId}/questions",new{questionText=spec.Text,questionType=spec.Type,displayOrder=i+1,marks=1},201)).GetProperty("questionId").GetString()!;
            var options=await Request(instructor,"GET",$"/api/Quizzes/{quizId}/questions/{qid}/options");
            for(int n=0;n<spec.Options.Length;n++)
            {
                var option=spec.Options[n];var existing=options.EnumerateArray().Where(x=>x.GetProperty("displayOrder").GetInt32()==n+1).ToArray();
                if(existing.Length==0)await Request(instructor,"POST",$"/api/Quizzes/{quizId}/questions/{qid}/options",new{optionText=option.Item1,isCorrect=option.Item2,displayOrder=n+1},201);
                else if(existing.Length!=1||existing[0].GetProperty("optionText").GetString()!=option.Item1||existing[0].GetProperty("isCorrect").GetBoolean()!=option.Item2)throw new Exception("Existing option differs.");
            }
        }
        if(!quiz.GetProperty("isPublished").GetBoolean())await Request(instructor,"PATCH",$"/{lesson}/quiz/publish",new{});
        var assignments=await Request(instructor,"GET",$"/api/Lessons/{lesson}/assignments");
        var assignmentMatches=assignments.EnumerateArray().Where(x=>x.GetProperty("title").GetString()=="SMOKE Practice Assignment").ToArray();
        if(assignmentMatches.Length>1)throw new Exception("Duplicate assignment.");
        var assignment=assignmentMatches.Length==1?assignmentMatches[0]:await Request(instructor,"POST",$"/api/Lessons/{lesson}/assignments",new{title="SMOKE Practice Assignment",instructions="In your own words, describe what you learned from the three question types. Submit text, one PDF, or both. Review before confirming: one final submission, no resubmission. Staff records one final grade and feedback out of 10.",dueAtUtc=DateTimeOffset.UtcNow.AddDays(14),maximumMarks=10},201);
        if(assignment.GetProperty("dueAtUtc").GetDateTimeOffset()<=DateTimeOffset.UtcNow)throw new Exception("Existing assignment deadline is not future; no policy override made.");
        if(!assignment.GetProperty("isPublished").GetBoolean())await Request(instructor,"PATCH",$"/api/Lessons/{lesson}/assignments/{Id(assignment)}/publication",new{isPublished=true,confirmed=true});
        var unitState=await Request(instructor,"GET",$"/api/Lessons/{lesson}");
        if(!unitState.GetProperty("isPublished").GetBoolean())await Request(instructor,"PATCH",$"/api/Lessons/{lesson}/publish",new{});
        var courseState=await Request(instructor,"GET",$"/api/Courses/{course}");
        if(!courseState.GetProperty("isPublished").GetBoolean())await Request(instructor,"POST",$"/api/Courses/{course}/publish",new{});
        var existingEnrollments=await Request(student,"GET","/api/Enrollments/me");
        var own=existingEnrollments.EnumerateArray().Where(x=>x.GetProperty("courseId").GetString()==course).ToArray();
        if(own.Length==0)await Request(student,"POST",$"/api/Courses/{course}/enroll",new{},201);
        else if(own.Length!=1||own[0].GetProperty("status").GetInt32()!=1)throw new Exception("Existing assessment enrollment is not Active; history preserved.");
        var verifiedEnrollments=await Request(student,"GET","/api/Enrollments/me");
        var practiceEnrollment=verifiedEnrollments.EnumerateArray().Single(x=>x.GetProperty("courseId").GetString()==course);
        if(practiceEnrollment.GetProperty("status").GetInt32()!=1||practiceEnrollment.GetProperty("completedAt").ValueKind!=JsonValueKind.Null)throw new Exception("Practice enrollment is not Active.");
        Record("PASS assessment enrollment Active, completedAt null; id="+Id(practiceEnrollment));
        var studentQuiz=await Request(student,"GET",$"/{lesson}/quiz");
        var attempts=await Request(student,"GET",$"/api/Quizzes/{quizId}/attempts");
        var studentAssignment=await Request(student,"GET",$"/api/Lessons/{lesson}/assignments/{Id(assignment)}");
        var submissions=await Request(student,"GET",$"/api/Lessons/{lesson}/assignments/{Id(assignment)}/submissions/me");
        var finalOriginal=await Request(student,"GET",$"/api/Enrollments/{enrollment}");
        if(finalOriginal.GetProperty("completedAt").GetString()!=completion)throw new Exception("Original completion changed.");
        var manifest=new{course,section,lesson,quiz=quizId,assignment=Id(assignment),courseTitle=courseState.GetProperty("title").GetString(),quizSettings=studentQuiz,assignmentSettings=studentAssignment,attemptCount=attempts.GetArrayLength(),submissionCount=submissions.GetArrayLength(),originalCompletedAt=completion,browserUrl=S("WebUrl")+"/app/workspace/student/courses/"+course};
        await File.WriteAllTextAsync(Path.Combine(root,"assessment-fixture.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        Record(JsonSerializer.Serialize(manifest));
        Record("No quiz attempt started, assignment submitted, or grade recorded. Environment left running.");
    }
}
