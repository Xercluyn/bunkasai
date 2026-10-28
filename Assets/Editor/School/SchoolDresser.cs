using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Dress School: dresses a floor like the real campus in one go: textured floor,
/// walls, ceiling and pillars (ShellDresser), furniture recoloured to the facility photos (PropRecolour),
/// campus props placed and kept where you moved them (PropsLower 1F–3F, PropsUpper 4F–5F), the fixed room
/// layouts (SchoolLayout3F / 5F), then the floor's light mood (Fix Indoor Lighting). Every step is safe to run
/// again, and props deleted by hand are never placed again. Run Make Walls Double-Sided first.
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

    [MenuItem("Tools/Elevator Game/School/Arrange Rooms (2F, 3F, 5F)")]
    static void ArrangeRooms() => SchoolKit.ForFloors("Arrange Rooms", 2, 5, Arrange);

    static void Dress(Scene scene, int floor)
    {
        ShellDresser.Apply(scene, floor);
        PropRecolour.Apply(scene, floor);
        PlaceProps(scene, floor);
        Arrange(scene, floor);
        IndoorLightingSetup.Fix();   // works on the active scene, which is this floor
    }

    static void PlaceProps(Scene scene, int floor)
    {
        PropsLower.Apply(scene, floor);
        PropsUpper.Apply(scene, floor);
    }

    // The fixed room layouts (each a no-op on other floors): 2F reception door, 3F desks and drafting rows,
    // 5F laptops, care/CE and the alley cut through the corridor wall (after the surfaces, so the hole stays)
    static void Arrange(Scene scene, int floor)
    {
        SchoolLayout2F.Apply(scene, floor);
        SchoolLayout3F.Apply(scene, floor);
        SchoolLayout5F.Apply(scene, floor);
        AlleyCutter.Apply(scene, floor);
    }
}
