using UnityEngine;

/// <summary>
/// GameObject.CreatePrimitive for URP builds. A primitive made in code gets the built-in default material, which URP
/// draws magenta in a player build (the editor hides this). These get Resources/RuntimeLit (URP Lit), or
/// RuntimeLitGlow (URP Lit with _EMISSION on, so that variant is in the build too); being in Resources puts them
/// and their shader variants in every build. Colour them as before with renderer.material.color.
/// </summary>
public static class Primitives
{
    static Material lit, glow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => lit = glow = null;

    public static Material Lit => lit != null ? lit : lit = Resources.Load<Material>("RuntimeLit");
    public static Material Glow => glow != null ? glow : glow = Resources.Load<Material>("RuntimeLitGlow");

    /// <summary>A primitive with the URP material (glow: the emissive one).</summary>
    public static GameObject Make(PrimitiveType type, bool emissive = false)
    {
        var go = GameObject.CreatePrimitive(type);
        var m = emissive ? Glow : Lit;
        if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
        else Debug.LogWarning($"Primitives: Resources/{(emissive ? "RuntimeLitGlow" : "RuntimeLit")} is missing; {type} keeps the default material.");
        return go;
    }
}
