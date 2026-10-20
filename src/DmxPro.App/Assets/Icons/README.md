# Icons

Vector icons used across DMX Pro (toolbars, menus, navigation, status bar). One SVG per icon, named with our
semantic name in kebab-case (`save-as.svg`), not after the picture it shows.

## Source and licence

All icons come from **Microsoft Fluent UI System Icons** — <https://github.com/microsoft/fluentui-system-icons> —
"Regular" style, 24 px variant (`assets/<Icon Name>/SVG/ic_fluent_<name>_24_regular.svg`), taken from `main` at
commit `a347af95478f989038cccbf085978865264c048a`.

Copyright (c) 2020 Microsoft Corporation, MIT licence — full text in
[`LICENSE-fluentui-system-icons.txt`](LICENSE-fluentui-system-icons.txt).

**Exception: `stage.svg` is an original drawing** made for DMX Pro (stage floor with a light and beam); it is not from
Fluent UI System Icons and carries no third-party licence.

Files are normalised: `viewBox="0 0 24 24"`, `fill="currentColor"` (no fixed colours), only `<svg>` and `<path>`.

## How the app uses them

The SVGs are source assets only (build action `None`; nothing reads them at runtime).
`tools/icons/Generate-IconResources.ps1` turns them into frozen `Geometry` resources in
`src/DmxPro.App/Themes/Icons.xaml` with key `Icon.<PascalCaseName>` (`save-as.svg` → `Icon.SaveAs`).
Draw one with the `DmxIcon` control, which takes its colour from the parent's foreground:

```xml
xmlns:c="clr-namespace:DmxPro.App.Controls"

<c:DmxIcon Kind="SaveAs" Size="20" />
<c:DmxIcon Data="{StaticResource Icon.SaveAs}" Size="16" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
```

## Adding or changing an icon

1. Find the icon at <https://github.com/microsoft/fluentui-system-icons/tree/main/assets> (or the Fluent icon
   catalogue) and download `ic_fluent_<name>_24_regular.svg`. Prefer the 24 px Regular variant; if an icon has no
   24 px file, pick a different icon rather than mixing sizes.
2. Check it is a plain SVG (only `<svg>`/`<path>`, no scripts or links), then save it here under our kebab-case name.
   Set `fill="currentColor"` on the `<svg>` and remove fixed colours.
3. Run the generator from the repo root and commit the SVG together with the regenerated `Themes/Icons.xaml`:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\icons\Generate-IconResources.ps1
   ```

   The output is sorted and deterministic; never edit `Icons.xaml` by hand.
4. Add a row to the table below.

## Icon map

| File | `Kind` (key `Icon.<Kind>`) | Fluent source |
|------|----------------------------|---------------|
| `about.svg` | `About` | Book Information (`ic_fluent_book_information_24_regular`) |
| `add-to-group.svg` | `AddToGroup` | Collections Add (`ic_fluent_collections_add_24_regular`) |
| `address-map.svg` | `AddressMap` | Grid (`ic_fluent_grid_24_regular`) |
| `add.svg` | `Add` | Add (`ic_fluent_add_24_regular`) |
| `auto-address.svg` | `AutoAddress` | Number Symbol (`ic_fluent_number_symbol_24_regular`) |
| `auto-patch.svg` | `AutoPatch` | Wand (`ic_fluent_wand_24_regular`) |
| `blackout.svg` | `Blackout` | Weather Moon (`ic_fluent_weather_moon_24_regular`) |
| `channel.svg` | `Channel` | Options (`ic_fluent_options_24_regular`) |
| `chaser.svg` | `Chaser` | Arrow Repeat All (`ic_fluent_arrow_repeat_all_24_regular`) |
| `check.svg` | `Check` | Checkmark (`ic_fluent_checkmark_24_regular`) |
| `chevron-down.svg` | `ChevronDown` | Chevron Down (`ic_fluent_chevron_down_24_regular`) |
| `chevron-right.svg` | `ChevronRight` | Chevron Right (`ic_fluent_chevron_right_24_regular`) |
| `close.svg` | `Close` | Dismiss (`ic_fluent_dismiss_24_regular`) |
| `color-cycle.svg` | `ColorCycle` | Arrow Sync Circle (`ic_fluent_arrow_sync_circle_24_regular`) |
| `color.svg` | `Color` | Color Fill (`ic_fluent_color_fill_24_regular`) |
| `console.svg` | `Console` | Board (`ic_fluent_board_24_regular`) |
| `copy.svg` | `Copy` | Copy (`ic_fluent_copy_24_regular`) |
| `cut.svg` | `Cut` | Cut (`ic_fluent_cut_24_regular`) |
| `delete.svg` | `Delete` | Delete (`ic_fluent_delete_24_regular`) |
| `dimmer.svg` | `Dimmer` | Brightness High (`ic_fluent_brightness_high_24_regular`) |
| `duplicate.svg` | `Duplicate` | Document Copy (`ic_fluent_document_copy_24_regular`) |
| `edit.svg` | `Edit` | Edit (`ic_fluent_edit_24_regular`) |
| `error.svg` | `Error` | Error Circle (`ic_fluent_error_circle_24_regular`) |
| `exit-full-screen.svg` | `ExitFullScreen` | Full Screen Minimize (`ic_fluent_full_screen_minimize_24_regular`) |
| `exit.svg` | `Exit` | Sign Out (`ic_fluent_sign_out_24_regular`) |
| `export.svg` | `Export` | Arrow Export LTR (`ic_fluent_arrow_export_ltr_24_regular`) |
| `fade.svg` | `Fade` | Transparency Square (`ic_fluent_transparency_square_24_regular`) |
| `filter.svg` | `Filter` | Filter (`ic_fluent_filter_24_regular`) |
| `fixtures.svg` | `Fixtures` | Lightbulb (`ic_fluent_lightbulb_24_regular`) |
| `flash.svg` | `Flash` | Flashlight (`ic_fluent_flashlight_24_regular`) |
| `folder.svg` | `Folder` | Folder (`ic_fluent_folder_24_regular`) |
| `full-screen.svg` | `FullScreen` | Full Screen Maximize (`ic_fluent_full_screen_maximize_24_regular`) |
| `functions.svg` | `Functions` | Flash (`ic_fluent_flash_24_regular`) |
| `group.svg` | `Group` | Group List (`ic_fluent_group_list_24_regular`) |
| `help.svg` | `Help` | Question Circle (`ic_fluent_question_circle_24_regular`) |
| `import.svg` | `Import` | Arrow Import (`ic_fluent_arrow_import_24_regular`) |
| `info.svg` | `Info` | Info (`ic_fluent_info_24_regular`) |
| `input-output.svg` | `InputOutput` | Plug Connected (`ic_fluent_plug_connected_24_regular`) |
| `keyboard.svg` | `Keyboard` | Keyboard (`ic_fluent_keyboard_24_regular`) |
| `library.svg` | `Library` | Library (`ic_fluent_library_24_regular`) |
| `lock.svg` | `Lock` | Lock Closed (`ic_fluent_lock_closed_24_regular`) |
| `monitor.svg` | `Monitor` | Pulse (`ic_fluent_pulse_24_regular`) |
| `more.svg` | `More` | More Horizontal (`ic_fluent_more_horizontal_24_regular`) |
| `move-down.svg` | `MoveDown` | Arrow Down (`ic_fluent_arrow_down_24_regular`) |
| `move-up.svg` | `MoveUp` | Arrow Up (`ic_fluent_arrow_up_24_regular`) |
| `music.svg` | `Music` | Music Note 2 (`ic_fluent_music_note_2_24_regular`) |
| `new.svg` | `New` | Document Add (`ic_fluent_document_add_24_regular`) |
| `open.svg` | `Open` | Folder Open (`ic_fluent_folder_open_24_regular`) |
| `output-off.svg` | `OutputOff` | Plug Disconnected (`ic_fluent_plug_disconnected_24_regular`) |
| `output-on.svg` | `OutputOn` | Power (`ic_fluent_power_24_regular`) |
| `paste.svg` | `Paste` | Clipboard Paste (`ic_fluent_clipboard_paste_24_regular`) |
| `pause.svg` | `Pause` | Pause (`ic_fluent_pause_24_regular`) |
| `play.svg` | `Play` | Play (`ic_fluent_play_24_regular`) |
| `preview.svg` | `Preview` | Eye (`ic_fluent_eye_24_regular`) |
| `profile-new.svg` | `ProfileNew` | Document One Page Add (`ic_fluent_document_one_page_add_24_regular`) |
| `profile.svg` | `Profile` | Document Text (`ic_fluent_document_text_24_regular`) |
| `pulse.svg` | `Pulse` | Heart Pulse (`ic_fluent_heart_pulse_24_regular`) |
| `recent.svg` | `Recent` | History (`ic_fluent_history_24_regular`) |
| `redo.svg` | `Redo` | Arrow Redo (`ic_fluent_arrow_redo_24_regular`) |
| `refresh.svg` | `Refresh` | Arrow Sync (`ic_fluent_arrow_sync_24_regular`) |
| `release-all.svg` | `ReleaseAll` | Arrow Reset (`ic_fluent_arrow_reset_24_regular`) |
| `remove.svg` | `Remove` | Subtract (`ic_fluent_subtract_24_regular`) |
| `rename.svg` | `Rename` | Rename (`ic_fluent_rename_24_regular`) |
| `save-as.svg` | `SaveAs` | Save Edit (`ic_fluent_save_edit_24_regular`) |
| `save.svg` | `Save` | Save (`ic_fluent_save_24_regular`) |
| `scenes.svg` | `Scenes` | Color (`ic_fluent_color_24_regular`) |
| `search.svg` | `Search` | Search (`ic_fluent_search_24_regular`) |
| `select-all.svg` | `SelectAll` | Select All On (`ic_fluent_select_all_on_24_regular`) |
| `sequence.svg` | `Sequence` | Text Number List LTR (`ic_fluent_text_number_list_ltr_24_regular`) |
| `settings.svg` | `Settings` | Settings (`ic_fluent_settings_24_regular`) |
| `stage.svg` | `Stage` | Original DMX Pro drawing (not from Fluent UI System Icons) |
| `stop-all.svg` | `StopAll` | Record Stop (`ic_fluent_record_stop_24_regular`) |
| `stop.svg` | `Stop` | Stop (`ic_fluent_stop_24_regular`) |
| `strobe.svg` | `Strobe` | Flash Flow (`ic_fluent_flash_flow_24_regular`) |
| `test.svg` | `Test` | Beaker (`ic_fluent_beaker_24_regular`) |
| `theme-dark.svg` | `ThemeDark` | Dark Theme (`ic_fluent_dark_theme_24_regular`) |
| `theme-light.svg` | `ThemeLight` | Weather Sunny (`ic_fluent_weather_sunny_24_regular`) |
| `timeline.svg` | `Timeline` | Timeline (`ic_fluent_timeline_24_regular`) |
| `undo.svg` | `Undo` | Arrow Undo (`ic_fluent_arrow_undo_24_regular`) |
| `universe.svg` | `Universe` | Globe (`ic_fluent_globe_24_regular`) |
| `unlock.svg` | `Unlock` | Lock Open (`ic_fluent_lock_open_24_regular`) |
| `unverified.svg` | `Unverified` | Shield Question (`ic_fluent_shield_question_24_regular`) |
| `verified.svg` | `Verified` | Checkmark Circle (`ic_fluent_checkmark_circle_24_regular`) |
| `warning.svg` | `Warning` | Warning (`ic_fluent_warning_24_regular`) |
| `wizard.svg` | `Wizard` | Sparkle (`ic_fluent_sparkle_24_regular`) |
