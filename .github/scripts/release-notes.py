"""Generate notes from the previous release reachable on this branch."""
import os
import subprocess
from pathlib import Path

def run(*args):
    return subprocess.check_output(args, text=True).strip()

sha = os.environ['GITHUB_SHA']
tag = os.environ['RELEASE_TAG']
previous = None
for candidate in run('git', 'tag', '--merged', sha, '--sort=-creatordate').splitlines():
    if candidate.startswith('build-') and candidate != tag:
        previous = candidate
        break
revision = f'{previous}..{sha}' if previous else sha
commits = run('git', 'log', '--no-merges', '--format=- %s (%h)', revision)
print(f"Build from `{sha[:12]}` on `{os.environ['GITHUB_REF_NAME']}`.\n")
changelog = Path('CHANGELOG.md')
if changelog.exists():
    sections = changelog.read_text(encoding='utf-8').split('\n## ')
    if len(sections) > 1:
        print('Release highlights\n\n' + sections[1].split('\n', 1)[-1].strip() + '\n')
print('Changes\n\n' + (commits or '- Rebuild of the same source.'))
print('\nDownloads\n\n- Android: universal release APK, Android 7.0+.')
signed = os.environ.get('SIGNED_ANDROID') == 'true'
print('  ' + ('Uses the configured release signing key.' if signed else 'Uses an ephemeral test key; uninstall earlier builds before installing if signatures differ.'))
print('- Windows x64: EXE installer and self-contained portable ZIP. Installer is not Authenticode signed.')
print('- Web: static application ZIP; the default branch also deploys to GitHub Pages.')
print('- SHA256SUMS.txt verifies the downloaded assets.\n')
print('Development preview: see README.md for format fidelity and unverified platform behavior.')
