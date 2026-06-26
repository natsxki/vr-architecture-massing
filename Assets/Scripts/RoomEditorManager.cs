using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Handles manual translation and scaling of generated massing rooms.
/// Active ONLY when AppStateManager.IsEditingModeActive == true.
/// </summary>
public class RoomEditorManager : MonoBehaviour
{
    [Header("Raycast Setup")]
    [Tooltip("Reference to the Right Controller Transform.")]
    public Transform rayOrigin;
    public float maxDistance = 30f;

    [Header("Visual Feedback")]
    public Color rayColor = new Color(0.1f, 0.6f, 1f, 0.8f);      // Blue ray for editing
    public Color highlightColor = new Color(1f, 0.8f, 0.1f, 1f);  // Yellow/Orange for selection

    private InputDevice _rightController;
    private bool _rightFound;
    private float _nextSearch;

    private LineRenderer _lineRenderer;
    private GameObject _cursor;

    private bool _triggerWasPressed;
    private bool _bWasPressed;

    // --- Selection State ---
    private GameObject _selectedRoom;
    private Color _originalRoomColor;

    private void Awake()
    {
        BuildEditorPointer();
    }

    private void BuildEditorPointer()
    {
        // 1. Build Blue LineRenderer
        GameObject lineGo = new GameObject("Editor_Ray");
        lineGo.transform.SetParent(transform, false);
        _lineRenderer = lineGo.AddComponent<LineRenderer>();
        _lineRenderer.startWidth = 0.005f;
        _lineRenderer.endWidth = 0.005f;
        _lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        _lineRenderer.material.color = rayColor;
        _lineRenderer.enabled = false;

        // 2. Build Blue Cursor
        _cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _cursor.name = "Editor_Cursor";
        _cursor.transform.SetParent(transform, false);
        _cursor.transform.localScale = Vector3.one * 0.04f;
        Destroy(_cursor.GetComponent<Collider>()); // Cursor shouldn't block raycasts
        _cursor.GetComponent<Renderer>().material = _lineRenderer.material;
        _cursor.SetActive(false);
    }

    private void Update()
    {
        // 1. SLEEP if Editing Mode is OFF
        if (!AppStateManager.IsEditingModeActive)
        {
            _lineRenderer.enabled = false;
            _cursor.SetActive(false);

            // Safety: If mode is turned off while a room is selected, drop it
            DeselectCurrentRoom();
            return;
        }

        TryFindRightController();
        if (!_rightFound || rayOrigin == null) return;

        // 2. WAKE UP: Enable Ray and Cursor
        _lineRenderer.enabled = true;
        _cursor.SetActive(true);
        _lineRenderer.SetPosition(0, rayOrigin.position);

        // 3. Shoot Raycast
        bool hitSomething = Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, maxDistance);
        
        if (hitSomething)
        {
            _lineRenderer.SetPosition(1, hit.point);
            _cursor.transform.position = hit.point;
        }
        else
        {
            Vector3 endPos = rayOrigin.position + rayOrigin.forward * maxDistance;
            _lineRenderer.SetPosition(1, endPos);
            _cursor.transform.position = endPos;
        }

        // 4. Read Controller Inputs
        _rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerNow);
        _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow); // B button on Right Quest Controller

        // --- TRIGGER LOGIC: SELECT ---
        if (triggerNow && !_triggerWasPressed)
        {
            if (hitSomething)
            {
                // Heuristic: Is it a generated room? 
                // Rooms are cubes created under "Option_1" or "Option_2" parent by MassingGenerator
                Transform hitParent = hit.collider.transform.parent;
                if (hitParent != null && hitParent.name.StartsWith("Option_"))
                {
                    SelectRoom(hit.collider.gameObject);
                }
            }
        }

        // --- B BUTTON LOGIC: DESELECT ---
        if (bNow && !_bWasPressed)
        {
            DeselectCurrentRoom();
        }

        _triggerWasPressed = triggerNow;
        _bWasPressed = bNow;
    }

    private void SelectRoom(GameObject room)
    {
        // Ignore if clicking the already selected room
        if (_selectedRoom == room) return;

        // Clean up previously selected room first
        DeselectCurrentRoom();

        _selectedRoom = room;
        Renderer r = _selectedRoom.GetComponent<Renderer>();
        if (r != null)
        {
            _originalRoomColor = r.material.color;
            r.material.color = highlightColor; // Highlight it!
        }
        
        Debug.Log($"<color=cyan>[RoomEditor] Selected Room: {room.name}</color>");
        // NotificationManager.Instance?.ShowStatus($"Selected: {room.name}");
    }

    private void DeselectCurrentRoom()
    {
        if (_selectedRoom != null)
        {
            Renderer r = _selectedRoom.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = _originalRoomColor; // Restore original architecture color
            }
            _selectedRoom = null;
            Debug.Log("<color=cyan>[RoomEditor] Room deselected.</color>");
        }
    }

    private void TryFindRightController()
    {
        if (_rightFound) return;
        if (Time.time < _nextSearch) return;
        _nextSearch = Time.time + 2f;

        var list = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, list);
        if (list.Count > 0)
        {
            _rightController = list[0];
            _rightFound = true;
        }
    }
}