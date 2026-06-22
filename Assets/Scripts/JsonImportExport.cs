using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Saves and loads MuseumMassingResult JSON files to Application.persistentDataPath/SavedMassings/.
/// Called by RadialMenu (Import / Export buttons).
/// </summary>
public class JsonImportExport : MonoBehaviour
{
    public static JsonImportExport Instance { get; private set; }

    [Header("References")]
    public MassingGenerator massingGenerator;

    // The last successfully generated result — set by AppFlowController after Gemini responds.
    public static MuseumMassingResult LastResult;

    private string SaveDir => Path.Combine(Application.persistentDataPath, "SavedMassings");

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Directory.CreateDirectory(SaveDir);
    }

    // -------------------------------------------------------------------------
    // Export
    // -------------------------------------------------------------------------

    public void ExportCurrent()
    {
        if (LastResult == null)
        {
            NotificationManager.Instance?.ShowWarning("Nothing to export — generate a massing first.");
            return;
        }

        string fileName = $"massing_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(SaveDir, fileName);

        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(LastResult, Formatting.Indented));
            NotificationManager.Instance?.ShowWarning($"Exported: {fileName}");
            Debug.Log($"[Export] Saved to {path}");
        }
        catch (Exception e)
        {
            NotificationManager.Instance?.ShowWarning("Export failed. Check storage permissions.");
            Debug.LogError($"[Export] {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Import  (loads the most recently saved file)
    // -------------------------------------------------------------------------

    public void ImportLatest()
    {
        if (!Directory.Exists(SaveDir))
        {
            NotificationManager.Instance?.ShowWarning("No saved massings found.");
            return;
        }

        string[] files = Directory.GetFiles(SaveDir, "*.json");
        if (files.Length == 0)
        {
            NotificationManager.Instance?.ShowWarning("No saved files found.");
            return;
        }

        Array.Sort(files);                    // alphabetical = chronological for our naming
        string latest = files[files.Length - 1];

        try
        {
            string json = File.ReadAllText(latest);
            MuseumMassingResult result = JsonConvert.DeserializeObject<MuseumMassingResult>(json);
            LastResult = result;
            massingGenerator.GenerateMassings(result);
            AppStateManager.Instance?.SetState(AppState.ViewingOptions);
            NotificationManager.Instance?.ShowWarning($"Loaded: {Path.GetFileName(latest)}");
        }
        catch (Exception e)
        {
            NotificationManager.Instance?.ShowWarning("Import failed — file may be corrupt.");
            Debug.LogError($"[Import] {e.Message}");
        }
    }
}
