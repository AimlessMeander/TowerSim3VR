# Changes

## 0.11.0

- **Faster and cleaner:** some costly screen-space effects are left out in the headset (global illumination,
  ambient occlusion, subsurface scattering, motion vectors; `ReduceEffects`, on by default): about 25% less GPU
  time per frame (11 ms down to 8 on an RTX 4090), and they looked better off in VR.
- **Less washed out:** the headset picture is darker than the game's own brightness, which looked washed out in VR
  (`Brightness`, default -1; Ctrl+Shift+Up/Down adjusts it while playing).

## 0.10.0

- **New: aircraft labels in VR.** The call sign and runway labels you see on the monitor now float above the
  aircraft in the headset, always facing you, with the game's colours and rules (which aircraft have one, fading
  with distance, dimmer for other frequencies, hidden behind the tower frame). They keep a readable size at any
  distance and through the binoculars, and labels that would overlap are stacked. Settings: `[Labels]` `Enabled`
  and `Size` (see INSTALL.txt).
- **Fixed: blue tint over everything** after leaving an airport and loading one again (for example to change the
  weather). The second time VR started, the controller lasers were only half set up, and a laser part was left
  around your head.

## 0.9.0

- First playtest release.
