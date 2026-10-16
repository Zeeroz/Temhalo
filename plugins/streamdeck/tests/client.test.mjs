import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { TemhaloClient } from '../src/client.mjs';

const initial = () => ({running:true,canControl:true,targetSeconds:0,currentDelayMilliseconds:0,phase:'Live',
  ingestConnected:true,protectionSeconds:60,outputs:[{ready:true,reconnecting:false}]});
async function fixture(context, options={}) {
  const token=randomBytes(32).toString('hex'), runtime={state:initial(),requests:[],postDelay:0,postMode:'ok',stateMode:'ok',active:0,maxActive:0};
  const server=createServer(async(request,response)=>{
    runtime.requests.push({method:request.method,url:request.url,authorization:request.headers.authorization});
    if(request.headers.authorization!==`Bearer ${token}`){response.writeHead(401);response.end();return;}
    if(request.url==='/api/state'){
      if(runtime.stateMode==='unauthorized'){response.writeHead(401);response.end();return;}
      if(runtime.stateMode==='offline'){response.writeHead(503);response.end();return;}
      if(runtime.stateMode==='redirect'){response.writeHead(302,{Location:`http://127.0.0.1:${server.address().port}/stolen`});response.end();return;}
      if(runtime.stateMode==='oversize'){response.writeHead(200,{'Content-Type':'application/json'});response.end(' '.repeat(70000));return;}
      response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(runtime.state));return;
    }
    if(request.url==='/api/delay'){
      runtime.active++;runtime.maxActive=Math.max(runtime.maxActive,runtime.active);
      let text='';for await(const chunk of request)text+=chunk;
      const command=JSON.parse(text);await delay(runtime.postDelay);
      if(runtime.postMode==='dropped'){runtime.active--;request.socket.destroy();return;}
      if(runtime.postMode==='busy'){runtime.active--;response.writeHead(409,{'Content-Type':'application/json'});response.end(JSON.stringify({accepted:false,targetSeconds:runtime.state.targetSeconds}));return;}
      runtime.state.targetSeconds=command.action==='protect'?60:command.action==='set'?command.seconds:Math.max(0,Math.min(300,runtime.state.targetSeconds+command.seconds));
      runtime.active--;response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify({accepted:true,targetSeconds:runtime.state.targetSeconds}));return;
    }
    response.writeHead(404);response.end();
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const connection={port:server.address().port,token},client=new TemhaloClient(options);client.configure(connection);
  context.after(async()=>{client.dispose();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));});
  return {runtime,client,connection};
}
test('HTTP local authentifié : protection, direct et ajustement borné',async context=>{
  const {client,runtime}=await fixture(context);await client.refresh();
  await client.send({action:'protect'});assert.equal(runtime.state.targetSeconds,60);
  await client.send({action:'set',seconds:0});assert.equal(runtime.state.targetSeconds,0);
  await client.send({action:'adjust',seconds:-10});assert.equal(runtime.state.targetSeconds,0);
  assert.ok(runtime.requests.every(request=>request.authorization.startsWith('Bearer ')));
  assert.ok(runtime.requests.every(request=>!request.url.includes('#')));
});
test('les pressions rapides restent sérialisées et ne perdent pas les incréments',async context=>{
  const {client,runtime}=await fixture(context);runtime.postDelay=15;await client.refresh();
  await Promise.all(Array.from({length:6},()=>client.send({action:'adjust',seconds:10})));
  assert.equal(runtime.state.targetSeconds,60);assert.equal(runtime.maxActive,1);
});
test('réponse POST perdue : arrêt de la file et aucun rejeu à la reconnexion',async context=>{
  const {client,runtime}=await fixture(context);runtime.postDelay=20;runtime.postMode='dropped';await client.refresh();
  const results=await Promise.allSettled(Array.from({length:3},()=>client.send({action:'adjust',seconds:10})));
  assert.ok(results.every(result=>result.status==='rejected'));
  assert.equal(runtime.requests.filter(request=>request.method==='POST').length,1);
  runtime.postMode='ok';await client.refresh();await delay(20);
  assert.equal(runtime.requests.filter(request=>request.method==='POST').length,1);assert.equal(client.status,'ready');
});
test('401, redirection et réponse trop grosse ne peuvent déclencher de commande',async context=>{
  const {client,runtime,connection}=await fixture(context);
  for(const mode of ['unauthorized','redirect','oversize']){
    client.configure(connection);runtime.stateMode=mode;await client.refresh();assert.equal(client.state,null);
    await assert.rejects(client.send({action:'protect'}));
  }
  assert.equal(runtime.requests.filter(request=>request.method==='POST'||request.url==='/stolen').length,0);
});
test('Temhalo occupé : aucune commande locale ; refus 409 sans répétition',async context=>{
  const {client,runtime}=await fixture(context);runtime.state.canControl=false;
  await assert.rejects(client.send({action:'protect'}));assert.equal(runtime.requests.filter(request=>request.method==='POST').length,0);
  runtime.state.canControl=true;runtime.postMode='busy';await assert.rejects(client.send({action:'protect'}));
  assert.equal(runtime.requests.filter(request=>request.method==='POST').length,1);
});
test('les commandes qui expirent dans la file sont abandonnées',async context=>{
  let now=0;const {client,runtime}=await fixture(context,{now:()=>now});runtime.postDelay=25;await client.refresh();
  const commands=Promise.allSettled([client.send({action:'adjust',seconds:10}),client.send({action:'adjust',seconds:10})]);
  await delay(10);now=2000;const results=await commands;
  assert.equal(results[0].status,'fulfilled');assert.equal(results[1].status,'rejected');assert.equal(runtime.state.targetSeconds,10);
});
test('un seul poller suit les changements externes et s’arrête sans touche visible',async context=>{
  const {client,runtime}=await fixture(context,{interval:20});client.setActive(true);
  for(let i=0;i<30&&client.state===null;i++)await delay(10);
  runtime.state.targetSeconds=42;
  for(let i=0;i<30&&client.state?.targetSeconds!==42;i++)await delay(10);
  assert.equal(client.state.targetSeconds,42);client.setActive(false);await delay(30);
  const count=runtime.requests.length;await delay(50);assert.equal(runtime.requests.length,count);
});
