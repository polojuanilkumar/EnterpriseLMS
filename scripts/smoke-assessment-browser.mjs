import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';

const root=path.resolve(process.argv[2]);
const state=JSON.parse(fs.readFileSync(path.join(root,'state.json')));
if(state.Run!=='d6966d0607204702879ad62b6d352f6a'||path.basename(root)!==state.Run)throw Error('Wrong run');
const fixture=JSON.parse(fs.readFileSync(path.join(root,'fixtures.json')));
const assessment=JSON.parse(fs.readFileSync(path.join(root,'assessment-fixture.json')));
const profile=path.join(root,'browser-assessment-'+Date.now());
const chrome=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+profile,'--no-first-run','--no-default-browser-check','about:blank'],{windowsHide:true,stdio:'ignore'});
let ws;
try {
  let port;
  for(let i=0;i<100;i++){try{port=Number(fs.readFileSync(path.join(profile,'DevToolsActivePort'),'utf8').split('\n')[0]);break;}catch{await delay(100);}}
  if(!port)throw Error('Browser unavailable');
  const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
  await new Promise(resolve=>ws.addEventListener('open',resolve,{once:true}));
  let id=0;const pending=new Map();const network=[];
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error('Browser protocol error')):p.resolve(m.result);}if(m.method==='Network.responseReceived'){const r=m.params.response;const u=new URL(r.url);if(u.origin===state.WebUrl&&u.pathname.startsWith('/bff/'))network.push({path:u.pathname,status:r.status});}});
  const call=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});ws.send(JSON.stringify({id:key,method,params}));});
  const evaluate=async expression=>{const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error('Page evaluation failed');return r.result.value;};
  await call('Network.enable');
  await call('Page.navigate',{url:state.WebUrl+'/app/login'});
  for(let i=0;i<100;i++){if(await evaluate('!!document.querySelector("#password")'))break;await delay(100);}
  const lines=fs.readFileSync(path.join(root,'credentials.txt'),'utf8').split(/\r?\n/);
  const prefix='Disposable synthetic identities only. Password for these five accounts: ';
  if(!lines[0].startsWith(prefix))throw Error('Unexpected credential format');
  const credentials={email:'smoke-student@example.invalid',password:lines[0].slice(prefix.length)};
  const found=await evaluate(`(()=>{const c=${JSON.stringify(credentials)};for(const k of ['email','password']){const e=document.getElementById(k);if(!e)return false;e.value=c[k];e.dispatchEvent(new Event('input',{bubbles:true}));}document.querySelector('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return true;})()`);
  credentials.password='';if(!found)throw Error('Login form absent');
  await delay(4000);
  const selected=await evaluate(`(async()=>{const c=await(await fetch('/bff/antiforgery')).json();const r=await fetch('/bff/organization',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':c.requestToken},body:JSON.stringify({organizationId:${JSON.stringify(fixture.orgA)}})});return r.status;})()`);
  if(selected!==200)throw Error('Synthetic organization selection failed');
  const observations=[];
  const original='/app/workspace/student/courses/'+fixture.course;
  const practice='/app/workspace/student/courses/'+assessment.course;
  const unit=practice+'/sections/'+assessment.section+'/lessons/'+assessment.lesson;
  for(const [label,url,ready] of [
    ['completed course',original,'Status: Completed'],
    ['completed lesson',original+'/sections/'+fixture.section+'/lessons/'+fixture.lesson,'Lesson state: Completed'],
    ['assessment course',practice,'Status: Active'],
    ['assessment quiz',unit+'/quiz','Start attempt (starts server timer)'],
    ['assessment assignments',unit+'/assignments','SMOKE Practice Assignment'],
    ['assessment assignment',unit+'/assignments/'+assessment.assignment,'Review final submission']]){
    await call('Page.navigate',{url:state.WebUrl+url});
    let loaded=false;
    for(let i=0;i<900;i++){if(await evaluate(`document.body.innerText.includes(${JSON.stringify(ready)})`)){loaded=true;break;}await delay(100);}
    const dom=await evaluate(`({path:location.pathname,text:document.body.innerText,buttons:[...document.querySelectorAll('button,input,textarea')].map(x=>({label:x.textContent.trim()||x.getAttribute('aria-label')||x.id,disabled:x.matches(':disabled')})),links:[...document.querySelectorAll('a')].map(x=>({text:x.textContent.trim(),href:x.getAttribute('href')}))})`);
    observations.push({label,loaded,...dom});
  }
  const result={observations,network,actions:'Only login/organization selection and page reads. No quiz start, submission, or grading.'};
  fs.writeFileSync(path.join(root,'assessment-browser-observations.json'),JSON.stringify(result,null,2));
  fs.writeFileSync(path.join(root,'assessment-browser-observations-'+Date.now()+'.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({observations:observations.map(({label,loaded,path,buttons})=>({label,loaded,path,buttons})),network,actions:result.actions}));
  await evaluate(`(async()=>{const c=await(await fetch('/bff/antiforgery')).json();await fetch('/bff/logout',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':c.requestToken},body:'{}'});})()`);
  await call('Browser.close');
}finally{ws?.close();if(chrome.exitCode===null)chrome.kill();}
