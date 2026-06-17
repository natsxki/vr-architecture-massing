using System.Collections.Generic;

[System.Serializable]
public class RoomData
{
    public string roomName;
    public float posX, posY, posZ;
    public float scaleX, scaleY, scaleZ;
}

[System.Serializable]
public class MassingOption
{
    public string optionName;
    public string architecturalConcept; 
    public List<RoomData> rooms;
}

[System.Serializable]
public class MuseumMassingResult
{
    public MassingOption option1;
    public MassingOption option2;
}