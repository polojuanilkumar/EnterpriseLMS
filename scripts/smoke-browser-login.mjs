import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

// Never record protocol payloads: they contain credentials and cookies.
const root = path.resolve(process.argv[2]);
const state = JSON.parse(fs.readFileSync(path.join(root, 'state.json')));
if (path.basename(root) !== state.Run || !/^[a-f0-9]{32}$/.test(state.Run)) throw Error('Invalid run');
const profile = path.join(root, 'browser-diagnostic-' + Date.now());
const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
let ws;
try {
  let port;
  for (let i=0; i<100; i++) {
    try { port = Number(fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]); break; } catch { await delay(100); }
  }
  if (!port) throw Error('Diagnostic browser unavailable');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  ws = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise(resolve => ws.addEventListener('open', resolve, { once: true }));
  let id=0;
  const pending=new Map();
  const network=[]; const exceptions=[];
  ws.addEventListener('message', e => {
    const m=JSON.parse(e.data);
    if (m.id) { const p=pending.get(m.id); pending.delete(m.id); if (m.error) p.reject(Error('Browser protocol error')); else p.resolve(m.result); }
    if (m.method==='Network.responseReceived') { const r=m.params.response; const u=new URL(r.url); if(u.origin===state.WebUrl && u.pathname.startsWith('/bff/')) network.push({path:u.pathname,status:r.status}); }
    if (m.method==='Runtime.exceptionThrown') exceptions.push(m.params.exceptionDetails.exception?.description?.match(/(?:NG\d+|[A-Za-z]+Error)/)?.[0] ?? 'script_exception');
  });
  const call=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});ws.send(JSON.stringify({id:key,method,params}));});
  const evaluate=async expression=>(await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true})).result.value;
  await call('Network.enable'); await call('Runtime.enable');
  await call('Page.navigate',{url:state.WebUrl+'/app/login'});
  for(let i=0;i<100;i++) {if(await evaluate('!!document.querySelector("#password")'))break;await delay(100);}
  const lines=fs.readFileSync(path.join(root,'credentials.txt'),'utf8').split(/\r?\n/);
  const prefix='Disposable synthetic identities only. Password for these five accounts: ';
  if(!lines[0].startsWith(prefix))throw Error('Unsupported credentials format');
  const credentials={email:lines.find(x=>x==='smoke-student@example.invalid'),password:lines[0].slice(prefix.length)};
  const form=await evaluate(`(()=>{const c=${JSON.stringify(credentials)};for(const k of ['email','password']){const e=document.getElementById(k);if(!e)return {found:false};e.value=c[k];e.dispatchEvent(new Event('input',{bubbles:true}));}const f=document.querySelector('form');f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return {found:true};})()`);
  credentials.password='';
  await delay(5000);
  const result=await evaluate(`({path:location.pathname,alerts:[...document.querySelectorAll('[role="alert"],[role="status"]')].map(x=>x.textContent.trim()).filter(Boolean),passwordEmpty:document.querySelector('#password')?.value===''})`);
  console.log(JSON.stringify({form,...result,network,exceptions}));
  await evaluate(`(async()=>{const r=await fetch('/bff/antiforgery');const c=await r.json();await fetch('/bff/logout',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':c.requestToken},body:'{}'});})()`);
  await call('Browser.close');
} finally { if(ws)ws.close(); if(chrome.exitCode===null)chrome.kill(); }
