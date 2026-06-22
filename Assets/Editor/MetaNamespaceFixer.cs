#if UNITY_ANDROID
using System.IO;
using System.IO.Compression;
using UnityEditor.Android;
using UnityEngine;

// Patches InteractionSdk.aar and OVRPlugin.aar after Unity generates the Gradle project.
// Both AARs ship with package="com.oculus.Integration" which causes a namespace conflict
// in Android Gradle Plugin 8+. This runs before Gradle executes, so it's permanent per-build.
public class MetaNamespaceFixer : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string libs = Path.Combine(path, "libs");
        if (!Directory.Exists(libs)) return;

        PatchAar(Path.Combine(libs, "InteractionSdk.aar"), "com.oculus.Integration.interaction");
        PatchAar(Path.Combine(libs, "OVRPlugin.aar"),      "com.oculus.Integration.core");
    }

    static void PatchAar(string aarPath, string newPackage)
    {
        if (!File.Exists(aarPath)) return;

        string tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmp);
        try
        {
            ZipFile.ExtractToDirectory(aarPath, tmp);

            string manifest = Path.Combine(tmp, "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;

            string xml = File.ReadAllText(manifest);
            string patched = xml.Replace(
                "package=\"com.oculus.Integration\"",
                $"package=\"{newPackage}\"");

            if (xml == patched) return; // nothing to change

            File.WriteAllText(manifest, patched);
            File.Delete(aarPath);
            ZipFile.CreateFromDirectory(tmp, aarPath);

            Debug.Log($"[MetaNamespaceFixer] Patched {Path.GetFileName(aarPath)} → {newPackage}");
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}
#endif
