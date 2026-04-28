from pathlib import Path
import requests

HEADERS = {"User-Agent": "HyPrism-installer"}

def install_icon(install_dir: Path):
    ICON_URL = "https://raw.githubusercontent.com/yyyumeniku/HyPrism/main/assets/Hyprism.png"
    # TODO: replace with bundled asset or CDN when infra exists
    
    resp = requests.get(ICON_URL, headers=HEADERS, timeout=10)
    resp.raise_for_status()

    icon_path = install_dir / "HyPrism_icon.png"
    install_dir.mkdir(parents=True, exist_ok=True)

    if not icon_path.exists():
        icon_path.write_bytes(resp.content)
        print("Icon downloaded.")
    else:
        print("Icon already exists, skipping download.")
    
    icon_path.chmod(0o644)

def ins_shortcut(app_path: Path, install_dir: Path):
    install_icon(install_dir)

    desktop_entry_template = f"""[Desktop Entry]
Name=HyPrism
Comment=Hytale Launcher
Exec={app_path}
TryExec={app_path}
Icon={install_dir}/HyPrism_icon.png
Terminal=false
Type=Application
Categories=Game;
StartupWMClass=HyPrism
"""

    paths = [
        Path.home() / "Desktop" / "HyPrism.desktop",
        Path.home() / ".local" / "share" / "applications" / "HyPrism.desktop"
    ]

    for p in paths:
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(desktop_entry_template)
        p.chmod(0o755)

    print("Shortcuts created/updated.")
