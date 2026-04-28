import sys
from pathlib import Path
# ---- imports
from logic.backup import backup_zip
from logic.ins_hyprism import install_hyprism
from logic.ins_shortcut import ins_shortcut
from logic.dp import dp
from parser import parser_argv
# ----

def main():
    config = parser_argv(sys.argv[1:])

    install_dir = (
        config["install_dir"]
        if config["install_dir"]
        else Path.home() / "Applications" / "HyPrism"
    )

    if dp(config): # Mems
        return

    app_file = install_dir / "HyPrism.AppImage"
    is_update = app_file.exists()

    if is_update:
        print(f"Update detected in {install_dir}")
        if config["no_backup"]:
            print("Skipping backup (--no-backup)")
        else:
            # --yes not work for backup.
            backup_zip(install_dir)

    try:
        app_path = install_hyprism(install_dir)
        
        if config["no_shortcut"]:
            print("Skipping shortcuts.")
            return
        # check - it`s "all_yes"?
        if config["all_yes"]:
            ins_shortcut(app_path, install_dir)
            return
        # check - it`s should update?
        if is_update:
            print("Skipping shortcuts on update.")
            return

        ans = input("Do you want to create a desktop shortcut? [y/N]: ").strip().lower()
        if ans in ("y", "yes"):
            ins_shortcut(app_path, install_dir)
        else:
            print("Ok, skipping shortcuts.")

    except Exception as e:
        print(f"Error occurred: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()