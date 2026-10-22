// Transport-free submission state machine. Production supplies its existing
// authenticated operator transport; tests supply an isolated fake backend.
import {open,rename,stat} from 'node:fs/promises';
import {constants} from 'node:fs';
import {randomUUID} from 'node:crypto';
import path from 'node:path';

const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const phases=new Set(['reserved','requested','approved','recovered']);
async function syncDirectory(directory){const handle=await open(directory,constants.O_RDONLY|constants.O_DIRECTORY);try{await handle.sync()}finally{await handle.close()}}
async function writeJournal(file,value,initial=false){
 const temporary=initial?file:file+'.'+randomUUID()+'.tmp';
 const handle=await open(temporary,constants.O_WRONLY|constants.O_CREAT|constants.O_EXCL|constants.O_NOFOLLOW,0o600);
 try{await handle.writeFile(JSON.stringify(value,null,2)+'\n');await handle.sync()}finally{await handle.close()}
 if(!initial)await rename(temporary,file);
 await syncDirectory(path.dirname(file));
}
async function readJournal(file,definition,digest){
 let handle;try{handle=await open(file,constants.O_RDONLY|constants.O_NOFOLLOW)}catch(error){if(error.code==='ENOENT')return null;throw error}
 try{
  const info=await handle.stat();if(!info.isFile()||info.size>65536)throw new Error('Invalid submission file; operator review required');
  const value=JSON.parse(await handle.readFile('utf8'));
  if(!value||value.schema!=='voidwright.depth-submission.v1'||value.definition!==definition||value.plan_sha256!==digest||!phases.has(value.phase)
   ||(value.run_id!==undefined&&!uuid.test(value.run_id))||(value.phase!=='reserved'&&!uuid.test(value.run_id??'')))throw new Error('Invalid submission binding; operator review required');
  return value;
 }finally{await handle.close()}
}

export async function submitOnce({api,root,definition,digest}){
 if(!/^[0-9a-f]{64}$/.test(digest)||!/^voidwright\.depth-navigation\.[a-z0-9-]+$/.test(definition))throw new Error('Invalid fixed plan identity');
 const parent=path.dirname(root),directory=await stat(parent);
 if(!directory.isDirectory()||(directory.mode&0o077)!==0)throw new Error('Submission parent must be private');
 const file=path.join(parent,'submission-'+digest+'.json');
 const journal=await readJournal(file,definition,digest);
 if(journal)return {status:journal.run_id?'already-submitted':'review-required',submission:journal,note:'Last durable submission state, not current service status. No automatic replay.'};
 const existing=await api('GET','/v1/control/experiments');
 const installed=existing.definitions.find(x=>x.id===definition);
 if(!installed||installed.repository_root!==root||installed.executable!==path.join(root,'run_fixed.py')||installed.args.length!==0||Object.keys(installed.environment).length!==0||installed.timeout_seconds!==1320)throw new Error('Installed fixed definition differs from reviewed binding');
 const matches=existing.runs.filter(x=>x.definition_id===definition&&typeof x.reason==='string'&&x.reason.endsWith('plan '+digest));
 if(matches.length>1)throw new Error('Duplicate backend plan history requires operator review');
 if(matches.length===1){
  if(!uuid.test(matches[0].id))throw new Error('Invalid backend run identity');
  const recovered={schema:'voidwright.depth-submission.v1',definition,plan_sha256:digest,phase:'recovered',run_id:matches[0].id,created_at:new Date().toISOString()};
  await writeJournal(file,recovered,true);
  return {status:'already-submitted',submission:recovered,note:'Recovered matching backend history after missing local journal; no new request or approval.'};
 }
 if(existing.runs.some(x=>x.definition_id===definition&&['awaiting_approval','queued','running'].includes(x.status)))throw new Error('A matching definition already has pending work');
 const value={schema:'voidwright.depth-submission.v1',definition,plan_sha256:digest,phase:'reserved',created_at:new Date().toISOString()};
 await writeJournal(file,value,true);
 const requested=await api('POST','/v1/control/experiments',{definition_id:definition,requested_by:'Blake via Codex',reason:'Human explicitly authorized mpai/GPU offline experiment; plan '+digest});
 if(!uuid.test(requested.run?.id??'')||requested.run?.definition_id!==definition||requested.run?.status!=='awaiting_approval'||!Number.isFinite(Date.parse(requested.run?.updated_at)))throw new Error('Unexpected request acknowledgement; inspect backend before further action');
 value.run_id=requested.run.id;value.phase='requested';await writeJournal(file,value);
 const decision=await api('POST',`/v1/control/experiments/${value.run_id}/decision`,{approved:true,expected_updated_at:requested.run.updated_at,reason:'Blake directly authorized the bounded offline GPU work; plan '+digest});
 if(decision.run?.id!==value.run_id||decision.run?.definition_id!==definition)throw new Error('Unexpected decision acknowledgement; inspect exact request');
 value.phase='approved';await writeJournal(file,value);
 return {plan_sha256:digest,run:decision.run};
}
