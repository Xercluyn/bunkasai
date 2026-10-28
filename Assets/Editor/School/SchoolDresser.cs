using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Dress School: dresses a floor like the real campus in one go: textured floor,
/// walls, ceiling and pillars (ShellDresser), furniture recoloured to the facility photos (PropRecolour),
/// campus props placed and kept where you moved them (PropsLower 1F–3F, PropsUpper 4F–5F), then the floor's
/// light mood (Fix Indoor Lighting). Every step is safe to run again. Run Make Walls Double-Sided first.
/// </summary>
public static class SchoolDresser
{
    const string Title = "Dress School";

    [MenuItem("Tools/Elevator Game/School/Dress School (This Floor)")]
    static void DressOpenFloor() => SchoolKit.OnOpenFloor(Title, Dress);

    [MenuItem("Tools/Elevator Game/School/Dress School (All Floors)")]
    static void DressAllFloors() => SchoolKit.ForFloors(Title, 1, 5, Dress);

    [MenuItem("Tools/Elevator Game/School/Place School Props (All Floors)")]
    static void PlaceAllProps() => SchoolKit.ForFloors("Place School Props", 1, 5, PlaceProps);

    static void Dress(Scene scene, int floor)
    {
        ShellDresser.Apply(scene, floor);
        PropRecolour.Apply(scene, floor);
        PlaceProps(scene, floor);
        IndoorLightingSetup.Fix();   // works on the active scene, which is this floor
    }

    static void PlaceProps(Scene scene, int floor)
    {
        PropsLower.Apply(scene, floor);
        PropsUpper.Apply(scene, floor);
    }
}
