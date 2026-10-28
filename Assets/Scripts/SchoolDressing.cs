using UnityEngine;

/// <summary>
/// Marks the "SchoolProps" root that the Tools > Elevator Game > School tools fill with furniture from the
/// real campus. The building tools (colliders, wall thickness, gap closing, double-siding) skip roots with
/// this marker; the props bring their own box colliders.
/// </summary>
public class SchoolDressing : MonoBehaviour { }
