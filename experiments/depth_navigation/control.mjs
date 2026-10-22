// Operator-side preview/request/inspect. The training child receives no key.
import {readFile, writeFile} from 'node:fs/promises';
import {createHmac,createHash} from 'node:crypto';
import http from 'node:http';
import path from 'node:path';
import {submitOnce} from './control_state.mjs';

const root=path.dirname(new URL(import.meta.url).pathname);
const action=process.argv[2]??'preview';
async function api(method,route,value){
 const key=Buffer.from((await readFile(path.join(process.env.HOME,'.config/agent-runtime/wake-hmac.key'),'utf8')).trim());
 const timestamp=new Date().toISOString();const body=value===undefined?Buffer.alloc(0):Buffer.from(JSON.stringify(value));
 const sig=createHmac('sha256',key).update(timestamp+'\n'+method+'\n'+route+'\n').update(body).digest('hex');
 return await new Promise((resolve,reject)=>{const req=http.request({socketPath:`/run/user/${process.getuid()}/agent-runtime/scheduler.sock`,method,path:route,headers:{'content-type':'application/json','content-length':body.length,'x-agent-runtime-timestamp':timestamp,'x-agent-runtime-signature':'sha256='+sig},timeout:10000},res=>{const chunks=[];let bytes=0;res.on('data',x=>{bytes+=x.length;if(bytes>4*1024*1024){req.destroy(new Error('Response budget exceeded'));return}chunks.push(x)});res.on('end',()=>{let v;try{v=JSON.parse(Buffer.concat(chunks))}catch(e){reject(e);return}if(res.statusCode<200||res.statusCode>=300){reject(new Error(`Control HTTP ${res.statusCode}: ${JSON.stringify(v)}`));return}resolve(v)})});req.on('timeout',()=>req.destroy(new Error('Control timeout')));req.on('error',reject);req.end(body)})
}
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const protocol=JSON.parse(await readFile(path.join(root,'protocol.json'),'utf8'));
const manifest=JSON.parse(await readFile(path.join(root,'manifest.json'),'utf8'));
const definition=manifest.definition;
if(!/^voidwright\.depth-navigation\.[a-z0-9-]+$/.test(definition))throw new Error('Invalid fixed experiment definition');
const snapshot={schema:'voidwright.depth-experiment-plan.v1',definition,source_root:root,manifest_sha256:hash(await readFile(path.join(root,'manifest.json'))),protocol,limits:{seconds:1250,host_memory:'6G',cpu_cores:4,gpu_fraction:.4},live_control:false};
const digest=hash(JSON.stringify(snapshot));
if(action==='preview')console.log(JSON.stringify({...snapshot,plan_sha256:digest,execution_requested:false},null,2));
else if(action==='inspect'){
 const all=await api('GET','/v1/control/experiments');
 console.log(JSON.stringify({definitions:all.definitions.filter(x=>x.id===definition),runs:all.runs.filter(x=>x.definition_id===definition)},null,2));
}else if(action==='execute'){
 if(process.argv[3]!==digest)throw new Error('Exact current preview plan SHA256 required');
 for(const [name,expected] of Object.entries(manifest.files)){if(path.basename(name)!==name||hash(await readFile(path.join(root,name)))!==expected)throw new Error('Source changed: '+name)}
 console.log(JSON.stringify(await submitOnce({api,root,definition,digest}),null,2));
}else if(action==='cancel'){
 const id=process.argv[3];if(!/^[0-9a-f-]{36}$/.test(id??''))throw new Error('Exact experiment UUID required');
 const all=await api('GET','/v1/control/experiments');const run=all.runs.find(x=>x.id===id&&x.definition_id===definition);
 if(!run)throw new Error('No matching owned experiment');
 if(['completed','failed','denied','recovery_required'].includes(run.status))console.log(JSON.stringify({id,status:'already-terminal',manager_status:run.status}));
 else if(run.status==='awaiting_approval'){
  const decision=await api('POST',`/v1/control/experiments/${id}/decision`,{approved:false,expected_updated_at:run.updated_at,reason:'Operator cancelled this offline experiment before approval'});
  console.log(JSON.stringify({id,status:'denied',run:decision.run}));
 }else{
  const marker=path.join(path.dirname(root),'cancel-'+id+'.json');
  try{await writeFile(marker,JSON.stringify({id,requested_at:new Date().toISOString()})+'\n',{flag:'wx',mode:0o600})}catch(error){if(error.code!=='EEXIST')throw error}
  let scopeStopStatus=null;
  if(manifest.runtime_scope==='lease-pid-v1'&&run.status==='running'){
   const {spawnSync}=await import('node:child_process');const stopped=spawnSync('systemctl',['--user','stop','voidwright-depth-'+id+'.scope'],{encoding:'utf8',timeout:30000,env:{...process.env,XDG_RUNTIME_DIR:'/run/user/'+process.getuid(),DBUS_SESSION_BUS_ADDRESS:'unix:path=/run/user/'+process.getuid()+'/bus'}});scopeStopStatus=stopped.status;
  }
  console.log(JSON.stringify({schema:'voidwright.depth-cancel.v1',id,status:'cancel-requested',scope_stop_status:scopeStopStatus,note:'Marker prevents queued startup; direct-PID scope is stopped when running. Inspect exact terminal state and lease release.'}));
 }
}else throw new Error('Use preview, execute HASH, inspect or cancel UUID');
