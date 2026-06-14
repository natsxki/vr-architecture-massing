# VR Architectural Concept Generator 

An interactive Virtual Reality prototype that leverages generative AI to instantly translate spoken architectural concepts into 3D massing models. Designed for the Meta Quest, this tool allows users to rapidly iterate on spatial designs for a small sensory museum using just their voice (and eventually the VR set controller).

This project was made by @Lomerson, @zozozn, @natsxki :)


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

**User Voice Input:** 
> *"Entrance hall is wide and low at the center. Light gallery is tall and narrow on the left."*

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

**Final output:** Two Massing options in Unity that can then be visited by the user.
