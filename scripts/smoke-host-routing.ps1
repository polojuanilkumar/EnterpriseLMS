param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$state=Get-Content (Join-Path $Root 'state.json') -Raw | ConvertFrom-Json
if ((Split-Path $Root -Leaf) -cne $state.Run) { throw 'Run mismatch.' }
$c=(& docker inspect $state.Container | ConvertFrom-Json)[0]
if ($LASTEXITCODE -ne 0 -or $c.Id -cne $state.Container -or $c.Name -cne ('/lms-smoke-'+$state.Run) -or $c.Config.Labels.'lms.smoke.owner' -cne $state.Run) { throw 'Container ownership mismatch.' }
$binding=@($c.NetworkSettings.Ports.'1433/tcp')
if (!$c.State.Running -or $binding.Count -ne 1 -or $binding[0].HostIp -cne '127.0.0.1' -or $binding[0].HostPort -cne $state.SqlPort) { throw 'Container binding mismatch.' }
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SmokeProcessEnvironment {
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
 [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr read);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h,int cls,byte[] info,int size,out int read);
 static byte[] Read(IntPtr h,long address,int size) { var b=new byte[size]; IntPtr n; if(!ReadProcessMemory(h,new IntPtr(address),b,size,out n)||n.ToInt64()!=size)throw new Exception("Process memory unavailable.");return b; }
 public static string ReadEnvironment(int id) {
  if(IntPtr.Size!=8)throw new Exception("64-bit diagnostic required.");
  var h=OpenProcess(0x410,false,id); if(h==IntPtr.Zero)throw new Exception("Process access denied.");
  try {var info=new byte[48];int read;if(NtQueryInformationProcess(h,0,info,48,out read)!=0)throw new Exception("Process query failed.");
   long peb=BitConverter.ToInt64(info,8);long parameters=BitConverter.ToInt64(Read(h,peb+0x20,8),0);
   long env=BitConverter.ToInt64(Read(h,parameters+0x80,8),0);
   var result=new StringBuilder(); for(int offset=0;offset<1048576;offset+=2){char ch=(char)BitConverter.ToUInt16(Read(h,env+offset,2),0);if(ch=='\0'&&result.Length>0&&result[result.Length-1]=='\0')break;result.Append(ch);} return result.ToString();
  }finally{CloseHandle(h);}
 }
}
'@
foreach($entry in $state.Processes) {
 $p=Get-Process -Id $entry.Id
 $command=(Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.Id)").CommandLine
 $expected=Join-Path $Root $(if($entry.Name -eq 'api'){'api/LMS.Api.dll'}else{'web/LMS.Web.dll'})
 if($entry.Dll -cne $expected -or $p.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.StartTicks -or !$command.Contains($expected)){throw 'Host ownership mismatch.'}
 $envMap=@{}
 foreach($item in [SmokeProcessEnvironment]::ReadEnvironment($entry.Id).Split([char]0)){ $i=$item.IndexOf('=');if($i -gt 0){$envMap[$item.Substring(0,$i)]=$item.Substring($i+1)} }
 if($entry.Name -eq 'web'){
  Write-Output "Running BFF API destination matches run: $($envMap['LmsApi__BaseUrl'] -ceq ($state.ApiUrl+'/'))"
 }else{
  $builder=New-Object System.Data.Common.DbConnectionStringBuilder
  $builder.set_ConnectionString($envMap['ConnectionStrings__DefaultConnection'])
  Write-Output "Running API SQL endpoint matches owned container: $($builder['Server'] -ceq ('127.0.0.1,'+$state.SqlPort))"
  Write-Output "Running API database matches run: $($builder['Database'] -ceq ('LMS_Smoke_'+$state.Run))"
  Write-Output "Running API strict TLS and certificate pin match: $($builder['Encrypt'] -ceq 'Strict' -and $builder['Server Certificate'] -ceq (Join-Path $Root 'sql.pem'))"
 }
 $envMap.Clear()
}
