import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { WebSocketServer } from 'ws';
import { spawn } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir, copyFile, readdir, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { ACTIONS, PLUGIN_ID } from '../src/model.mjs';

test('bundle officiel : protocole Stream Deck 6.8, huit actions, molette, stockage global et reconnexion', {timeout:20000}, async context=>{
  const root=fileURLToPath(new URL('../../../',import.meta.url));
  const output=path.join(root,`artifacts/streamdeck/${PLUGIN_ID}.sdPlugin`);
  const fixture=path.join(root,'artifacts/validation/streamdeck/sdk-'+randomUUID());
  await mkdir(fixture,{recursive:true});await copyFile(path.join(output,'manifest.json'),path.join(fixture,'manifest.json'));
  const token=randomBytes(32).toString('hex'),commands=[];
  const state={running:true,canControl:true,targetSeconds:0,currentDelayMilliseconds:0,phase:'Live',
    ingestConnected:true,protectionSeconds:60,outputs:[{ready:true,reconnecting:false}]};
  let online=true;
  const api=createServer(async(request,response)=>{
    if(!online){response.writeHead(503);response.end();return;}
    if(request.headers.authorization!==`Bearer ${token}`){response.writeHead(401);response.end();return;}
    response.setHeader('Content-Type','application/json');
    if(request.url==='/api/state'){response.end(JSON.stringify(state));return;}
    let body='';for await(const chunk of request)body+=chunk;
    const command=JSON.parse(body);commands.push(command);
    state.targetSeconds=command.action==='protect'?60:command.action==='set'?command.seconds:Math.max(0,Math.min(300,state.targetSeconds+command.seconds));
    response.end(JSON.stringify({accepted:true,targetSeconds:state.targetSeconds}));
  });
  await new Promise(resolve=>api.listen(0,'127.0.0.1',resolve));
  const host=new WebSocketServer({host:'127.0.0.1',port:0});await new Promise(resolve=>host.once('listening',resolve));
  let socket,registered=false,globals={},errorOutput='';
  const messages=[],settings=new Map();
  host.on('connection',connection=>{socket=connection;connection.on('message',data=>{
    const message=JSON.parse(data.toString());messages.push(message);
    if(message.event==='registerPlugin')registered=true;
    if(message.event==='getGlobalSettings')connection.send(JSON.stringify({event:'didReceiveGlobalSettings',payload:{settings:globals}}));
    if(message.event==='setGlobalSettings'){globals=message.payload;connection.send(JSON.stringify({event:'didReceiveGlobalSettings',payload:{settings:globals}}));}
    if(message.event==='setSettings')settings.set(message.context,message.payload);
  });});
  const manifest=JSON.parse(await readFile(path.join(output,'manifest.json'),'utf8'));
  const info={application:{version:'6.8.0.21252',platform:'windows',platformVersion:'10.0',language:'fr',font:'Segoe UI'},
    plugin:{uuid:PLUGIN_ID,version:manifest.Version},devicePixelRatio:1,colors:{},devices:[{id:'test-device',name:'Test',type:7,size:{columns:4,rows:2}}]};
  const child=spawn(process.execPath,[path.join(output,'bin/plugin.js'),'-port',String(host.address().port),'-pluginUUID',PLUGIN_ID,
    '-registerEvent','registerPlugin','-info',JSON.stringify(info)],{cwd:fixture,windowsHide:true,stdio:['ignore','pipe','pipe']});
  child.stderr.on('data',data=>{errorOutput+=data.toString();});
  context.after(async()=>{
    child.kill();for(const connection of host.clients)connection.terminate();
    await new Promise(resolve=>host.close(resolve));api.closeAllConnections();await new Promise(resolve=>api.close(resolve));
  });
  async function until(predicate,label){for(let i=0;i<400;i++){if(predicate())return;if(child.exitCode!==null)throw new Error('Le bundle a quitté : '+errorOutput);await delay(10);}throw new Error('Délai de test dépassé : '+label);}
  const send=(event,kind,payload={})=>socket.send(JSON.stringify({event,action:`${PLUGIN_ID}.${kind}`,context:kind,device:'test-device',payload}));
  const image=kind=>messages.filter(message=>message.event==='setImage'&&message.context===kind).at(-1)?.payload.image??'';
  const svg=kind=>Buffer.from(image(kind).split(',')[1]??'','base64').toString('utf8');
  await until(()=>registered,'enregistrement SDK');
  await until(()=>messages.some(message=>message.event==='getGlobalSettings'),'réglages globaux');
  for(const [kind] of ACTIONS)send('willAppear',kind,{controller:kind==='dial'?'Encoder':'Keypad',settings:{},state:0,isInMultiAction:false,coordinates:{column:0,row:0}});
  send('propertyInspectorDidAppear','preset');
  send('sendToPlugin','preset',{type:'connect',link:`http://127.0.0.1:${api.address().port}/dock#${token}`});
  await until(()=>globals.token===token&&svg('protection').includes('60s'),'connexion privée et rendu des touches');
  assert.equal(messages.filter(message=>message.event==='setGlobalSettings').length,1);
  assert.ok(messages.filter(message=>message.event==='setSettings').every(message=>!JSON.stringify(message).includes(token)));
  send('sendToPlugin','preset',{type:'settings',values:{seconds:45}});
  await until(()=>settings.get('preset')?.seconds===45,'réglage preset');
  send('keyDown','preset',{settings:{seconds:45},controller:'Keypad'});await until(()=>state.targetSeconds===45,'preset');
  send('keyDown','protection',{settings:{},controller:'Keypad'});await until(()=>state.targetSeconds===60,'protection');
  send('keyDown','direct',{settings:{},controller:'Keypad'});await until(()=>state.targetSeconds===0,'direct');
  send('keyDown','increase',{settings:{step:10},controller:'Keypad'});await until(()=>state.targetSeconds===10,'ajout');
  send('keyDown','decrease',{settings:{step:5},controller:'Keypad'});await until(()=>state.targetSeconds===5,'retrait');
  send('dialRotate','dial',{settings:{step:5},ticks:2,pressed:false,controller:'Encoder'});await until(()=>state.targetSeconds===15,'molette');
  send('dialDown','dial',{settings:{},controller:'Encoder'});await until(()=>state.targetSeconds===60,'pression molette');
  state.targetSeconds=30;state.currentDelayMilliseconds=19400;
  await until(()=>svg('delay').includes('00:19')&&svg('delay').includes('CIBLE 00:30'),'mesure réelle et modification externe');
  const count=commands.length;
  send('keyDown','delay',{settings:{},controller:'Keypad'});send('keyDown','status',{settings:{},controller:'Keypad'});await delay(50);
  assert.equal(commands.length,count);
  assert.ok(messages.some(message=>message.event==='setFeedback'&&message.context==='dial'));
  online=false;await until(()=>svg('delay').includes('HORS LIGNE'),'déconnexion');
  send('keyDown','protection',{settings:{},controller:'Keypad'});await delay(50);online=true;
  await until(()=>svg('delay').includes('00:19'),'reconnexion');assert.equal(commands.length,count);
  assert.ok(!JSON.stringify(messages.filter(message=>message.event==='sendToPropertyInspector'||message.event==='setSettings')).includes(token));
  for(const [kind] of ACTIONS)send('willDisappear',kind,{controller:kind==='dial'?'Encoder':'Keypad',settings:{}});
  await delay(40);
  const logs=await readdir(path.join(fixture,'logs')).catch(()=>[]);
  for(const file of logs){const contents=await readFile(path.join(fixture,'logs',file),'utf8');assert.ok(!contents.includes(token));}
  assert.equal(errorOutput,'');
});
