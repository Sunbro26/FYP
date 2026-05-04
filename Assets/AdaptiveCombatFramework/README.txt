============================================================
ADAPTIVE COMBAT FRAMEWORK (ACF) - v1.0
Project Title: Enhancing Player Engagement with Adaptive Game AI
============================================================

1. REQUIRED PACKAGES
This framework requires the following Unity packages to be installed via the Package Manager:
- Cinemachine
- Input System
- ML-Agents (v2.0+)

2. INITIAL PROJECT SETUP
Unity packages do not export project-specific tags and layers. 
You MUST manually create the following for the combat logic to function:
- TAG: Create a tag named "sword" (all lowercase).
- LAYER: Create a layer named "Enemy".

3. GETTING STARTED
   A) TESTING THE FRAMEWORK:
      1. Open the "ACF_DemoScene" included in this package.
      2. Everything is pre-configured. Simply press PLAY to test the combat loop.

   B) BUILDING A NEW SCENE FROM SCRATCH:
      If you want to use the framework in a new level, drag these 5 prefabs from 
      the Prefabs folder into your hierarchy in this order:
      1. ACF_CoreSystems (Handles logic, telemetry, and ML)
      2. ACF_CameraRig (Pre-configured Cinemachine cameras)
      3. ACF_MainUI (Health/Stamina bars and Death screen)
      4. ACF_Player_Template (Your player entity)
      5. ACF_Enemy_Template (The AI entity)
      
      Note: After dragging these into a NEW scene, you must manually link the 
      'Object References' in the Telemetry and FightLogger scripts (on the CoreSystems 
      object) to the Player/Enemy in that specific scene.

4. SWITCHING BETWEEN HUMAN AND AI (PROXY) CONTROL
To toggle who is controlling the Player (ACF_Player_Template):
- Select the Player object.
- Locate the "Behavior Parameters" component.
- Change the "Behavior Type" dropdown:
  A) HUMAN PLAY: Set to "Heuristic Only". 
     (Ensure your "Player Input" component is Enabled).
  B) AI PLAY: Set to "Inference Only" and drag your trained .onnx model into the "Model" slot.
     (Ensure your "Player Input" component is Disabled to prevent interference).

5. CONFIGURING ATTACKS
The AI utilizes a data-driven "Attack Library" found on the SkeletonAI script. 
Each attack in the list allows for fine-tuning of:
- Wind-up/Damage Window timings.
- Damage amounts and Stamina block costs.
- Parriability toggles.
- Quirk toggles (Tracking player vs Static animations like 360 spins).

6. DATA COLLECTION (IMITATION LEARNING)
The "FightLogger" component on ACF_CoreSystems will automatically generate CSV logs 
in your "Application.persistentDataPath" after every fight, tracking player 
proficiency, distance metrics, and attack distributions for ML training.

==========================================================
Developed by Effan Shakeel, Abdullah Hussain & Muniba Noor
==========================================================