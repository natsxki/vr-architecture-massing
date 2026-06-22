using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Y button (left controller secondaryButton) pops and executes the top undo action.
/// Other systems push actions here before making a destructive change.
/// </summary>
public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }

    [Header("References")]
    public GroundAnchorManager anchorManager;
    public MassingGenerator    massingGenerator;

    private readonly Stack<IUndoAction> _stack = new Stack<IUndoAction>();

    private InputDevice _left;
    private bool _leftFound;
    private float _nextSearch;
    private bool _yWasPressed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        TryFindLeft();
        if (!_leftFound) return;

        // Y = secondaryButton on the left Meta Quest controller
        _left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool yNow);

        if (yNow && !_yWasPressed) DoUndo();
        _yWasPressed = yNow;
    }

    // -------------------------------------------------------------------------
    // Push actions (called by other systems before changing state)
    // -------------------------------------------------------------------------

    public void PushAnchor(Vector3 prevPos, bool hadAnchor)
        => _stack.Push(new AnchorAction(anchorManager, prevPos, hadAnchor));

    public void PushMassing(GameObject prev1, GameObject prev2)
        => _stack.Push(new MassingAction(massingGenerator, prev1, prev2));

    public void PushDescription(string section, string prevText)
        => _stack.Push(new DescriptionAction(section, prevText));

    // -------------------------------------------------------------------------

    private void DoUndo()
    {
        if (_stack.Count == 0)
        {
            NotificationManager.Instance?.ShowWarning("Nothing left to undo.");
            return;
        }
        _stack.Pop().Undo();
    }

    private void TryFindLeft()
    {
        if (_leftFound) return;
        if (Time.time < _nextSearch) return;
        _nextSearch = Time.time + 2f;

        var list = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, list);
        if (list.Count > 0) { _left = list[0]; _leftFound = true; }
    }
}

// =============================================================================
// Undo action types
// =============================================================================

public interface IUndoAction { void Undo(); }

public class AnchorAction : IUndoAction
{
    private readonly GroundAnchorManager _mgr;
    private readonly Vector3 _prevPos;
    private readonly bool _hadAnchor;

    public AnchorAction(GroundAnchorManager mgr, Vector3 prevPos, bool hadAnchor)
    { _mgr = mgr; _prevPos = prevPos; _hadAnchor = hadAnchor; }

    public void Undo() => _mgr.RestoreAnchor(_prevPos, _hadAnchor);
}

public class MassingAction : IUndoAction
{
    private readonly MassingGenerator _gen;
    private readonly GameObject _p1, _p2;

    public MassingAction(MassingGenerator gen, GameObject p1, GameObject p2)
    { _gen = gen; _p1 = p1; _p2 = p2; }

    public void Undo() => _gen.RestorePrevious(_p1, _p2);
}

public class DescriptionAction : IUndoAction
{
    private readonly string _section, _prevText;

    public DescriptionAction(string section, string prevText)
    { _section = section; _prevText = prevText; }

    public void Undo() => MuseumDescriptionPanel.Instance?.RestoreSection(_section, _prevText);
}
