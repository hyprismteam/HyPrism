# HyPrism Linux Installer Documentation

This documentation provides details on how to install, configure, and build the **HyPrism Installer** for Linux systems.

---

## 🚀 Features

* 
**Dynamic Installation**: Uses the GitHub API to fetch the latest AppImage from the official repository.


* 
**Update Support**: Detects existing installations and safely replaces them.


* 
**Optional Backups**: Offers to create a timestamped ZIP archive of your current game version before updating.


* 
**System Integration**: Automatically generates `.desktop` shortcuts for your desktop and application menu.


* 
**Resource Efficient**: Downloads files in chunks to minimize system load.



---

## 🛠 Requirements

* 
**Operating System**: Linux (tested on Arch Linux).


* **Dependencies**:
* Python 3 and `pip`.


* 
`FUSE3` (`fusermount3`) to run AppImages.


* 
`requests` Python library.





---

## 📥 Usage

### 1. Automatic Environment Check & Install

Use the provided bash script to verify dependencies and run the installer automatically:

```bash
chmod +x install_linux.sh
./install_linux.sh

```

**This script will:**

* Verify Python 3, pip, and FUSE3 are present.


* Download the latest installer binary.
* Execute the installation with default settings.

### 2. Manual Execution

If running the Python script (`main.py`) or the compiled binary directly, you can use the following flags:

| Option | Description |
| --- | --- |
| `--dir=PATH` | Set a custom installation directory (Default: `~/Applications/HyPrism`).

 |
| `--no-shortcut` | Skip the creation of desktop and menu shortcuts.

 |
| `--no-backup` | Skip the backup prompt during updates.

 |
| `--yes` | Automatically accept prompts (except for backup). |
| `-c` / `-coffe` | Enable "Coffee Mode" (Easter egg).

 |

---

## 📂 Backup Behavior

* The installer prompts for a backup **only** during an update.


* It requires the path to your current `HyPrism/game_version` (not the root folder).


* If the directory requires elevated permissions, the installer attempts to use `pkexec` for access.


* Backups are saved as `HyPrism_backup_YYYYMMDD_HHMMSS.zip` in your installation folder.



---

## 🏗 Building from Source

To compile the installer into a standalone binary using **PyInstaller**:

1. **Set up a virtual environment**:
```bash
python3 -m venv .venv
source .venv/bin/activate

```


2. **Install requirements**:
```bash
pip install requests pyinstaller

```


3. **Build the project**:
```bash
pyinstaller main.spec

```



---

## ❓ Troubleshooting

* 
**AppImage not launching**: Ensure `fuse3` is installed on your system.


* 
**Permission Denied**: If the backup fails, ensure you have write access or accept the `pkexec` prompt.


* 
**Missing Icons**: Check if `HyPrism_icon.png` exists in the installation directory; you may need to restart your desktop environment to refresh shortcuts.