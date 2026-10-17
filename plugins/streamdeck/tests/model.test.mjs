import test from 'node:test';
import assert from 'node:assert/strict';
import { actionSettings, clock, commandFor, readState, streamDeckVersion, validateCommand } from '../src/model.mjs';
import { COLORS, keySvg, keyView } from '../src/render.mjs';

export const state = (extra = {}) => ({ running:true,canControl:true,targetSeconds:30,currentDelayMilliseconds:15230,
  phase:'IncreasingPause',ingestConnected:true,protectionSeconds:60,outputs:[{ready:true,reconnecting:false}],...extra });

test('la version du plugin suit le seul numéro choisi pour Temhalo',()=>{
  assert.equal(streamDeckVersion('0.7.0-preview.3'),'0.7.0.3');assert.equal(streamDeckVersion('0.7.0'),'0.7.0.65535');
  for(const value of ['v0.7.0','01.2.3','1.2.3-preview.65535','65536.0.0','1.2.3\n'])assert.throws(()=>streamDeckVersion(value));
});

test('commandes distinctes et limites du délai et des pas', () => {
  assert.deepEqual(commandFor('protection',{}),{action:'protect'});
  assert.deepEqual(commandFor('direct',{}),{action:'set',seconds:0});
  assert.deepEqual(commandFor('preset',{seconds:300}),{action:'set',seconds:300});
  assert.deepEqual(commandFor('increase',{}),{action:'adjust',seconds:10});
  assert.deepEqual(commandFor('decrease',{step:25}),{action:'adjust',seconds:-25});
  assert.deepEqual(commandFor('dial',{step:5},-3),{action:'adjust',seconds:-15});
  assert.deepEqual(commandFor('dial',{step:60},100),{action:'adjust',seconds:300});
  assert.equal(commandFor('dial',{},0),null);
  assert.equal(commandFor('delay',{}),null);assert.equal(commandFor('status',{}),null);
  for(const value of [-1,301,1.2,'30',NaN,null])assert.throws(()=>commandFor('preset',{seconds:value}));
  assert.throws(()=>actionSettings('dial',{step:0}));assert.throws(()=>commandFor('unknown',{}));
});
test('le snapshot retient uniquement les données utiles et refuse les valeurs ambiguës', () => {
  const result=readState(state({secret:'not retained',outputs:[{name:'not retained',ready:true,reconnecting:false}]}));
  assert.equal('secret' in result,false);assert.equal('name' in result.outputs[0],false);
  for(const delta of [{targetSeconds:301},{currentDelayMilliseconds:-1},{canControl:'true'},{outputs:[{ready:true}]}])assert.throws(()=>readState(state(delta)));
});
test('mesure absente, cible et construction du délai restent distinctes', () => {
  const client={configured:true,state:state({currentDelayMilliseconds:null})};
  assert.equal(keyView('delay',{},client).value,'—');
  client.state=state();
  assert.equal(keyView('delay',{},client).value,'00:15');
  assert.equal(keyView('delay',{},client).bottom,'CIBLE 00:30');
  client.state=state({targetSeconds:60});
  assert.equal(keyView('protection',{},client).bottom,'EN CONSTRUCTION');
  assert.equal(keyView('protection',{},client).color,COLORS.warning);
  assert.equal(clock(0),'00:00');assert.equal(clock(59999),'00:59');assert.equal(clock(300000),'05:00');
});
test('indisponibilité et chaînes SVG sans injection',()=>{
  const view=keyView('delay',{}, {configured:true,state:null});
  assert.equal(view.bottom,'HORS LIGNE');assert.equal(view.value,'—');
  assert.ok(keySvg({...view,top:'<script>',value:'&',bottom:'"x"'}).includes('&lt;script&gt;'));
  assert.ok(!keySvg({...view,top:'<script>'}).includes('<script>'));
});

test('la touche relais demande un état explicite et reste bloquée pendant les transitions', () => {
  for (const [phase, canStart, canStop, expected] of [['Stopped', true, false, 'relay-start'], ['Running', false, true, 'relay-stop']]) {
    const current = readState(state({ relay: { phase, canStart, canStop } }));
    assert.deepEqual(commandFor('relay', {}, 1, current), { action: expected });
    const view = keyView('relay', {}, { state: current });
    assert.equal(view.value, phase === 'Stopped' ? 'DÉMARRER' : 'ARRÊTER');
    assert.ok(keySvg(view, 72).includes('width="72"'));
  }
  for (const phase of ['Starting', 'Stopping', 'Stopped', 'Running']) {
    const current = readState(state({ relay: { phase, canStart: false, canStop: false } }));
    assert.throws(() => commandFor('relay', {}, 1, current));
    assert.equal(keyView('relay', {}, { state: current }).color, COLORS.warning);
  }
  const legacy = readState(state());
  assert.throws(() => commandFor('relay', {}, 1, legacy));
  assert.equal(keyView('relay', {}, { state: legacy }).value, 'METTRE À JOUR');
  for (const relay of [{ phase: 'Starting', canStart: true, canStop: false }, { phase: 'Running', canStart: false }, { phase: 'unknown', canStart: false, canStop: false }])
    assert.throws(() => readState(state({ relay })));
  assert.deepEqual(validateCommand({ action: 'relay-start' }), { action: 'relay-start' });
  assert.throws(() => validateCommand({ action: 'relay-stop', seconds: 0 }));
});
