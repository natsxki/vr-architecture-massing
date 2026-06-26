using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Handles manual translation and scaling of generated massing rooms.
/// Now features X, Z symmetric scaling, and bottom-locked Y scaling.
/// </summary>
public class RoomEditorManager : MonoBehaviour
{
    [Header("Raycast Setup")]
    public Transform rayOrigin;
    public float maxDistance = 30f;

    [Header("Visual Feedback & Handles")]
    public Color rayColor = new Color(0.1f, 0.6f, 1f, 0.8f);
    public float handleSize = 0.05f;   // 调小了 Handle 的尺寸（5cm）
    public float handleOffset = 0.05f; // Handle 距离墙面的间隙（5cm）

    private InputDevice _rightController;
    private bool _rightFound;
    private float _nextSearch;

    private LineRenderer _lineRenderer;
    private GameObject _cursor;

    private bool _triggerWasPressed;
    private bool _bWasPressed;

    // --- Selection & Editing State ---
    private GameObject _selectedRoom;
    private GameObject _handlesRoot;
    private Transform[] _handles = new Transform[5]; // 0:+X, 1:-X, 2:+Z, 3:-Z, 4:+Y(Top)

    private enum DragMode { None, Translating, ScalingX, ScalingZ, ScalingY }
    private DragMode _currentDragMode = DragMode.None;
    
    // Dragging Offsets
    private Vector3 _dragOffset;
    private float _yDragOffset;
    private Plane _verticalDragPlane; // Used specifically for Y-axis dragging

    private void Awake()
    {
        BuildEditorPointer();
    }

    private void BuildEditorPointer()
    {
        GameObject lineGo = new GameObject("Editor_Ray");
        lineGo.transform.SetParent(transform, false);
        _lineRenderer = lineGo.AddComponent<LineRenderer>();
        _lineRenderer.startWidth = 0.005f;
        _lineRenderer.endWidth = 0.005f;
        _lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        _lineRenderer.material.color = rayColor;
        _lineRenderer.enabled = false;

        _cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _cursor.name = "Editor_Cursor";
        _cursor.transform.SetParent(transform, false);
        _cursor.transform.localScale = Vector3.one * 0.04f;
        Destroy(_cursor.GetComponent<Collider>());
        _cursor.GetComponent<Renderer>().material = _lineRenderer.material;
        _cursor.SetActive(false);
    }

    private void Update()
    {
        if (!AppStateManager.IsEditingModeActive)
        {
            if (_lineRenderer.enabled) _lineRenderer.enabled = false;
            if (_cursor.activeSelf) _cursor.SetActive(false);
            DeselectCurrentRoom();
            return;
        }

        TryFindRightController();
        if (!_rightFound || rayOrigin == null) return;

        _lineRenderer.enabled = true;
        _cursor.SetActive(true);
        _lineRenderer.SetPosition(0, rayOrigin.position);

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, maxDistance);

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

        _rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerNow);
        _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bNow);

        // --- B BUTTON: DESELECT ---
        if (bNow && !_bWasPressed)
        {
            DeselectCurrentRoom();
        }

        // --- TRIGGER LOGIC ---
        if (triggerNow)
        {
            if (!_triggerWasPressed) 
            {
                // Trigger JUST pressed down
                if (hitSomething)
                {
                    Transform hitT = hit.collider.transform;

                    // A. Did we hit a Scale Handle?
                    if (hitT.name.StartsWith("Handle_"))
                    {
                        if (hitT.name.EndsWith("X")) _currentDragMode = DragMode.ScalingX;
                        else if (hitT.name.EndsWith("Z")) _currentDragMode = DragMode.ScalingZ;
                        else if (hitT.name.EndsWith("Y")) 
                        {
                            _currentDragMode = DragMode.ScalingY;
                            // 对于 Y 轴调整，我们需要生成一个面向玩家的垂直平面
                            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : rayOrigin.position;
                            Vector3 planeNormal = camPos - hitT.position;
                            planeNormal.y = 0; // 抹平Y，使其绝对垂直
                            if (planeNormal.sqrMagnitude < 0.001f) planeNormal = Vector3.forward;
                            _verticalDragPlane = new Plane(planeNormal.normalized, hitT.position);

                            if (_verticalDragPlane.Raycast(ray, out float enterDist))
                            {
                                _yDragOffset = hitT.position.y - ray.GetPoint(enterDist).y;
                            }
                        }
                    }
                    // B. Did we hit a Room Body?
                    else if (hitT.parent != null && hitT.parent.name.StartsWith("Option_"))
                    {
                        if (_selectedRoom != hitT.gameObject) SelectRoom(hitT.gameObject);
                        
                        _currentDragMode = DragMode.Translating;
                        
                        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, _selectedRoom.transform.position.y, 0));
                        if (groundPlane.Raycast(ray, out float enterDist))
                        {
                            Vector3 planeHit = ray.GetPoint(enterDist);
                            _dragOffset = _selectedRoom.transform.position - planeHit;
                        }
                    }
                }
            }
            else 
            {
                // Trigger HELD down
                if (_currentDragMode != DragMode.None && _selectedRoom != null)
                {
                    if (_currentDragMode == DragMode.ScalingY)
                    {
                        // --- 垂直拉伸算法 (Locked Bottom) ---
                        if (_verticalDragPlane.Raycast(ray, out float enterDist))
                        {
                            Vector3 planeHit = ray.GetPoint(enterDist);
                            float desiredHandleY = planeHit.y + _yDragOffset;
                            float targetTopY = desiredHandleY - handleOffset;

                            // 计算底部所在的绝对 Y 坐标（保证向上拉伸时底部不陷入地下）
                            float bottomY = _selectedRoom.transform.position.y - (_selectedRoom.transform.localScale.y / 2f);
                            
                            // 防止高度变成负数导致翻转
                            float newScaleY = Mathf.Max(0.02f, targetTopY - bottomY);

                            _selectedRoom.transform.localScale = new Vector3(_selectedRoom.transform.localScale.x, newScaleY, _selectedRoom.transform.localScale.z);
                            // 重置中心点位置
                            _selectedRoom.transform.position = new Vector3(_selectedRoom.transform.position.x, bottomY + newScaleY / 2f, _selectedRoom.transform.position.z);
                        }
                    }
                    else
                    {
                        // --- 平移与水平对称拉伸算法 ---
                        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, _selectedRoom.transform.position.y, 0));
                        if (groundPlane.Raycast(ray, out float enterDist))
                        {
                            Vector3 planeHit = ray.GetPoint(enterDist);

                            if (_currentDragMode == DragMode.Translating)
                            {
                                Vector3 newPos = planeHit + _dragOffset;
                                newPos.y = _selectedRoom.transform.position.y;
                                _selectedRoom.transform.position = newPos;
                            }
                            else if (_currentDragMode == DragMode.ScalingX)
                            {
                                float newScaleX = Mathf.Abs(planeHit.x - _selectedRoom.transform.position.x) * 2f;
                                newScaleX = Mathf.Max(0.02f, newScaleX); 
                                _selectedRoom.transform.localScale = new Vector3(newScaleX, _selectedRoom.transform.localScale.y, _selectedRoom.transform.localScale.z);
                            }
                            else if (_currentDragMode == DragMode.ScalingZ)
                            {
                                float newScaleZ = Mathf.Abs(planeHit.z - _selectedRoom.transform.position.z) * 2f;
                                newScaleZ = Mathf.Max(0.02f, newScaleZ);
                                _selectedRoom.transform.localScale = new Vector3(_selectedRoom.transform.localScale.x, _selectedRoom.transform.localScale.y, newScaleZ);
                            }
                        }
                    }
                    UpdateHandlesPosition();
                }
            }
        }
        else
        {
            _currentDragMode = DragMode.None;
        }

        _triggerWasPressed = triggerNow;
        _bWasPressed = bNow;
    }

    private void SelectRoom(GameObject room)
    {
        DeselectCurrentRoom();
        _selectedRoom = room;

        _handlesRoot = new GameObject("RoomScaleHandles");
        
        Material matSide = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        matSide.color = new Color(0.2f, 0.8f, 1f, 0.9f); // Blue for X/Z

        Material matTop = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        matTop.color = new Color(1f, 0.8f, 0.2f, 0.9f); // Yellow for Y (Height)

        _handles[0] = CreateHandle("Handle_PosX", matSide);
        _handles[1] = CreateHandle("Handle_NegX", matSide);
        _handles[2] = CreateHandle("Handle_PosZ", matSide);
        _handles[3] = CreateHandle("Handle_NegZ", matSide);
        _handles[4] = CreateHandle("Handle_PosY", matTop);

        UpdateHandlesPosition();
    }

    private Transform CreateHandle(string handleName, Material mat)
    {
        GameObject h = GameObject.CreatePrimitive(PrimitiveType.Cube);
        h.name = handleName;
        h.transform.SetParent(_handlesRoot.transform);
        h.transform.localScale = Vector3.one * handleSize;
        if (h.GetComponent<Renderer>()) h.GetComponent<Renderer>().material = mat;
        return h.transform;
    }

    private void UpdateHandlesPosition()
    {
        if (_selectedRoom == null || _handlesRoot == null) return;
        
        Vector3 center = _selectedRoom.transform.position;
        Vector3 extents = _selectedRoom.transform.localScale / 2f;

        _handles[0].position = center + new Vector3(extents.x + handleOffset, 0, 0);
        _handles[1].position = center + new Vector3(-extents.x - handleOffset, 0, 0);
        _handles[2].position = center + new Vector3(0, 0, extents.z + handleOffset);
        _handles[3].position = center + new Vector3(0, 0, -extents.z - handleOffset);
        
        // 第五个 Handle 在顶面正中心
        _handles[4].position = center + new Vector3(0, extents.y + handleOffset, 0);
    }

    private void DeselectCurrentRoom()
    {
        if (_selectedRoom != null)
        {
            _selectedRoom = null;
            _currentDragMode = DragMode.None;
        }
        if (_handlesRoot != null) Destroy(_handlesRoot);
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