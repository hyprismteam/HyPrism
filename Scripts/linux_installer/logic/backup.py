from pathlib import Path
import sys, os
import datetime, shutil

def backup_zip(install_dir: Path):
    print("Backup Hytale")
    try:
        y = input("Do you want a backup (just in case)? [y/N]: ").strip().lower()
        if y not in ("y", "yes"):
            print("Skipping backup")
            return


        hyprism_path = input("Path to your current HyPrism/game_version (not HyPrism folder): ").strip()
        hyprism_path = Path(hyprism_path).expanduser().resolve()

        if not hyprism_path.exists():
            print(f"Path {hyprism_path} does not exist. Skipping backup.")
            return

        # Check write access
        if not os.access(hyprism_path, os.W_OK):
            if os.geteuid() != 0 and "--elevated" not in sys.argv:
                os.execvp(
                    "pkexec",
                    ["pkexec", sys.executable] + sys.argv + ["--elevated"]
                )
            elif os.geteuid() != 0:
                raise PermissionError("pkexec failed or was cancelled")


        timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_path = install_dir / f"HyPrism_backup_{timestamp}"

        print(f"Creating backup: {backup_path}.zip from {hyprism_path}")
        shutil.make_archive(str(backup_path), 'zip', hyprism_path)
        print("Backup created successfully.")
    except KeyboardInterrupt:
        print("Shutdown")
        sys.exit(0)