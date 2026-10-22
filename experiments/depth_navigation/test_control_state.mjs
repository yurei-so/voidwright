import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,rm,readFile,writeFile,unlink,symlink} from 'node:fs/promises';
import os from 'node:os';import path from 'node:path';
import {submitOnce} from './control_state.mjs';

async function fixture(t){
 const directory=await mkdtemp(path.join(os.tmpdir(),'voidwright-submit-test-'));t.after(()=>rm(directory,{recursive:true,force:true}));
 const root=path.join(directory,'source');await mkdir(root,{mode:0o700});const definition='voidwright.depth-navigation.fixture-test',digest='a'.repeat(64);
 const definitions=[{id:definition,repository_root:root,executable:path.join(root,'run_fixed.py'),args:[],environment:{},timeout_seconds:1320}];
 const calls=[],runs=[];let failure=null;
 const api=async(method,route,body)=>{
  calls.push({method,route,body});
  if(method==='GET')return {definitions,runs};
  if(route==='/v1/control/experiments'){
   const run={id:'00000000-0000-4000-8000-'+String(runs.length+1).padStart(12,'0'),definition_id:body.definition_id,
    reason:body.reason,status:'awaiting_approval',updated_at:new Date().toISOString()};runs.push(run);
   if(failure==='request-ack-lost')throw new Error('request-ack-lost');return {run:{...run}};
  }
  if(failure==='decision-ack-lost')throw new Error('decision-ack-lost');
  const run=runs[0];assert.equal(body.expected_updated_at,run.updated_at);run.status='queued';return {run:{...run}};
 };
 return {api,root,definition,digest,calls,runs,definitions,file:path.join(directory,'submission-'+digest+'.json'),fail:value=>failure=value};
}

test('same exact plan requests and approves once across repeated invocations',async t=>{
 const f=await fixture(t);const first=await submitOnce(f);const second=await submitOnce(f);
 assert.equal(first.run.id,second.submission.run_id);assert.equal(f.runs.length,1);assert.equal(second.status,'already-submitted');
 assert.equal(f.calls.filter(x=>x.method==='POST').length,2);
});
test('lost local journal recovers terminal backend history without a new job',async t=>{
 const f=await fixture(t);await submitOnce(f);await unlink(f.file);f.runs[0].status='completed';const count=f.calls.length;
 const repeated=await submitOnce(f);assert.equal(repeated.submission.phase,'recovered');assert.equal(f.runs.length,1);
 assert.equal(f.calls.slice(count).filter(x=>x.method==='POST').length,0);
});
test('request acknowledgement loss leaves a reservation and never replays',async t=>{
 const f=await fixture(t);f.fail('request-ack-lost');await assert.rejects(submitOnce(f),/request-ack-lost/);
 const repeated=await submitOnce(f);assert.equal(repeated.status,'review-required');assert.equal(f.runs.length,1);
});
test('decision acknowledgement loss preserves the exact requested identity',async t=>{
 const f=await fixture(t);f.fail('decision-ack-lost');await assert.rejects(submitOnce(f),/decision-ack-lost/);
 const repeated=await submitOnce(f);assert.equal(repeated.submission.phase,'requested');assert.equal(repeated.submission.run_id,f.runs[0].id);assert.equal(f.runs.length,1);
});
test('changed installed root is rejected before mutation',async t=>{
 const f=await fixture(t);f.definitions[0].repository_root='/other';await assert.rejects(submitOnce(f),/binding/);assert.equal(f.runs.length,0);
});
test('corrupt or cross-plan local record fails closed',async t=>{
 const f=await fixture(t);await writeFile(f.file,JSON.stringify({schema:'wrong'}));await assert.rejects(submitOnce(f),/binding/);assert.equal(f.calls.length,0);
});
test('symlink journal is not followed',async t=>{
 const f=await fixture(t);const other=path.join(path.dirname(f.file),'other');await writeFile(other,'{}');await symlink(other,f.file);
 await assert.rejects(submitOnce(f));assert.equal(f.calls.length,0);
});
test('parallel submissions cannot both request a job',async t=>{
 const f=await fixture(t);const results=await Promise.allSettled([submitOnce(f),submitOnce(f)]);
 assert.ok(results.some(x=>x.status==='fulfilled'));assert.equal(f.runs.length,1);
 assert.equal(f.calls.filter(x=>x.method==='POST'&&x.route==='/v1/control/experiments').length,1);
});
test('unknown interrupted reservation is not treated as permission to retry',async t=>{
 const f=await fixture(t);await writeFile(f.file,JSON.stringify({schema:'voidwright.depth-submission.v1',definition:f.definition,plan_sha256:f.digest,phase:'reserved'}));
 const result=await submitOnce(f);assert.equal(result.status,'review-required');assert.equal(f.calls.length,0);
});
test('malformed approved run identity is not accepted as a valid journal',async t=>{
 const f=await fixture(t);await writeFile(f.file,JSON.stringify({schema:'voidwright.depth-submission.v1',definition:f.definition,plan_sha256:f.digest,phase:'approved',run_id:'rounded-or-foreign'}));
 await assert.rejects(submitOnce(f),/binding/);assert.equal(f.calls.length,0);
});
