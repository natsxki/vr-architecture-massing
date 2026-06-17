using UnityEngine;

public class MassingGenerator : MonoBehaviour
{
    [Header("Materials")]
    public Material option1Material;
    public Material option2Material;

    [Header("Settings")]
    public float spacingBetweenOptions = 20f; 

    private GameObject currentOption1Parent;
    private GameObject currentOption2Parent;

    public void GenerateMassings(MuseumMassingResult data)
    {
        // Clean up previous generations
        if (currentOption1Parent) Destroy(currentOption1Parent);
        if (currentOption2Parent) Destroy(currentOption2Parent);

        // Create new container objects
        currentOption1Parent = new GameObject("Option_1_Massing");
        currentOption2Parent = new GameObject("Option_2_Massing");

        // Offset Option 2 for side-by-side comparison
        currentOption2Parent.transform.position = new Vector3(spacingBetweenOptions, 0, 0);

        BuildMuseum(data.option1, currentOption1Parent.transform, option1Material);
        BuildMuseum(data.option2, currentOption2Parent.transform, option2Material);

        Debug.Log("3D Massings Generated Successfully.");
    }

    private void BuildMuseum(MassingOption optionData, Transform parentNode, Material mat)
    {
        foreach (var room in optionData.rooms)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = room.roomName;
            cube.transform.SetParent(parentNode);
            
            cube.transform.localScale = new Vector3(room.scaleX, room.scaleY, room.scaleZ);
            
            // Offset Y so the building sits flush on the ground plane (Y=0)
            cube.transform.localPosition = new Vector3(room.posX, room.posY + (room.scaleY / 2f), room.posZ);
            
            if (mat != null)
            {
                cube.GetComponent<MeshRenderer>().material = mat;
            }
        }
    }
}