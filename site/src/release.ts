// Latest published release, read from the GitHub API at build time so the download buttons point straight at the
// installer instead of the releases page. The release workflow redeploys the site after publishing, so a new version
// shows up within minutes. If the API is unreachable the buttons fall back to the releases page.
//
// /releases/latest is not used: GitHub excludes pre-releases (all 0.x betas) from it.

export interface ReleaseAsset {
  name: string;
  url: string;
  /** Size in bytes. */
  size: number;
}

export interface Release {
  version: string;
  page: string;
  date: string;
  msi: ReleaseAsset;
  portable?: ReleaseAsset;
  checksums?: ReleaseAsset;
}

const api = 'https://api.github.com/repos/insideapp-srl/talk2tia/releases?per_page=20';

interface GitHubRelease {
  tag_name: string;
  html_url: string;
  draft: boolean;
  published_at: string;
  assets: { name: string; browser_download_url: string; size: number }[];
}

async function fetchLatest(): Promise<Release | undefined> {
  const headers: Record<string, string> = { Accept: 'application/vnd.github+json', 'User-Agent': 'talk2tia-site' };
  const token = process.env.GITHUB_TOKEN;
  if (token) headers.Authorization = `Bearer ${token}`;
  try {
    const res = await fetch(api, { headers });
    if (!res.ok) throw new Error(`GitHub API ${res.status} ${res.statusText}`);
    const releases = (await res.json()) as GitHubRelease[];
    for (const r of releases) {
      if (r.draft) continue;
      const asset = (test: (name: string) => boolean): ReleaseAsset | undefined => {
        const a = r.assets.find((x) => test(x.name));
        return a && { name: a.name, url: a.browser_download_url, size: a.size };
      };
      const msi = asset((n) => n.endsWith('.msi'));
      if (!msi) continue; // release still uploading, or without installer
      return {
        version: r.tag_name.replace(/^v/, ''),
        page: r.html_url,
        date: r.published_at,
        msi,
        portable: asset((n) => n.endsWith('-portable.zip')),
        checksums: asset((n) => n === 'SHA256SUMS.txt'),
      };
    }
    console.warn('[release] no published release with an MSI; download buttons use the releases page');
  } catch (err) {
    console.warn(`[release] cannot read the latest release (${(err as Error).message}); download buttons use the releases page`);
  }
  return undefined;
}

export const latest: Release | undefined = await fetchLatest();

/** "2.9 MB" */
export function formatSize(bytes: number): string {
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
