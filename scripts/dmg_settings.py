"""Finder presentation for the read-only Stationary installer image."""

from pathlib import Path

app = Path(defines["app"])
artwork = Path(defines["background"])
if not app.is_dir() or not artwork.is_file():
    raise ValueError("DMG presentation requires the staged app and background artwork")

format = "UDZO"
filesystem = "HFS+"
files = [str(app)]
symlinks = {"Applications": "/Applications"}
background = str(artwork)
icon = str(app / "Contents" / "Resources" / "appicon.icns")
hide_extensions = []
window_rect = ((160, 160), (760, 480))
default_view = "icon-view"
icon_size = 112
text_size = 14
label_pos = "bottom"
arrange_by = None
icon_locations = {app.name: (190, 240), "Applications": (570, 240)}
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
include_icon_view_settings = True
include_list_view_settings = False
