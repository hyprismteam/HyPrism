import requests # type: ignore
from pathlib import Path

OWNER = "yyyumeniku"
REPO = "HyPrism"
HEADERS = {"User-Agent": "HyPrism-installer"}

"""
1.Используеться GITHUB-API для динамической установки программы.
2.инсталлер зафиксирован на .appimage and 'x64' in name. Во избежание добавления дополнительных условий
3.используеться обработка ошибок - поэтому шанс что программа упадёт намного ниже чем раньше. Возможно стоит её обробатывать KeyboardInterpput
4.скачиваеться всё по чанкам, что-бы не перегружать компьютер пользователя.
"""

def install_hyprism(install_dir: Path) -> Path:
    install_dir.mkdir(parents=True, exist_ok=True)

    api_url = f"https://api.github.com/repos/{OWNER}/{REPO}/releases/latest"
    resp = requests.get(api_url, headers=HEADERS)
    resp.raise_for_status()
    release = resp.json()

    asset_url = None
    for asset in release.get("assets", []):
        name = asset.get("name", "")
        # only if x64
        if name.endswith(".AppImage"):
            asset_url = asset.get("browser_download_url")
            break

    if not asset_url:
        raise RuntimeError("AppImage not found in latest release")
    # rename file
    filename = install_dir / "HyPrism.AppImage"
    temp_file = filename.with_suffix(".tmp")

    print(f"Downloading latest HyPrism...")
    with requests.get(asset_url, stream=True, headers=HEADERS) as r:
        r.raise_for_status()
        with open(temp_file, "wb") as f:
            for chunk in r.iter_content(8192):
                f.write(chunk)

    if filename.exists():
        filename.unlink()
    temp_file.rename(filename)
    filename.chmod(0o755)

    print(f"HyPrism installed/updated successfully at: {filename}")
    return filename

