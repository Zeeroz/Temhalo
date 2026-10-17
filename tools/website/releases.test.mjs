import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import {fetchReleases, parseVersion, renderRelease, repository, selectRelease} from './releases.mjs';

function release(version, overrides = {}) {
  const tag = `v${version}`;
  const assets = [`Temhalo-${version}-Setup-x64.exe`, `Temhalo-${version}-win-x64.zip`]
    .flatMap(name => [name, `${name}.sha256`])
    .map(name => ({name, state: 'uploaded', size: 1234, digest: `sha256:${'a'.repeat(64)}`,
      browser_download_url: `${repository}/releases/download/${tag}/${name}`}));
  return {tag_name: tag, draft: false, prerelease: version.includes('-preview.'),
    published_at: '2026-10-17T12:00:00Z', html_url: `${repository}/releases/tag/${tag}`, assets, ...overrides};
}

test('a newer preview is offered even when a stable release exists', () => {
  assert.equal(selectRelease([release('0.7.0'), release('0.8.0-preview.1')]).version, '0.8.0-preview.1');
});
test('preview 10 follows preview 9 numerically, regardless of API order', () => {
  assert.equal(selectRelease([release('0.7.0-preview.9'), release('0.7.0-preview.10')]).version, '0.7.0-preview.10');
});
test('stable supersedes its previews, including a later-published preview', () => {
  assert.equal(selectRelease([release('0.7.0-preview.99', {published_at: '2026-10-18T12:00:00Z'}), release('0.7.0')]).version, '0.7.0');
});
test('publishing an older maintenance release never downgrades the version', () => {
  assert.equal(selectRelease([release('0.6.9', {published_at: '2026-10-18T12:00:00Z'}), release('0.7.0-preview.6')]).version, '0.7.0-preview.6');
});
test('drafts and unpublished entries are excluded', () => {
  assert.equal(selectRelease([release('1.0.0', {draft:true}), release('0.9.0', {published_at:null}), release('0.7.0-preview.6')]).version, '0.7.0-preview.6');
});
test('an incomplete release keeps the previous complete version available', () => {
  const incomplete = release('0.8.0-preview.1');
  incomplete.assets.pop();
  assert.equal(selectRelease([incomplete, release('0.7.0-preview.6')]).version, '0.7.0-preview.6');
});
test('a file still uploading is excluded', () => {
  const incomplete = release('0.8.0');
  incomplete.assets[0].state = 'new';
  assert.equal(selectRelease([incomplete, release('0.7.0')]).version, '0.7.0');
});
test('foreign URLs, duplicate names, empty files and missing digests are excluded', () => {
  for (const mutate of [
    item => {item.assets[0].browser_download_url = 'https://example.com/setup.exe';},
    item => {item.html_url = 'https://example.com/release';},
    item => {item.assets.push({...item.assets[0]});},
    item => {item.assets[1].size = 0;},
    item => {item.assets[0].digest = null;},
  ]) {
    const item = release('0.8.0');
    mutate(item);
    assert.equal(selectRelease([item, release('0.7.0')]).version, '0.7.0');
  }
});
test('unrelated extra assets do not hide the official four files', () => {
  const item = release('0.7.0');
  item.assets.push({name:'sbom.json'});
  assert.equal(selectRelease([item]).version, '0.7.0');
});
test('version format and preview flag agree with the application', () => {
  for (const tag of ['v01.0.0','v0.7.0-preview.01','v0.7.0-preview.65535','v65536.0.0','v0.7.0-rc.1','0.7.0','v1.0.0<script>','v0.7.0\n']) {
    assert.equal(parseVersion(tag), null);
  }
  assert.deepEqual(parseVersion('v0.7.0-preview.0'), [0,7,0,0]);
  assert.throws(() => selectRelease([release('0.7.0-preview.6', {prerelease:false})]), /Aucune version/);
});
test('no eligible release fails the build instead of publishing guessed links', () => {
  assert.throws(() => selectRelease([]), /Aucune version/);
  assert.throws(() => selectRelease({}), /invalide/);
});
test('pagination includes eligible versions beyond the first 100 records', async () => {
  const requests = [];
  const releases = await fetchReleases({token:'test-build-token', fetchImpl:async(url, options) => {
    requests.push(url);
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers.Authorization, 'Bearer test-build-token');
    assert.ok(url.startsWith('https://api.github.com/repos/Zeeroz/Temhalo/releases?'));
    return new Response(JSON.stringify(requests.length === 1
      ? Array.from({length:100}, () => release('1.0.0', {draft:true}))
      : [release('0.7.0-preview.6')]));
  }});
  assert.equal(requests.length, 2);
  assert.ok(requests[1].endsWith('page=2'));
  assert.equal(selectRelease(releases).version, '0.7.0-preview.6');
});
test('API errors and malformed responses abort rather than use partial metadata', async () => {
  await assert.rejects(fetchReleases({fetchImpl:async()=>new Response('Rate limited', {status:403})}), /HTTP 403/);
  await assert.rejects(fetchReleases({fetchImpl:async()=>new Response('{}')}), /invalide/);
  await assert.rejects(fetchReleases({fetchImpl:async()=>new Response('x'.repeat(8*1024*1024+1))}), /volumineuse/);
});
test('an endless paginated response cannot silently truncate selection', async () => {
  await assert.rejects(fetchReleases({fetchImpl:async()=>new Response(JSON.stringify(Array(100).fill({draft:true})))}), /pagination/);
});
test('the real HTML template follows a future preview without any manual link edits', async () => {
  const template = await readFile(new URL('../../website/index.html', import.meta.url), 'utf8');
  const future = selectRelease([release('0.7.0-preview.6'), release('0.7.0-preview.7')]);
  const html = renderRelease(template, future);
  assert.ok(html.includes('id="release-version">0.7.0-preview.7<'));
  assert.ok(html.includes('href="https://github.com/Zeeroz/Temhalo/releases/download/v0.7.0-preview.7/Temhalo-0.7.0-preview.7-Setup-x64.exe"'));
  assert.ok(html.includes('Temhalo-0.7.0-preview.7-win-x64.zip'));
  assert.ok(html.includes('href="https://github.com/Zeeroz/Temhalo/releases/tag/v0.7.0-preview.7"'));
  assert.ok(html.includes('Version preview'));
  assert.ok(!html.includes('0.7.0-preview.6'));
  assert.ok(!html.includes('{{'));
  const stable = renderRelease(template, selectRelease([release('0.7.0')]));
  assert.ok(stable.includes('Version stable'));
  assert.ok(!stable.includes('Version en cours de test.'));
});
test('a missing or duplicate template placeholder fails visibly', async () => {
  const template = await readFile(new URL('../../website/index.html', import.meta.url), 'utf8');
  const selected = selectRelease([release('0.7.0')]);
  assert.throws(() => renderRelease(template.replace('{{INSTALLER_URL}}',''), selected), /INSTALLER_URL/);
  assert.throws(() => renderRelease(template + '{{RELEASE_URL}}', selected), /RELEASE_URL/);
});
