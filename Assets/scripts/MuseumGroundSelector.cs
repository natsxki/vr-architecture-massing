using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class MuseumGroundSelector : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public SpeechToTextUI speechToTextUI;

    [Header("Ground Settings")]
    public float groundSize = 20f;
    public float groundDistanceFromCamera = 5f;
    public float groundHeightBelowCamera = 1.2f;
    public Color groundColor = new Color(0.45f, 0.9f, 0.45f, 1f);
    public Color markerColor = new Color(1f, 0.75f, 0.1f, 1f);

    [Header("Hint UI")]
    public Vector3 hintLocalOffset = new Vector3(0f, 0.2f, 2.0f);

    private GameObject groundObject;
    private GameObject markerObject;

    private Canvas hintCanvas;
    private Text hintText;

    private bool hasSelectedPosition = false;
    private bool wasXRTriggerPressed = false;
    private bool isInitialized = false;

    void Start()
    {
        if (cameraTransform == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                cameraTransform = mainCam.transform;
            }
        }

        if (speechToTextUI == null)
        {
            speechToTextUI = FindObjectOfType<SpeechToTextUI>();
        }

        if (speechToTextUI != null)
        {
            speechToTextUI.HidePromptUI();
        }

        StartCoroutine(InitializeAfterCameraReady());
    }

    private IEnumerator InitializeAfterCameraReady()
    {
        // Wait for XR Camera Rig / CenterEyeAnchor to update its real pose.
        yield return null;
        yield return null;
        yield return null;

        CreateGround();
        CreateMarker();
        CreateHintUI();

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateHintUIPosition();

        if (!hasSelectedPosition)
        {
            HandleGroundSelection();
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.G) && !hasSelectedPosition)
        {
            Vector3 testPosition = groundObject.transform.position;
            testPosition.y = GetGroundSurfaceY();
            SelectPosition(testPosition);
        }
#endif
    }

    private void CreateGround()
    {
        Vector3 groundCenter = Vector3.zero;

        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            groundCenter = cameraTransform.position + forward * groundDistanceFromCamera;
            groundCenter.y = cameraTransform.position.y - groundHeightBelowCamera;
        }
        else
        {
            groundCenter = new Vector3(0f, -1.2f, 5f);
        }

        groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundObject.name = "Museum Ground";
        groundObject.transform.position = groundCenter;
        groundObject.transform.localScale = new Vector3(groundSize, 0.04f, groundSize);

        Renderer renderer = groundObject.GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", groundColor);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", groundColor);
        }
        else
        {
            mat.color = groundColor;
        }

        renderer.material = mat;

        Debug.Log("Museum Ground created at: " + groundObject.transform.position);
    }

    private void CreateMarker()
    {
        markerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        markerObject.name = "Selected Museum Position Marker";
        markerObject.transform.localScale = new Vector3(0.35f, 0.03f, 0.35f);
        markerObject.SetActive(false);

        Renderer renderer = markerObject.GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", markerColor);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", markerColor);
        }
        else
        {
            mat.color = markerColor;
        }

        renderer.material = mat;

        Collider markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }
    }

    private void CreateHintUI()
    {
        GameObject canvasObj = new GameObject("Position Selection Hint UI");

        hintCanvas = canvasObj.AddComponent<Canvas>();
        hintCanvas.renderMode = RenderMode.WorldSpace;

        Camera cam = GetSelectionCamera();
        if (cam != null)
        {
            hintCanvas.worldCamera = cam;
        }

        RectTransform canvasRect = hintCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700, 120);
        canvasRect.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        Image bg = canvasObj.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.65f);
        bg.raycastTarget = false;

        GameObject textObj = new GameObject("Hint Text");
        textObj.transform.SetParent(canvasObj.transform, false);

        hintText = textObj.AddComponent<Text>();
        hintText.text = "Select a position to build the museum";
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.fontSize = 30;
        hintText.color = Color.white;
        hintText.alignment = TextAnchor.MiddleCenter;

        RectTransform textRect = hintText.GetComponent<RectTransform>();
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(660, 100);
    }

    private void UpdateHintUIPosition()
    {
        if (cameraTransform == null || hintCanvas == null || hasSelectedPosition)
        {
            return;
        }

        hintCanvas.transform.position = cameraTransform.TransformPoint(hintLocalOffset);
        hintCanvas.transform.rotation = Quaternion.LookRotation(hintCanvas.transform.position - cameraTransform.position);
    }

    private void HandleGroundSelection()
    {
        bool shouldSelect = false;
        Ray ray = new Ray();

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            Camera cam = GetSelectionCamera();

            if (cam == null)
            {
                Debug.LogWarning("No valid camera found for ground selection.");
                return;
            }

            ray = cam.ScreenPointToRay(Input.mousePosition);
            shouldSelect = true;

            Debug.Log("Mouse clicked. Camera: " + cam.name);
        }
#endif

#if UNITY_ANDROID
        bool triggerPressed = GetXRTriggerPressed();

        if (triggerPressed && !wasXRTriggerPressed)
        {
            if (cameraTransform != null)
            {
                ray = new Ray(cameraTransform.position, cameraTransform.forward);
                shouldSelect = true;
            }
        }

        wasXRTriggerPressed = triggerPressed;
#endif

        if (!shouldSelect)
        {
            return;
        }

        if (groundObject == null)
        {
            Debug.LogWarning("Ground object does not exist.");
            return;
        }

        float groundY = GetGroundSurfaceY();
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);

            Vector3 center = groundObject.transform.position;
            float halfSize = groundSize * 0.5f;

            bool insideGround =
                hitPoint.x >= center.x - halfSize &&
                hitPoint.x <= center.x + halfSize &&
                hitPoint.z >= center.z - halfSize &&
                hitPoint.z <= center.z + halfSize;

            if (insideGround)
            {
                SelectPosition(hitPoint);
                Debug.Log("Ground position selected: " + hitPoint);
            }
            else
            {
                Debug.Log("Clicked point is outside the ground area: " + hitPoint);
                Debug.Log("Ground center: " + center + ", half size: " + halfSize);
            }
        }
        else
        {
            Debug.Log("Mouse ray did not intersect the ground plane.");
        }
    }

    private float GetGroundSurfaceY()
    {
        if (groundObject == null)
        {
            return 0f;
        }

        return groundObject.transform.position.y + groundObject.transform.localScale.y * 0.5f;
    }

    private void SelectPosition(Vector3 position)
    {
        hasSelectedPosition = true;

        Vector3 markerPosition = position;
        markerPosition.y = GetGroundSurfaceY() + 0.03f;

        markerObject.transform.position = markerPosition;
        markerObject.SetActive(true);

        if (hintCanvas != null)
        {
            hintCanvas.gameObject.SetActive(false);
        }

        if (speechToTextUI != null)
        {
            speechToTextUI.OpenPromptUI(position);
        }
        else
        {
            Debug.LogWarning("Position selected, but SpeechToTextUI is not assigned.");
        }

        Debug.Log("Selected museum position: " + position);
    }

    private Camera GetSelectionCamera()
    {
        Camera cam = null;

        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                cam = cameraTransform.GetComponentInChildren<Camera>();
            }

            if (cam == null && cameraTransform.parent != null)
            {
                cam = cameraTransform.parent.GetComponentInChildren<Camera>();
            }
        }

        if (cam == null)
        {
            cam = Camera.main;
        }

        return cam;
    }

    private bool GetXRTriggerPressed()
    {
        List<InputDevice> devices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices
        );

        foreach (InputDevice device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
            {
                return true;
            }
        }

        return false;
    }
}
