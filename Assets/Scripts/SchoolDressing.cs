using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks the "SchoolProps" root that the Tools > Elevator Game > School tools fill with furniture from the
/// real campus. The building tools (colliders, wall thickness, gap closing, double-siding) skip roots with
/// this marker; the props bring their own box colliders.
/// It also records every prop id ever placed, so a prop deleted by hand is never placed again.
/// </summary>
public class SchoolDressing : MonoBehaviour
{
    [Tooltip("Ids of every school prop placed here. Ones missing from the children were deleted by hand and stay deleted; remove an id to let the tools place it again.")]
    public List<string> placed = new List<string>();

    [HideInInspector] public bool adopted;   // props placed before ids were recorded have been adopted
}
