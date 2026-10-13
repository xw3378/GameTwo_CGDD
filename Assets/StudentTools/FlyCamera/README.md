# FlyCamera

A walkthrough camera you can drop into a scene. It passes through objects so you can look around a level and frame a shot. One script, two prefabs:

| Prefab | Use |
|--------|-----|
| `FlyCamera` | 3D scenes. Perspective camera. Look around and fly through the level |
| `WalkCamera2D` | 2D scenes. Orthographic camera. Pan and zoom without changing Z |

## Use

1. Drag `FlyCamera` into a 3D scene, or `WalkCamera2D` into a 2D scene.
2. Press Play.

On Play, this camera disables every other active Camera and Audio Listener in the scene. A camera that came with the scene can stay in the Hierarchy.

You can switch **View Mode** on either prefab. **2D Walk** makes the camera orthographic. **3D Fly** makes it perspective again.

## 3D: FlyCamera

While it is enabled, this camera owns the cursor. Holding the right mouse button hides and locks it. Releasing the button, or pressing Esc, shows it again.

| Input | Action |
|------|------|
| W A S D, or arrow keys | Move along the view |
| Q, or Ctrl | Down |
| E, or Space | Up |
| Hold the right mouse button and move | Look |
| Shift | Faster |
| Scroll wheel | Change the base speed |
| Esc | Release the mouse |

Speed is in world units per second. Sensitivity is degrees per pixel. The scroll wheel stays between **Min Speed** and **Max Speed**.

## 2D: WalkCamera2D

The camera keeps its current Z and rotation and moves on the XY plane, so you can walk across a 2D level. It starts at `(0, 0, -10)` with an orthographic size of 5, matching Unity's 2D camera.

| Input | Action |
|------|------|
| W A S D, or arrow keys | Pan up, down, left, and right |
| Hold the right or middle mouse button and drag | Grab and pan the view |
| Scroll wheel | Zoom toward the cursor |
| Q / E | Zoom out / zoom in |
| Shift | Faster keyboard pan |

2D mode does not lock the cursor.

## Other

A control hint sits in the corner. Turn off **Show Controls Hint** on the prefab if you do not want it.

Movement still works when Time Scale is 0, so you can look around a paused scene. Both the old and new Input Systems work. The camera is set up for URP. In a project without URP, remove the Missing Script; the fly script still works.
