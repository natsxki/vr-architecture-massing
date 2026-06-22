using System;
using UnityEngine;

public enum AppState
{
    Splash,
    Idle,
    PlacingAnchor,
    Recording,
    Transcribing,
    ReviewingTranscription,
    GeneratingMassing,
    ViewingOptions,
    MenuOpen
}

public enum AppPhase
{
    None,               // splash screen — nothing active
    PositionSelection,
    Design,
    Generation,
    Visualization
}

public class AppStateManager : MonoBehaviour
{
    public static AppStateManager Instance { get; private set; }

    public AppState CurrentState { get; private set; } = AppState.Splash;
    public AppPhase CurrentPhase { get; private set; } = AppPhase.None;

    // Fires with (previousState, newState)
    public event Action<AppState, AppState> OnStateChanged;
    // Fires with (previousPhase, newPhase)
    public event Action<AppPhase, AppPhase> OnPhaseChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetState(AppState newState)
    {
        if (newState == CurrentState) return;
        AppState oldState = CurrentState;
        CurrentState = newState;

        AppPhase newPhase = DerivePhase(newState);
        if (newPhase != CurrentPhase)
        {
            AppPhase oldPhase = CurrentPhase;
            CurrentPhase = newPhase;
            OnPhaseChanged?.Invoke(oldPhase, newPhase);
        }

        Debug.Log($"[AppState] {oldState} → {newState}  [{CurrentPhase}]");
        OnStateChanged?.Invoke(oldState, newState);
    }

    private AppPhase DerivePhase(AppState state)
    {
        switch (state)
        {
            case AppState.Splash:
                return AppPhase.None;
            case AppState.Idle:
                return AppPhase.PositionSelection;
            case AppState.PlacingAnchor:
                return AppPhase.Design;
            case AppState.Recording:
            case AppState.Transcribing:
            case AppState.ReviewingTranscription:
                return AppPhase.Design;
            case AppState.GeneratingMassing:
                return AppPhase.Generation;
            case AppState.ViewingOptions:
                return AppPhase.Visualization;
            case AppState.MenuOpen:
                return CurrentPhase; // keep current phase while menu is open
            default:
                return AppPhase.PositionSelection;
        }
    }
}
