using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cyan line around an object the player can use (the elevator, the arcade box). It pulses gently all
/// the time and turns brighter and thicker while its ElevatorPlayZone shows the Play button; while the object can't
/// be used yet (locked, e.g. the elevator before the quest is cleared) it is a still, dim grey line. Only the rim
/// past the object's silhouette shows (the object hides the rest), and walls in front hide it too.
/// Added by Tools > Elevator Game > Outline Interactables; the model needs Read/Write enabled.
/// </summary>
public class InteractableOutline : MonoBehaviour
{
    [Tooltip("Material using the Elevator Game/Interactable Outline shader.")]
    public Material outlineMaterial;
    // named `tint` rather than `color` so scenes saved with the old orange pick up the palette's cyan
    public Color tint = UiKit.Accent;
    [Tooltip("Outline width in screen pixels when idle and when the player can interact.")]
    public float idleWidth = 3f, focusWidth = 6f;
    [Tooltip("How far behind the object the outline sits, as a fraction of its distance from the camera. " +
             "Raise it if the outline colour shows on the object's face; lower it if the line goes missing around small raised parts.")]
    public float depthPush = 0.002f;

    [Tooltip("Outline only part of one mesh, e.g. the elevator's button: the mesh...")]
    public MeshFilter partMesh;
    [Tooltip("...and the part's box in that mesh's own space. Triangles inside it are outlined.")]
    public Bounds partBounds;

    /// <summary>Set by ElevatorPlayZone while the player is close enough to interact.</summary>
    [System.NonSerialized] public bool focused;
    /// <summary>Set by ElevatorPlayZone while the object can't be used yet: dim grey, idle width, no pulse.</summary>
    [System.NonSerialized] public bool locked;

    static readonly Color LockedColor = new Color(0.42f, 0.44f, 0.5f);

    static readonly Dictionary<Mesh, Mesh> smoothed = new Dictionary<Mesh, Mesh>();
    static readonly int ColorId = Shader.PropertyToID("_Color"), WidthId = Shader.PropertyToID("_Width"),
        DepthPushId = Shader.PropertyToID("_DepthPush");
    Material material;
    float focus;

    // Start, not Awake: building scripts (WallThickness, BuildingCollision) swap meshes and add colliders in Awake
    void Start()
    {
        if (outlineMaterial == null) { enabled = false; return; }
        // The expanded copy, a little behind the object so only its rim shows
        material = new Material(outlineMaterial) { renderQueue = (int)RenderQueue.Transparent + 2 };
        material.SetFloat(DepthPushId, depthPush);
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
            if (partMesh != null && mf != partMesh) continue;
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"InteractableOutline: turn on Read/Write for '{mf.sharedMesh.name}' to outline it.", mf);
                continue;
            }
            var hull = new GameObject("Outline", typeof(MeshFilter), typeof(MeshRenderer));
            hull.transform.SetParent(mf.transform, false);
            var source = partMesh != null ? Part(mf.sharedMesh, partBounds) : mf.sharedMesh;
            hull.GetComponent<MeshFilter>().sharedMesh = Smoothed(source);
            var r = hull.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    void Update()
    {
        if (material == null) return;
        if (locked)
        {
            focus = 0f;
            Color g = LockedColor * (focused ? 1.25f : 1f);   // a touch lighter with the player in front of it
            g.a = 1f;
            material.SetColor(ColorId, g);
            material.SetFloat(WidthId, idleWidth);
            return;
        }
        focus = Mathf.MoveTowards(focus, focused ? 1f : 0f, Time.deltaTime * 5f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (focused ? 6f : 2.5f));
        Color c = Color.Lerp(tint * Mathf.Lerp(0.65f, 1f, pulse), tint * 1.4f, focus);
        c.a = 1f;
        material.SetColor(ColorId, c);
        material.SetFloat(WidthId, Mathf.Lerp(idleWidth, focusWidth, focus) * Mathf.Lerp(0.85f, 1.1f, pulse));
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    // Copy keeping only the triangles inside box (slightly enlarged)
    static Mesh Part(Mesh mesh, Bounds box)
    {
        box.Expand(box.size.magnitude * 0.05f + 1e-4f);
        var verts = mesh.vertices;
        var tris = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var t = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
                if (box.Contains(verts[t[i]]) && box.Contains(verts[t[i + 1]]) && box.Contains(verts[t[i + 2]]))
                    tris.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
        }
        var part = Instantiate(mesh);
        part.name = mesh.name + " (part)";
        part.subMeshCount = 1;
        part.SetTriangles(tris, 0);
        return part;
    }

    // Copy with normals averaged where vertices share a position, so hard edges don't split the outline open
    static Mesh Smoothed(Mesh mesh)
    {
        if (smoothed.TryGetValue(mesh, out var cached) && cached != null) return cached;
        var copy = Instantiate(mesh);
        copy.name = mesh.name + " (outline)";
        var verts = copy.vertices;
        var normals = copy.normals;
        if (normals.Length != verts.Length)
        {
            copy.RecalculateNormals();
            normals = copy.normals;
        }
        var sums = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < verts.Length; i++)
            sums[verts[i]] = (sums.TryGetValue(verts[i], out var s) ? s : Vector3.zero) + normals[i];
        for (int i = 0; i < verts.Length; i++)
        {
            var n = sums[verts[i]];
            if (n.sqrMagnitude > 1e-12f) normals[i] = n.normalized;
        }
        copy.normals = normals;
        smoothed[mesh] = copy;
        return copy;
    }
}
