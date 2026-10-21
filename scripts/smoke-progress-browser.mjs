import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';

const root=path.resolve(process.argv[2]);
const state=JSON.parse(fs.readFileSync(path.join(root,'state.json')));
if(state.Run!=='d6966d0607204702879ad62b6d352f6a'||path.basename(root)!==state.Run)throw Error('Wrong run');
const refresh=path.resolve(fs.readFileSync(path.join(root,'latest-refresh.txt'),'utf8').trim());
if(!refresh.startsWith(root+path.sep))throw Error('Wrong refresh path');
const fixture=JSON.parse(fs.readFileSync(path.join(root,'fixtures.json')));
const assessment=JSON.parse(fs.readFileSync(path.join(root,'assessment-fixture.json')));
const chrome=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(refresh,'browser-profile'),'--no-first-run','--no-default-browser-check','about:blank'],{windowsHide:true,stdio:'ignore'});
let ws;
try {
  let port;
  for(let i=0;i<100;i++){try{port=Number(fs.readFileSync(path.join(refresh,'browser-profile','DevToolsActivePort'),'utf8').split('\n')[0]);break;}catch{await delay(100);}}
  if(!port)throw Error('Browser unavailable');
  const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
  await new Promise(resolve=>ws.addEventListener('open',resolve,{once:true}));
  let id=0;const pending=new Map();const requests=[];const responses=[];
  ws.addEventListener('message',e=>{
    const m=JSON.parse(e.data);
    if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error('Browser protocol error')):p.resolve(m.result);}
    if(m.method==='Network.requestWillBeSent'){const r=m.params.request;const u=new URL(r.url);if(u.origin===state.WebUrl&&u.pathname.startsWith('/bff/'))requests.push({method:r.method,path:u.pathname});}
    if(m.method==='Network.responseReceived'){const r=m.params.response;const u=new URL(r.url);if(u.origin===state.WebUrl&&u.pathname.startsWith('/bff/'))responses.push({path:u.pathname,status:r.status});}
  });
  const call=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});ws.send(JSON.stringify({id:key,method,params}));});
  const evaluate=async expression=>{const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error('Page evaluation failed');return r.result.value;};
  await call('Network.enable');await call('Emulation.setTimezoneOverride',{timezoneId:'Asia/Kolkata'});
  await call('Page.navigate',{url:state.WebUrl+'/app/login'});
  for(let i=0;i<100;i++){if(await evaluate('!!document.querySelector("#password")'))break;await delay(100);}
  const lines=fs.readFileSync(path.join(root,'credentials.txt'),'utf8').split(/\r?\n/);
  const prefix='Disposable synthetic identities only. Password for these five accounts: ';
  if(!lines[0].startsWith(prefix))throw Error('Unexpected credential format');
  const credentials={email:'smoke-student@example.invalid',password:lines[0].slice(prefix.length)};
  const found=await evaluate(`(()=>{const c=${JSON.stringify(credentials)};for(const k of ['email','password']){const e=document.getElementById(k);if(!e)return false;e.value=c[k];e.dispatchEvent(new Event('input',{bubbles:true}));}document.querySelector('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return true;})()`);
  credentials.password='';if(!found)throw Error('Login form absent');
  await delay(4000);
  const selected=await evaluate(`(async()=>{const c=await(await fetch('/bff/antiforgery')).json();return(await fetch('/bff/organization',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':c.requestToken},body:JSON.stringify({organizationId:${JSON.stringify(fixture.orgA)}})})).status;})()`);
  if(selected!==200)throw Error('Synthetic organization selection failed');
  const original='/app/workspace/student/courses/'+fixture.course;
  const originalLesson=original+'/sections/'+fixture.section+'/lessons/'+fixture.lesson;
  const practice='/app/workspace/student/courses/'+assessment.course;
  const practiceLesson=practice+'/sections/'+assessment.section+'/lessons/'+assessment.lesson;
  const progressPaths=[original,originalLesson,practice,practiceLesson].map(p=>p.replace('/app/workspace','/bff')+'/progress');
  const read=()=>evaluate(`Promise.all(${JSON.stringify(progressPaths)}.map(async path=>{const r=await fetch(path);return {path,status:r.status,body:await r.json()};}))`);
  const before=await read();
  if(before.some(r=>r.status!==200))throw Error('Progress baseline unavailable');
  const observations=[];
  for(const [label,url] of [['completed course',original],['completed lesson',originalLesson],['Active assessment course',practice],['Active assessment lesson',practiceLesson]]){
    await call('Page.navigate',{url:state.WebUrl+url});
    let loaded=false;
    for(let i=0;i<1200;i++){if(await evaluate(`(()=>{const p=document.querySelector('section[aria-label="Stored progress"]');return !!p&&p.innerText.includes('Historical enrollment')&&!p.innerText.includes('Loading stored progress');})()`)){loaded=true;break;}await delay(100);}
    const dom=await evaluate(`(()=>{const p=document.querySelector('section[aria-label="Stored progress"]');return {path:location.pathname,text:p?.innerText??document.body.innerText,controls:[...(p?.querySelectorAll('button,input')??[])].map(x=>({tag:x.tagName,type:x.getAttribute('type'),label:x.textContent.trim()||x.id,disabled:x.matches(':disabled')}))};})()`);
    const noPercentageEditor=!dom.controls.some(x=>x.type==='number'||/Save progress|lesson-percentage/.test(x.label));
    const completionLabels=dom.controls.filter(x=>/Start lesson|Mark lesson complete|Confirm lesson completion|Complete course enrollment|Confirm completion/.test(x.label));
    const observation={label,loaded,...dom,noPercentageEditor,completionLabels};
    if(label==='Active assessment lesson'){
      const stored=before[3].body.lesson;
      observation.eligibleAction=stored===null?'Start lesson':stored.isCompleted?'None':'Mark lesson complete';
      if(stored&&!stored.isCompleted){
        const opened=await evaluate(`(()=>{const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Mark lesson complete');if(!b||b.disabled)return false;b.click();return true;})()`);
        await delay(200);
        observation.confirmation=await evaluate(`({text:document.querySelector('section[aria-label="Stored progress"]').innerText,buttons:[...document.querySelectorAll('button')].map(x=>x.textContent.trim())})`);
        observation.confirmationOpened=opened&&observation.confirmation.buttons.includes('Confirm lesson completion');
        await evaluate(`(()=>{const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Cancel');if(b)b.click();})()`);
      }else observation.confirmationCheck='Unexecuted: not eligible; Start lesson was left unclicked.';
    }
    const screenshot=await call('Page.captureScreenshot',{format:'png'});
    fs.writeFileSync(path.join(refresh,label.replaceAll(' ','-')+'.png'),Buffer.from(screenshot.data,'base64'));
    observations.push(observation);console.log(JSON.stringify({label,loaded,noPercentageEditor,controls:dom.controls,confirmationOpened:observation.confirmationOpened,eligibleAction:observation.eligibleAction}));
  }
  const after=await read();
  const progressMutations=requests.filter(r=>r.method!=='GET'&&r.method!=='HEAD'&&(/\/progress(?:\/|$)/.test(r.path)||/\/enrollments\/[^/]+\/complete$/.test(r.path)));
  const unchanged=JSON.stringify(before)===JSON.stringify(after);
  const result={observedAt:new Date().toISOString(),timezone:'Asia/Kolkata',observations,before,after,progressMutations,progressUnchanged:unchanged,requests,responses,actions:'Login, organization selection, GET navigation, and opening/cancelling confirmation only where already eligible. No Start, completion, quiz or assignment mutation.'};
  fs.writeFileSync(path.join(refresh,'browser-progress-observations.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({progressUnchanged:unchanged,progressMutations,report:path.join(refresh,'browser-progress-observations.json')}));
  await evaluate(`(async()=>{const c=await(await fetch('/bff/antiforgery')).json();await fetch('/bff/logout',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':c.requestToken},body:'{}'});})()`);
  await call('Browser.close');
  if(!unchanged||progressMutations.length||observations.some(x=>!x.loaded||!x.noPercentageEditor))throw Error('Progress browser verification failed; inspect saved observations.');
}finally{ws?.close();if(chrome.exitCode===null)chrome.kill();}
