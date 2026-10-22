"""Exercise a disposable document on an existing Android emulator without clearing app data."""
import os
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
ADB = Path(os.environ['LOCALAPPDATA']) / 'Android/sdk/platform-tools/adb.exe'
DEVICE = 'emulator-5554'
OUT = ROOT / '.tools/office-ui'
OUT.mkdir(parents=True, exist_ok=True)


def run(*args, binary=False):
    result = subprocess.run([str(ADB), '-s', DEVICE, *args], check=True, capture_output=True)
    return result.stdout if binary else result.stdout.decode('utf-8', errors='replace')


def nodes():
    run('shell', 'uiautomator', 'dump', '/sdcard/gaius-office-upgrade.xml')
    return list(ET.fromstring(run('shell', 'cat', '/sdcard/gaius-office-upgrade.xml')).iter('node'))


def find(label, partial=False):
    for node in nodes():
        if any(label in value if partial else label == value for value in [node.get('text', ''), node.get('content-desc', '')]):
            return node
    return None


def tap(label, partial=False):
    node = find(label, partial)
    if node is None:
        raise AssertionError('Missing Android control: ' + label)
    x1, y1, x2, y2 = map(int, re.findall(r'\d+', node.attrib['bounds']))
    run('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(.4)


def capture(name):
    (OUT / name).write_bytes(run('exec-out', 'screencap', '-p', binary=True))


run('shell', 'am', 'start', '-n', 'app.gaius.gaius_mobile/.MainActivity')
time.sleep(2)
tap('Write')
assert find('Open tabs (1)', partial=True) is not None, 'Phone tab counter missing'
capture('android-write.png')
tap('Open tabs (1)', partial=True)
tap('Close Untitled document')
assert find('Save changes?') is not None
tap('Cancel')
tap('Close Untitled document')
tap('Discard')
assert find('No open tabs') is not None
run('shell', 'input', 'keyevent', '4')
time.sleep(.5)
tap('Write')
tap('Insert', partial=True)
tap('Shape')
tap('Star')
assert find('Editable star', partial=True) is not None
tap('Editable star', partial=True)
assert find('Format shape') is not None
capture('android-shape-format.png')
tap('Cancel')
run('shell', 'input', 'keyevent', '4')
time.sleep(.6)
if find('Save changes?') is None:
    run('shell', 'input', 'keyevent', '4')
    time.sleep(.6)
tap('Discard')
print('PASS: counted phone tabs, Save/Cancel/Discard and native editable shape dialog')
