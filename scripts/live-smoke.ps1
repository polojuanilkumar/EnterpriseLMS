param(
    [ValidateSet('start','resume','refresh','recreate','status','stop','cleanup','verify')][string]$Action = 'status',
    [string]$Run,
    [string]$Frontend = 'C:\Users\poloj\Anil Documents\EnterpriseLMS.Angular'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$base = Join-Path $workspace 'artifacts/live-smoke'
$image = 'mcr.microsoft.com/mssql/server:2025-latest'
$runner = Join-Path $workspace 'scripts/LiveSmoke/bin/Release/net10.0/LiveSmoke.dll'
function Docker([string[]]$Arguments) {
    $output = & docker.exe @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Docker operation failed (diagnostics suppressed to protect secrets).' }
    return $output
}
function Save-State { $script:state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $script:root 'state.json') }
function Owned-Container {
    $c = (Docker @('inspect',$script:state.Container) | ConvertFrom-Json)[0]
    if ($c.Id -cne $script:state.Container -or $c.Name -cne ('/lms-smoke-' + $script:state.Run) -or $c.Config.Labels.'lms.smoke.owner' -cne $script:state.Run) { throw 'Refusing foreign container.' }
    return $c
}
function Stop-OwnedHosts {
    # Validate every saved identity before stopping any host. Never kill by name or port.
    foreach ($entry in $script:state.Processes) {
        if ($entry.Name -notin @('api','web')) { throw 'Unknown saved host.' }
        $expected = Join-Path (Join-Path $script:root $entry.Name) $(if($entry.Name -eq 'api'){'LMS.Api.dll'}else{'LMS.Web.dll'})
        if ($entry.Dll -cne $expected) { throw 'Saved DLL is outside its owned host directory.' }
        $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
        if (!$process) { continue }
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.Id)").CommandLine
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.StartTicks -or !$command -or !$command.Contains($entry.Dll)) { throw 'Refusing process with mismatched ownership.' }
    }
    foreach ($entry in $script:state.Processes) {
        $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
        if (!$process) { continue }
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.Id)").CommandLine
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.StartTicks -or !$command.Contains($entry.Dll)) { throw 'Refusing process with mismatched ownership.' }
        Stop-Process -Id $entry.Id
        $process.WaitForExit(10000) | Out-Null
    }
}
function Stop-Owned {
    Stop-OwnedHosts
    if ($script:state.Container) {
        Owned-Container | Out-Null
        Docker @('rm','--force',$script:state.Container) | Out-Null
        $script:state.Container = ''
    }
    $script:state.Status = 'Stopped'; Save-State
}
function Free-Port {
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback,0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop(); return $port
}
function Host-Alive($entry) {
    $p=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if (!$p -or $p.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.StartTicks) { return $false }
    $command=(Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.Id)").CommandLine
    return ($command -and $command.Contains($entry.Dll) -and $entry.Dll.StartsWith($script:root + '\'))
}
function Http-Ready {
    try {
        $web=Invoke-WebRequest -UseBasicParsing -Uri ($script:state.WebUrl+'/bff/antiforgery') -TimeoutSec 3
        $apiStatus=0
        try { $api=Invoke-WebRequest -UseBasicParsing -Uri ($script:state.ApiUrl+'/api/auth/login') -TimeoutSec 3; $apiStatus=[int]$api.StatusCode }
        catch { if ($_.Exception.Response) { $apiStatus=[int]$_.Exception.Response.StatusCode } }
        return ($web.StatusCode -eq 200 -and $apiStatus -eq 405)
    } catch { return $false }
}
function Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($bytes); $rng.Dispose()
    return 'Sm0ke!' + ([BitConverter]::ToString($bytes).Replace('-','')) + 'a'
}
function App-Process($name,$environment) {
    $folder = Join-Path $script:root $name
    $dll = Join-Path $folder $(if($name -eq 'api'){'LMS.Api.dll'}else{'LMS.Web.dll'})
    # Remove ambient app overrides; no launch profiles or application settings are used.
    $saved = @{}
    foreach ($item in Get-ChildItem Env:) { if ($item.Name -match '^(ASPNETCORE_|DOTNET_|ConnectionStrings|Jwt|LmsApi|PrivateStorage|DataProtection|Kestrel|Angular|Logging|Account)') { $saved[$item.Name]=$item.Value; [Environment]::SetEnvironmentVariable($item.Name,$null,'Process') } }
    try {
        foreach($key in $environment.Keys) { [Environment]::SetEnvironmentVariable($key,$environment[$key],'Process') }
        $process = Start-Process dotnet -ArgumentList @(('"'+$dll+'"')) -WorkingDirectory $folder -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:root "$name.stdout.log") -RedirectStandardError (Join-Path $script:root "$name.stderr.log")
        $script:state.Processes += @{Id=$process.Id;StartTicks=$process.StartTime.ToUniversalTime().Ticks.ToString();Dll=$dll;Name=$name}
        Save-State
    } finally {
        foreach($key in $environment.Keys) { [Environment]::SetEnvironmentVariable($key,$null,'Process') }
        foreach($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key],'Process') }
    }
}
if ($Action -notin @('start','resume','refresh','recreate')) {
    if ($Run -notmatch '^[a-f0-9]{32}$') { throw 'Supply the exact -Run ID printed by start.' }
    $root = Join-Path $base $Run
    $state = Get-Content -LiteralPath (Join-Path $root 'state.json') -Raw | ConvertFrom-Json
    if ($state.Run -cne $Run) { throw 'Run ownership mismatch.' }
    if ($Action -eq 'status') {
        if ($state.Container) { $c = Owned-Container; Write-Output "Container: $($state.Container) ($($c.State.Status))" }
        $live=@($state.Processes | Where-Object { Host-Alive $_ })
        foreach($entry in $state.Processes) { $alive=Host-Alive $entry; Write-Output "$($entry.Name) PID $($entry.Id): running=$alive" }
        $state.Status=if($c -and $c.State.Running -and $live.Count -eq 2 -and (Http-Ready)){'Running'}elseif($live.Count -eq 0){'Stopped'}else{'Degraded'}
        Save-State
        Write-Output "Run: $Run; status: $($state.Status); browser: $($state.WebUrl)/app/"
        return
    }
    if ($Action -eq 'verify') { & dotnet $runner verify $root; if($LASTEXITCODE -ne 0){throw 'HTTP verification failed.'}; return }
    Stop-Owned
    if ($Action -eq 'cleanup') {
        $resolved=[IO.Path]::GetFullPath($root); $expected=[IO.Path]::GetFullPath((Join-Path $base $Run))
        if($resolved -cne $expected -or (Get-Item -LiteralPath $root).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or @(Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}).Count) { throw 'Refusing unsafe directory deletion.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Write-Output "Owned run $Run stopped."
    return
}
Docker @('image','inspect',$image,'--format','{{.Id}}') | Out-Null
& dotnet dev-certs https --check --trust | Out-Null
if($LASTEXITCODE -ne 0) { throw 'Prerequisite: run dotnet dev-certs https --trust yourself, then retry. No trust changes were made.' }
$certificate=Get-ChildItem Cert:\CurrentUser\My | Where-Object {$_.Subject -eq 'CN=localhost' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) -and $_.Extensions.Oid.Value -contains '1.3.6.1.4.1.311.84.1.1'} | Sort-Object NotAfter -Descending | Select-Object -First 1
if(!$certificate -or !$certificate.Verify() -or !($certificate.DnsNameList.Unicode -contains 'localhost')) { throw 'Trusted localhost certificate with matching SAN required.' }
if ($Action -eq 'refresh') {
    if ($Run -notmatch '^[a-f0-9]{32}$') { throw 'Supply exact existing run ID.' }
    $root=Join-Path $base $Run
    $state=Get-Content -LiteralPath (Join-Path $root 'state.json') -Raw | ConvertFrom-Json
    if ($state.Run -cne $Run) { throw 'Run ownership mismatch.' }
    $c=Owned-Container
    $bindings=@($c.NetworkSettings.Ports.'1433/tcp')
    if (!$c.State.Running -or $bindings.Count -ne 1 -or $bindings[0].HostIp -cne '127.0.0.1') { throw 'Refresh requires the existing running loopback-only container.' }
    $state.SqlPort=$bindings[0].HostPort; Save-State
    $refresh=Join-Path $root ('refresh-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $refresh | Out-Null
    # Build outside deployed paths: running hosts never observe partly overwritten assemblies.
    foreach ($name in @('api','web')) {
        $deployed=Join-Path $root $name
        if ((Get-Item -LiteralPath $deployed).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or @(Get-ChildItem -LiteralPath $deployed -Recurse -Force | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}).Count) { throw 'Refusing reparse point in deployment.' }
        if (@(Get-ChildItem -LiteralPath $deployed -Filter 'appsettings*.json').Count) { throw 'Unexpected application settings in isolated host directory.' }
        $project=if($name -eq 'api'){'LMS.Api'}else{'LMS.Web'}
        & dotnet publish (Join-Path $workspace "$project/$project.csproj") -c Release --no-restore -o (Join-Path $refresh $name) --nologo *> (Join-Path $refresh "$name-build.log")
        if($LASTEXITCODE -ne 0){throw "Refresh build failed: $name; existing hosts retained."}
    }
    Push-Location $Frontend
    try { & node (Join-Path $Frontend 'node_modules/@angular/cli/bin/ng.js') build --configuration production --base-href /app/ --output-path (Join-Path $refresh 'angular') *> (Join-Path $refresh 'angular-build.log') }
    finally { Pop-Location }
    if($LASTEXITCODE -ne 0){throw 'Angular refresh build failed; existing hosts retained.'}
    & dotnet build (Join-Path $PSScriptRoot 'LiveSmoke/LiveSmoke.csproj') -c Release --no-restore --nologo *> (Join-Path $refresh 'runner-build.log')
    if($LASTEXITCODE -ne 0){throw 'Refresh runner build failed; existing hosts retained.'}
    & dotnet $runner refresh-snapshot $root (Join-Path $refresh 'database-before.json')
    if($LASTEXITCODE -ne 0){throw 'Pinned-TLS pre-refresh snapshot failed; existing hosts retained.'}
    $preserved=@{}
    foreach ($file in @('private.json','credentials.txt','localhost.pfx','sql.pem','sql.key','mssql.conf','fixtures.json','assessment-fixture.json')) {
        $path=Join-Path $root $file
        if(!(Test-Path -LiteralPath $path)){throw "Required private artifact missing: $file"}
        $preserved[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    foreach ($folder in @('api-keys','web-keys','assignments','media','emails')) {
        $path=Join-Path $root $folder
        if(Test-Path -LiteralPath $path){foreach($file in Get-ChildItem -LiteralPath $path -File -Recurse){$preserved[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}}
    }
    $preserved | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $refresh 'preserved-files.json')
    Stop-OwnedHosts
    $remaining=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine.Contains($root)})
    if($remaining.Count){throw 'An existing run host remains; refusing binary overwrite.'}
    $state.Processes=@(); $state.Status='Refreshing'; Save-State
    foreach ($name in @('api','web')) {
        $deployed=Join-Path $root $name
        Copy-Item -LiteralPath $deployed -Destination (Join-Path $refresh ('previous-'+$name)) -Recurse
        # Do not deploy any appsettings files: isolated environment supplies all configuration.
        Get-ChildItem -LiteralPath (Join-Path $refresh $name) | Where-Object {$_.Name -notlike 'appsettings*.json'} | Copy-Item -Destination $deployed -Recurse -Force
    }
    $app=Join-Path $root 'web/wwwroot/app'
    Copy-Item -Path (Join-Path $refresh 'angular/browser/*') -Destination $app -Recurse -Force
    foreach($path in $preserved.Keys){if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $preserved[$path]){throw 'A preserved private artifact changed.'}}
    $refresh | Set-Content -LiteralPath (Join-Path $root 'latest-refresh.txt')
    Write-Output 'New builds deployed with owned hosts stopped; private artifacts retained. Starting existing resume lifecycle.'
    # Resume performs fresh ownership/port/pinned-TLS checks and starts only these hosts. No seed/migration branch.
    $Action='resume'
}
if ($Action -in @('resume','recreate')) {
    if ($Run -notmatch '^[a-f0-9]{32}$') { throw 'Supply exact existing run ID.' }
    $root=Join-Path $base $Run
    $state=Get-Content -LiteralPath (Join-Path $root 'state.json') -Raw | ConvertFrom-Json
    if ($state.Run -cne $Run) { throw 'Run ownership mismatch.' }
    if (@($state.Processes | Where-Object { Host-Alive $_ }).Count) { throw 'Existing owned host found; refusing duplicate startup.' }
    $hosts=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine.Contains($root) })
    if ($hosts.Count) { throw 'Existing run host found; refusing duplicate startup.' }
    if ($Action -eq 'recreate') {
        if ($state.Status -ne 'Stopped' -or $state.Container) { throw 'Recreation requires stopped state without a container.' }
        $oldByName=@(Docker @('ps','-aq','--filter',"name=^/lms-smoke-$Run$"))
        $oldByLabel=@(Docker @('ps','-aq','--filter',"label=lms.smoke.owner=$Run"))
        if ($oldByName.Count -or $oldByLabel.Count) { throw 'Old test container remains; refusing recreation.' }
        foreach ($file in @('private.json','localhost.pfx','sql.pem','sql.key','mssql.conf')) {
            if (!(Test-Path -LiteralPath (Join-Path $root $file))) { throw "Required existing artifact missing: $file" }
        }
        Write-Output 'Verified: no old run container or host remains.'
    } else {
    $c=Owned-Container
    $bindings=@($c.HostConfig.PortBindings.'1433/tcp')
    if ($bindings.Count -ne 1 -or $bindings[0].HostIp -cne '127.0.0.1') { throw 'Resume binding mismatch.' }
    }
    $archive=Join-Path $root ('attempt-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $archive | Out-Null
    Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Extension -eq '.log' -or $_.Name -in @('state.json','credentials.txt','fixtures.json','http-results.txt') } | Copy-Item -Destination $archive
    $state.Processes=@(); $state.Status='Preparing'; Save-State
} else {
$Run=[Guid]::NewGuid().ToString('N'); $root=Join-Path $base $Run
New-Item -ItemType Directory -Path $root -Force | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
$acl.AddAccessRule($rule); Set-Acl -LiteralPath $root -AclObject $acl
$state=@{Run=$Run;Container='';SqlPort='';WebUrl=('https://localhost:'+(Free-Port));ApiUrl=('https://localhost:'+(Free-Port));Processes=@();Status='Preparing'}
Save-State
Write-Output "Preparing owned disposable run $Run"
}
try {
    if ($Action -in @('resume','recreate')) {
        $private=Get-Content -LiteralPath (Join-Path $root 'private.json') -Raw | ConvertFrom-Json
        $pfx=Join-Path $root 'localhost.pfx'
        & dotnet build (Join-Path $PSScriptRoot 'LiveSmoke/LiveSmoke.csproj') -c Release --no-restore --nologo *> (Join-Path $root 'runner-build.log')
        if($LASTEXITCODE -ne 0){throw 'Fixture runner build failed.'}
        if ($Action -eq 'recreate') {
            "ACCEPT_EULA=Y`nMSSQL_PID=Developer`nMSSQL_SA_PASSWORD=$($private.SqlPassword)" | Set-Content -LiteralPath (Join-Path $root 'container.env') -Encoding ASCII
            try {
                $state.Container=(Docker @('create','--pull=never','--user','0','--name',"lms-smoke-$Run",'--label',"lms.smoke.owner=$Run",'--label','lms.smoke.purpose=EnterpriseLMS disposable live smoke','--publish','127.0.0.1::1433','--env-file',(Join-Path $root 'container.env'),'--entrypoint','/bin/bash',$image,'-c','chmod 600 /var/opt/mssql/smoke.key; exec /opt/mssql/bin/sqlservr')).Trim()
                $state.Status='Preparing'; Save-State
            } finally { Remove-Item -LiteralPath (Join-Path $root 'container.env') -ErrorAction SilentlyContinue }
        }
        if ($Action -eq 'resume') {
            foreach ($file in @('localhost.pfx','sql.pem','sql.key','mssql.conf','credentials.txt','fixtures.json','api/LMS.Api.dll','web/LMS.Web.dll','web/wwwroot/app/index.html')) {
                if (!(Test-Path -LiteralPath (Join-Path $root $file))) { throw "Required existing artifact missing: $file" }
            }
            $c=Owned-Container
            if (!$c.State.Running) { Docker @('start',$state.Container) | Out-Null }
        } else {
        Owned-Container | Out-Null
        foreach($pair in @(@('sql.pem','smoke.pem'),@('sql.key','smoke.key'))) { Docker @('cp',(Join-Path $root $pair[0]),($state.Container+':/var/opt/mssql/'+$pair[1])) | Out-Null }
        Docker @('cp',(Join-Path $root 'mssql.conf'),($state.Container+':/var/opt/mssql/mssql.conf')) | Out-Null
        Docker @('restart',$state.Container) | Out-Null
        }
        $c=Owned-Container; $bindings=@($c.NetworkSettings.Ports.'1433/tcp')
        if($bindings.Count -ne 1 -or $bindings[0].HostIp -cne '127.0.0.1'){throw 'SQL binding is not isolated after restart.'}
        $state.SqlPort=$bindings[0].HostPort; Save-State
    } else {
    $private=@{SqlPassword=(Secret);LoginPassword=(Secret);JwtKey=(Secret);CertificatePassword=(Secret)}
    $private | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'private.json')
    $pfx=Join-Path $root 'localhost.pfx'
    [IO.File]::WriteAllBytes($pfx,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$private.CertificatePassword))
    & dotnet build (Join-Path $PSScriptRoot 'LiveSmoke/LiveSmoke.csproj') -c Release --nologo *> (Join-Path $root 'runner-build.log')
    if($LASTEXITCODE -ne 0){throw 'Fixture runner build failed. See private runner-build.log.'}
    & dotnet $runner certificate $root
    if($LASTEXITCODE -ne 0){throw 'SQL certificate generation failed.'}
    "[network]`ntlsprotocols = 1.2`ntlscert = /var/opt/mssql/smoke.pem`ntlskey = /var/opt/mssql/smoke.key`nforceencryption = 1" | Set-Content -LiteralPath (Join-Path $root 'mssql.conf') -Encoding ASCII
    "ACCEPT_EULA=Y`nMSSQL_PID=Developer`nMSSQL_SA_PASSWORD=$($private.SqlPassword)" | Set-Content -LiteralPath (Join-Path $root 'container.env') -Encoding ASCII
    $state.Container=(Docker @('create','--pull=never','--user','0','--name',"lms-smoke-$Run",'--label',"lms.smoke.owner=$Run",'--label','lms.smoke.purpose=EnterpriseLMS disposable live smoke','--publish','127.0.0.1::1433','--env-file',(Join-Path $root 'container.env'),'--entrypoint','/bin/bash',$image,'-c','chmod 600 /var/opt/mssql/smoke.key; exec /opt/mssql/bin/sqlservr')).Trim()
    Save-State
    Remove-Item -LiteralPath (Join-Path $root 'container.env')
    Owned-Container | Out-Null
    foreach($pair in @(@('sql.pem','smoke.pem'),@('sql.key','smoke.key'),@('mssql.conf','mssql.conf'))) { Docker @('cp',(Join-Path $root $pair[0]),($state.Container+':/var/opt/mssql/'+$pair[1])) | Out-Null }
    Docker @('start',$state.Container) | Out-Null
    $c=Owned-Container; $bindings=@($c.NetworkSettings.Ports.'1433/tcp')
    if($bindings.Count -ne 1 -or $bindings[0].HostIp -cne '127.0.0.1'){throw 'SQL binding is not isolated.'}
    $state.SqlPort=$bindings[0].HostPort; Save-State
    }
    if ($Action -eq 'resume') {
        & dotnet $runner probe $root *> (Join-Path $root 'probe.log')
        if($LASTEXITCODE -ne 0){throw 'Existing SQL database strict TLS probe failed.'}
    } else {
    & dotnet $runner seed $root *> (Join-Path $root 'seed.log')
    if($LASTEXITCODE -ne 0){throw 'Disposable seed failed. See private seed.log.'}
    foreach($name in @('api','web')) {
        $project=if($name -eq 'api'){'LMS.Api'}else{'LMS.Web'}
        $destination=Join-Path $root $name
        & dotnet publish (Join-Path $workspace "$project/$project.csproj") -c Release -o $destination --nologo *> (Join-Path $root "$name-build.log")
        if($LASTEXITCODE -ne 0){throw "Build failed: $name (private build log)."}
        # Only files inside this freshly-created run's publish directories are removed.
        Get-ChildItem -LiteralPath $destination -Filter 'appsettings*.json' | Remove-Item -Force
    }
    $ng=Join-Path $Frontend 'node_modules/@angular/cli/bin/ng.js'
    Push-Location $Frontend
    try { & node $ng build --configuration production --base-href /app/ --output-path (Join-Path $root 'angular') *> (Join-Path $root 'angular-build.log') }
    finally { Pop-Location }
    if($LASTEXITCODE -ne 0){throw 'Angular build failed (private build log).'}
    $app=Join-Path $root 'web/wwwroot/app'; New-Item -ItemType Directory -Path $app -Force | Out-Null
    Copy-Item -Path (Join-Path $root 'angular/browser/*') -Destination $app -Recurse
    }
    foreach($name in @('api','web')) {
        $environment=@{
            ASPNETCORE_ENVIRONMENT='Smoke'; DOTNET_ENVIRONMENT='Smoke'; ASPNETCORE_URLS=$(if($name -eq 'api'){$state.ApiUrl.Replace('localhost','127.0.0.1')}else{$state.WebUrl.Replace('localhost','127.0.0.1')});
            Kestrel__Certificates__Default__Path=$pfx; Kestrel__Certificates__Default__Password=$private.CertificatePassword;
            DataProtection__KeyPath=(Join-Path $root "$name-keys"); DataProtection__ApplicationName="LMS-Smoke-$Run-$name";
            Logging__LogLevel__Default='Warning'; Logging__LogLevel__Microsoft='Warning'; AllowedHosts='localhost';
            PrivateStorage__Media=(Join-Path $root 'media'); PrivateStorage__Assignments=(Join-Path $root 'assignments'); PrivateStorage__Emails=(Join-Path $root 'emails')
        }
        if($name -eq 'api') {
            $environment.ConnectionStrings__DefaultConnection="Server=127.0.0.1,$($state.SqlPort);Database=LMS_Smoke_$Run;User ID=sa;Password=$($private.SqlPassword);Encrypt=Strict;Server Certificate=$(Join-Path $root 'sql.pem');Host Name In Certificate=localhost;Pooling=false"
            $environment.Jwt__Key=$private.JwtKey; $environment.Jwt__Issuer="smoke-$Run"; $environment.Jwt__Audience="smoke-$Run"; $environment.Jwt__AccessTokenMinutes='120'
        } else { $environment.LmsApi__BaseUrl=$state.ApiUrl+'/'; $environment.Angular__Enabled='true' }
        App-Process $name $environment
    }
    $ready=$false
    for($attempt=0;$attempt -lt 60;$attempt++) {
        try { $response=Invoke-WebRequest -UseBasicParsing -Uri ($state.WebUrl+'/bff/antiforgery'); if($response.StatusCode -eq 200){$ready=$true;break} } catch { Start-Sleep -Seconds 1 }
    }
    if(!$ready){throw 'Trusted HTTPS readiness failed (private process logs).'}
    & dotnet $runner verify $root *> (Join-Path $root 'verify.log')
    if($LASTEXITCODE -ne 0){throw 'Live HTTP verification failed (private verify.log).'}
    $state.Status='Running'; Save-State
    Write-Output "RUNNING: $Run`nBrowser: $($state.WebUrl)/app/`nDocker Desktop: lms-smoke-$Run`nCredentials: $(Join-Path $root 'credentials.txt')`nHTTP report: $(Join-Path $root 'http-results.txt')"
    foreach($entry in $state.Processes){Write-Output "$($entry.Name) PID $($entry.Id)"}
} catch {
    $failure=$_.Exception.Message
    if ($Action -eq 'start') {
        try { Stop-Owned } catch { Write-Warning 'Ownership-verified cleanup could not finish. Inspect status; no foreign resources were touched.' }
    } else { Write-Warning 'Resume failed; existing owned resources retained for inspection.' }
    if ($Action -ne 'start') { $state.Status='Failed'; Save-State }
    throw "Setup failed: $failure Run: $Run."
}
