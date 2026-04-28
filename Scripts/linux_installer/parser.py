from pathlib import Path

def parser_argv(argv):
    config = {
        "install_dir": None,
        "no_shortcut": False,
        "coffe": False,
        "no_backup": False,
        "all_yes": False
    }

    for arg in argv:
        match arg:
            case s if s.startswith("--dir="):
                config["install_dir"] = (
                    Path(s.split("=", 1)[1])
                    .expanduser()
                    .resolve()
                )
            case "--no-shortcut":
                config["no_shortcut"] = True
            case "-c" | "-coffe":
                config["coffe"] = True
            case "--no-backup":
                config["no_backup"] = True
            case "--yes":
                config["all_yes"] = True
            case _:
                raise ValueError(f"Unknown argument: {arg}")

    return config
