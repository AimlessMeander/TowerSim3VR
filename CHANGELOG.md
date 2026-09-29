# Changes

## Unreleased

- **Faster:** screen-space global illumination and motion vectors are left out in the headset (`ReduceEffects`,
  on by default): about 20% less GPU time per frame, with no visible difference.
- The GPU time per frame is written to the log once a minute, to help with performance reports.

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
