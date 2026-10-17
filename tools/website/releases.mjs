// The same stable/preview numbering and precedence as Temhalo's ReleaseVersion.
export const repository = 'https://github.com/Zeeroz/Temhalo';
const releaseApi = 'https://api.github.com/repos/Zeeroz/Temhalo/releases';

export function parseVersion(tag) {
  if (typeof tag !== 'string' || tag.length > 61) return null;
  const match = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.(0|[1-9]\d*))?$/.exec(tag);
  if (!match || match[0] !== tag) return null;
  const parts = match.slice(1).map(value => value === undefined ? null : Number(value));
  if (parts.some(value => value !== null && (!Number.isInteger(value) || value > 65535)) || parts[3] === 65535) return null;
  return parts;
}

function compareVersions(left, right) {
  for (let i = 0; i < 4; i++) {
    const difference = (left[i] ?? 65535) - (right[i] ?? 65535);
    if (difference) return difference;
  }
  return 0;
}

function completeRelease(release) {
  if (!release || release.draft !== false || !Array.isArray(release.assets)) return null;
  const version = parseVersion(release.tag_name);
  if (!version || release.prerelease !== (version[3] !== null) ||
      release.html_url !== `${repository}/releases/tag/${release.tag_name}` ||
      typeof release.published_at !== 'string' || !Number.isFinite(Date.parse(release.published_at))) return null;
  const number = release.tag_name.slice(1);
  const files = {
    installer: `Temhalo-${number}-Setup-x64.exe`,
    portable: `Temhalo-${number}-win-x64.zip`,
  };
  const assets = {};
  for (const [kind, name] of Object.entries(files)) {
    // All four official files must be uploaded before the site advertises a release.
    for (const filename of [name, `${name}.sha256`]) {
      const matching = release.assets.filter(asset => asset?.name === filename);
      if (matching.length !== 1) return null;
      const asset = matching[0];
      const url = `${repository}/releases/download/${release.tag_name}/${filename}`;
      if (asset.state !== 'uploaded' || !Number.isSafeInteger(asset.size) || asset.size <= 0 ||
          asset.browser_download_url !== url || typeof asset.digest !== 'string' ||
          !/^sha256:[a-f0-9]{64}$/i.test(asset.digest)) return null;
      if (filename === name) assets[kind] = {name, url, bytes: asset.size, sha256: asset.digest.slice(7).toLowerCase()};
    }
  }
  return {version: number, tag: release.tag_name, prerelease: release.prerelease,
    publishedAt: release.published_at, releaseUrl: release.html_url, assets, ordering: version};
}

export function selectRelease(releases) {
  if (!Array.isArray(releases)) throw new Error('La liste des releases GitHub est invalide.');
  const candidates = releases.map(completeRelease).filter(Boolean);
  candidates.sort((left, right) => compareVersions(right.ordering, left.ordering) ||
    Date.parse(right.publishedAt) - Date.parse(left.publishedAt));
  if (!candidates.length) throw new Error('Aucune version Temhalo publiée avec ses quatre fichiers complets.');
  const {ordering, ...selected} = candidates[0];
  return selected;
}

export async function fetchReleases({token, fetchImpl = fetch} = {}) {
  const headers = {Accept: 'application/vnd.github+json', 'User-Agent': 'Temhalo-Website', 'X-GitHub-Api-Version': '2026-03-10'};
  if (token) headers.Authorization = `Bearer ${token}`;
  const releases = [];
  // Pagination avoids accidentally hiding a version when many drafts exist.
  for (let page = 1; page <= 50; page++) {
    const response = await fetchImpl(`${releaseApi}?per_page=100&page=${page}`, {
      headers, redirect: 'error', signal: AbortSignal.timeout(25000),
    });
    if (!response.ok) throw new Error(`Lecture des releases GitHub impossible (HTTP ${response.status}). Le site publié reste inchangé.`);
    const payload = await response.text();
    if (payload.length > 8 * 1024 * 1024) throw new Error('Liste des releases trop volumineuse.');
    const batch = JSON.parse(payload);
    if (!Array.isArray(batch) || batch.length > 100) throw new Error('Réponse GitHub Releases invalide.');
    releases.push(...batch);
    if (batch.length < 100) return releases;
  }
  throw new Error('Limite de pagination atteinte ; aucun choix partiel de version ne sera publié.');
}

export function renderRelease(template, release) {
  const values = {
    RELEASE_VERSION: release.version,
    RELEASE_LABEL: release.prerelease ? 'Version preview' : 'Version stable',
    RELEASE_NOTICE: release.prerelease ? 'Version en cours de test. ' : '',
    INSTALLER_URL: release.assets.installer.url,
    PORTABLE_URL: release.assets.portable.url,
    RELEASE_URL: release.releaseUrl,
  };
  const escape = value => String(value).replace(/[&<>"']/g, char => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[char]));
  for (const [key, value] of Object.entries(values)) {
    const placeholder = `{{${key}}}`;
    if (template.split(placeholder).length !== 2) throw new Error(`Emplacement ${key} absent ou dupliqué dans le site.`);
    template = template.replace(placeholder, () => escape(value));
  }
  if (/\{\{[A-Z_]+\}\}/.test(template)) throw new Error('Un emplacement du site reste non renseigné.');
  return template;
}
