using UnityEditor;
using UnityEngine.SceneManagement;
using static EditorKit;

/// <summary>
/// Tools > Elevator Game > School > Dress School: dresses a floor like the real campus in one go: textured floor,
/// walls, ceiling and pillars (ShellDresser), furniture recoloured to the facility photos (PropRecolour),
/// campus props placed and kept where you moved them (PropsLower 1F–3F, PropsUpper 4F–5F), the fixed room
/// layouts (SchoolLayout2F / 3F / 5F and the 5F alley), the 学園祭 posters along the player's route
/// (FestivalPosters), then the floor's light mood (Fix Indoor Lighting). Every
/// step is safe to run again, and props deleted by hand are never placed again. Run Make Walls Double-Sided first.
/// The single steps are under School > Steps.
/// </summary>
public static class SchoolDresser
{
    const string Title = "Dress School";
    const string Steps = "Tools/Elevator Game/School/Steps/";

    [MenuItem("Tools/Elevator Game/School/Dress School (This Floor)", false, 100)]
    static void DressOpenFloor() => OnOpenFloor(Title, Dress);

    [MenuItem("Tools/Elevator Game/School/Dress School (All Floors)", false, 101)]
    static void DressAllFloors() => ForFloors(Title, 1, 5, Dress);

    [MenuItem(Steps + "Place School Props (This Floor)", false, 130)]
    static void PlaceOpenProps() => OnOpenFloor("Place School Props", PlaceProps);

    [MenuItem(Steps + "Place School Props (All Floors)", false, 131)]
    static void PlaceAllProps() => ForFloors("Place School Props", 1, 5, PlaceProps);

    [MenuItem(Steps + "Arrange Rooms (This Floor)", false, 140)]
    static void ArrangeOpenFloor() => OnOpenFloor("Arrange Rooms", Arrange);

    [MenuItem(Steps + "Arrange Rooms (All Floors)", false, 141)]
    static void ArrangeRooms() => ForFloors("Arrange Rooms", 2, 5, Arrange);

    [MenuItem(Steps + "Hang Festival Posters (This Floor)", false, 145)]
    static void PostersOpenFloor() => OnOpenFloor("Hang Festival Posters", FestivalPosters.Apply);

    [MenuItem(Steps + "Hang Festival Posters (All Floors)", false, 146)]
    static void PostersAllFloors() => ForFloors("Hang Festival Posters", 1, 5, FestivalPosters.Apply);

    static void Dress(Scene scene, int floor)
    {
        ShellDresser.Apply(scene, floor);
        PropRecolour.Apply(scene, floor);
        PlaceProps(scene, floor);
        Arrange(scene, floor);
        FestivalPosters.Apply(scene, floor);   // after the layout, so it sees where everything ended up
        IndoorLightingSetup.Fix();   // works on the active scene, which is this floor
    }

    // Floors 1–3 are PropsLower's, 4–5 PropsUpper's (each a no-op on the other floors)
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
