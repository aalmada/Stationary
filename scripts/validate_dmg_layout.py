"""Verify the mounted installer's persisted Finder layout."""

import sys
from pathlib import Path


def validate(mount: Path) -> None:
    from ds_store import DSStore

    if not (mount / ".background.png").is_file():
        raise ValueError("Installer background is missing")
    with DSStore.open(str(mount / ".DS_Store"), "r") as store:
        if store["Stationary.app"]["Iloc"] != (190, 240):
            raise ValueError("Installer app icon position is incorrect")
        if store["Applications"]["Iloc"] != (570, 240):
            raise ValueError("Installer Applications position is incorrect")
        view = store["."]["icvp"]
        if view["iconSize"] != 112 or view["backgroundType"] != 2:
            raise ValueError("Installer icon size or image background setting is incorrect")
        if not view.get("backgroundImageAlias"):
            raise ValueError("Installer background alias is missing")
        window = store["."]["bwsp"]
        if window["ShowToolbar"] or window["ShowSidebar"]:
            raise ValueError("Installer window must hide the toolbar and sidebar")
        if "760, 480" not in window["WindowBounds"]:
            raise ValueError("Installer window dimensions are incorrect")


if __name__ == "__main__":
    validate(Path(sys.argv[1]))
    print("Verified branded Finder layout")
