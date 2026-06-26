# VR Architectural Concept Generator 

An interactive Virtual Reality prototype that leverages generative AI to instantly translate spoken architectural concepts into 3D massing models. Designed for the Meta Quest, this tool allows users to rapidly iterate on spatial designs for a small sensory museum using just their voice (and eventually the VR set controller).

This project was made by [Yue Jia](https://github.com/mzozozn), [Yongyan Luo](https://github.com/Lomerson) and [Lylia Mesa](https://github.com/natsxki) :)


## Project Overview

Our project bridges the gap between conceptual ideation and 3D visualization. By describing the desired characteristics and positions of specific spaces (Entrance Hall, Light Gallery, Sound Gallery, and Café, by default), the system generates two distinct procedural massing options in real-time. 

The generated models are simplified representations using colored rectangular volumes (boxes) at a 1:100 scale, prioritizing spatial relationships and proportions over granular detail.

## Core Features

- **Voice-Driven Iteration:** No complex UI or controllers needed for modeling; users simply speak their design intentions.
- **AI Massing Generation:** Translates qualitative descriptions (e.g., "wide and low", "tall and narrow") into quantitative spatial coordinates using an LLM.
- **Procedural Mesh Generation:** Dynamically builds simple, distinctively colored cubic volumes in Unity based on the LLM's JSON output.
- **Comparative Options:** Generates two alternative interpretations for every prompt to encourage design exploration (and further iteraitons).

## Tech Stack

- **Game Engine:** Unity + XR Interaction Toolkit 
- **Hardware:** Meta Quest
- **Speech-to-Text:** Whisper API 
- **LLM:** Gemini

## System Architecture & Workflow

1. **Audio Capture:** The user speaks their design idea inside the VR environment.
2. **Transcription:** The audio is processed via Whisper API and converted into text.
3. **Prompting:** The text is sent to the LLM to process the spatial constraints.
4. **JSON Response:** The LLM returns structured data containing dimensions and coordinates for two distinct layout options.
5. **Procedural Generation:** Unity parses the JSON and instantiates colored primitives at a 1:100 scale (units in meters) in the scene.
6. **Iteration:** The user observes the massing, speaks a new prompt to adjust or completely change the design, and the cycle repeats.

## Example Interaction

**Place selection in the scene with the controller**

**User Voice Input (step by step):** 
> *"Entrance hall is wide and low at the center.*
> *Light gallery is tall and narrow on the left."*

**System Output (JSON from LLM):**
```json
{
  "option_A": [
    {
      "label": "entrance",
      "x": 0, "y": 0, "z": 0,
      "w": 15, "d": 10, "h": 4
    },
    {
      "label": "light gallery",
      "x": 16, "y": 0, "z": 0,
      "w": 8, "d": 8, "h": 12
    }
  ],
  "option_B": [
    {
      "label": "entrance",
      "x": 0, "y": 0, "z": 0,
      "w": 18, "d": 12, "h": 5
    },
    {
      "label": "light gallery",
      "x": -10, "y": 0, "z": 0,
      "w": 6, "d": 6, "h": 15
    }
  ]
}
```
**Final output:** Two Massing options in Unity that can then be visited by the user.

---

## Recent Updates

### VR UX & Interaction

- **Guided phase flow** — A phase badge (upper center-right) displays the current step as *1/3 Position · 2/3 Design · 3/3 Visualization* with a colour-coded accent stripe and a contextual sub-hint that updates with each state (placing anchor, recording, transcribing, reviewing, generating, viewing).
- **Left-grip hint card** — Pressing the left grip at any time raises a detailed control-reference card anchored above the left controller. Suppressed in Visualization mode where the grip is already bound to option selection.
- **Phase-based pointer lock** — The ground-placement cursor is visible only during Position Selection; it disappears as soon as an anchor is placed and only returns if the user presses B to reposition.
- **Locomotion lock** — Joystick movement and rotation are blocked whenever any menu is open (radial menu or option-selection menu).
- **Jump vertical-wobble fix** — Ground Y is re-sampled at jump start; the rig Y is only modified during the arc and never pinned each frame, eliminating the post-jump judder caused by Quest's floor-tracking fighting manual Y clamping.

### AI Generation

- **Gemini 3-model fallback** — If the primary model (`gemini-2.5-flash-lite`) fails or hits a quota limit, the system automatically rotates through `gemini-2.0-flash` and `gemini-1.5-flash` with a 2 s delay between attempts. Both the massing generation and the description summarisation use this retry chain.
- **Iteration mode** — After viewing a generated option, the user can select *Iterate* from the option menu. The current option's JSON is stored as a base; the next voice recording is sent to Gemini with an instruction to refine the existing design rather than generate from scratch.
- **Description summarisation** — Each voice recording is summarised and merged into a running keyword map by Gemini (e.g. `hall: large, bright, central`). The merged summary is displayed in the phase badge panel and sent as context when the user confirms generation.

### UI

- **Option-selection radial menu** — Left or right grip in Visualization mode opens a 3-item thumbstick radial: *Visualize* (real-scale placement) · *Iterate* (refine with voice) · *Cancel*.
- **Floating option labels** — Each generated massing has a billboard label (*◀ Option A / Left Grip* and *Option B ▶ / Right Grip*) rendered on a world-space canvas so it remains legible at any distance.
- **Description panel** — The merged design summary is shown in a dynamic-height dark panel directly below the phase badge; the panel shrinks or grows to fit the text content.
- **Fade-to-black transition** — Selecting *Home* in the radial menu fades the view to black (0.45 s), resets all state, returns to the splash screen, then fades back in (0.6 s).
- **Warnings and status** — Error/warning messages and status toasts appear in the top-left corner of the FoV in large bold text; they are separate from the phase badge.
- **Google font** — All procedural UI text uses a single TMP font asset assigned in the Inspector and propagated globally via `NotificationManager.GlobalFont`.

### Environment

- **Auto-generated forest** — Trees are procedurally placed in a ring around the anchor point at start.
- **Invisible boundary walls** — Collision walls are generated automatically to keep the user within the play area.

### Usage

1. Open the Unity Project

Clone or download the project repository, then open the project folder with Unity Hub.  
Make sure the installed Unity version supports XR development and that the Android Build Support module is installed.

Recommended setup:

- Unity with Android Build Support
- XR Interaction Toolkit
- OpenXR Plugin
- Meta Quest connected through USB or available for standalone build deployment

2. Configure API Keys

The system requires external AI services for speech transcription and massing generation.

Before running the project, add your API keys for:

- OpenAI Whisper API, used for speech-to-text transcription
- Google Gemini API, used for architectural massing generation and description summarisation

The keys should be assigned in the corresponding Unity scripts or configuration fields used by the project, such as the voice transcription manager and the Gemini manager. Do not commit personal API keys to a public repository.

3. Set Up the XR Scene

Open the main VR scene in Unity.  
Check that the following components are active in the scene:

- XR Origin / VR camera rig
- AppStateManager
- VoiceCommandManager
- GeminiManager
- MassingGenerator
- GroundAnchorManager
- UI managers for phase badge, radial menus, status messages, and option labels

The scene should also include the procedural environment setup, including the forest and boundary walls.

4. Run in Unity or Build to Meta Quest

For quick testing, enter Play Mode in Unity with a connected XR setup if available.

For standalone Quest testing:

1. Connect the Meta Quest headset to the computer.
2. Switch the Unity build target to Android.
3. Enable OpenXR as the XR backend.
4. Select the connected Quest device in Build Settings.
5. Click **Build and Run**.

After installation, launch the application inside the headset.  
