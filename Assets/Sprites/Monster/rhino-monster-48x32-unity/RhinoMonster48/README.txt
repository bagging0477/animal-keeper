RHINOCEROS MONSTER — UNITY PIXEL SPRITES

Contents
- rhino-atlas-288x224.png: native-resolution transparent atlas, six columns by seven rows.
- Frames/: 22 individually named 48x32 PNGs.
- validation.json: cell positions, bounds and color counts.

Row layout, left to right
1 Idle: 4 frames, two transparent unused cells.
2 Walk: 6 frames.
3 Charge warning: 4 frames, two unused cells.
4 Dash: 4 frames, two unused cells.
5 Recover: 2 frames, four unused cells.
6 Hurt: 1 frame, five unused cells.
7 Fallen: 1 frame, five unused cells.

Native export checks
Every frame is 48x32 pixels. Atlas is 288x224 pixels.
All visible pixels use an eight-color shared palette; alpha is either 0 or 255.
Silhouette boundary pixels are #2F2F2E. No ground or baked shadow.
Palette: #2F2F2E, #434B50, #5D6B73, #7C8A90, #9CA5A5, #B8B6A7, #666964, #AD9866.
The enlarged generated preview is not the import atlas. Use the PNG inside this folder.

Unity import: individual frames
Drag this folder into Assets.
Select all PNGs in Frames and set:
Texture Type: Sprite (2D and UI)
Sprite Mode: Single
Filter Mode: Point (no filter)
Compression: None
Generate Mip Maps: Off
Pivot: Center (0.5, 0.5)
Pixels Per Unit: 32 when the final object scale is 2.
Apply.
For the established world pixel rule, PPU = 16 x final scale, including parent scale.
Scale 1 requires PPU16; scale2 requires PPU32.
This preserves pixel size; the rhino is wider than a 32x32 animal at the same scale.

Unity import: atlas alternative
Use the atlas instead of individual frames if preferred.
Texture Type Sprite, Sprite Mode Multiple.
In Sprite Editor choose Slice > Grid By Cell Size > X48 Y32.
Pivot Center, Apply. Ignore unused transparent cells.
Keep the same Point, None and PPU settings as above.
Do not import the enlarged preview as a sprite atlas.

Suggested playback starting points
Idle: 4-6 fps, loop.
Walk: 6-8 fps, loop.
Charge: 6-8 fps, no loop; hold the final frame as needed for the warning timer.
Dash: 12 fps, loop while the dash movement is active.
Recover: 4-6 fps, no loop.
Hurt and Fallen: single-frame poses.
Actual dash speed, hitbox, warning duration and recovery vulnerability belong to your game code.
These frames do not move the object automatically.
Facing is right. Use SpriteRenderer.flipX for left; avoid negative root scale if that would affect colliders.
Motion dashes and small dust pixels are baked into their corresponding frames using the shared palette.

Scope
Generated artwork was exported at native resolution with palette/alpha cleanup.
Frame dimensions, occupied cells, color count and ZIP integrity were checked.
Approximate body proportions vary with pose. Visual continuity and attack telegraph timing should be reviewed in your actual game camera.
No Animator Controller, AI, collision code, Unity scene or prefab is included.
Unity Editor compile/Play testing has not been performed.
