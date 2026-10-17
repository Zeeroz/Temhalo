import {appendFile, cp, mkdir, mkdtemp, readFile, writeFile} from 'node:fs/promises';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {fetchReleases, renderRelease, selectRelease} from './releases.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
try {
  const release = selectRelease(await fetchReleases({token: process.env.GH_TOKEN}));
  const template = await readFile(join(root, 'website/index.html'), 'utf8');
  const html = renderRelease(template, release);
  const artifacts = join(root, 'artifacts');
  await mkdir(artifacts, {recursive: true});
  // A fresh directory prevents stale files; no existing folder is ever deleted.
  const directory = await mkdtemp(join(artifacts, 'website-'));
  await cp(join(root, 'website'), directory, {recursive: true});
  await writeFile(join(directory, 'index.html'), html);
  await writeFile(join(directory, 'release.json'), JSON.stringify(release, null, 2) + '\n');
  if (process.env.GITHUB_OUTPUT) {
    await appendFile(process.env.GITHUB_OUTPUT, `directory=${directory}\nversion=${release.version}\n`);
  }
  console.log(JSON.stringify({directory, version: release.version, prerelease: release.prerelease, releaseUrl: release.releaseUrl}));
} catch (error) {
  // Report only known errors, never request headers or the authentication token.
  console.error(error instanceof Error ? error.message : 'Préparation du site impossible.');
  process.exitCode = 1;
}
