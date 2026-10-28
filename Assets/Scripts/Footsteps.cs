using UnityEngine;

/// <summary>
/// Footstep sounds for the player (added by PlayerMovement). Steps follow the distance actually covered on the
/// ground, so they keep time with the run at every floor's scale and stop when a wall holds the player back: one
/// every strideBodyHeights × the character's height, the first one soon after setting off and a quieter settling
/// step on stopping. Landing after a fall plays a thud. A random step variant (never the same twice running) at a
/// slightly random pitch and volume, on 2D sources of the player's own.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class Footsteps : MonoBehaviour
{
    [Tooltip("Distance between steps, in body heights.")]
    public float strideBodyHeights = 0.55f;
    [Range(0f, 1f)] public float volume = 0.4f;
    [Tooltip("The step on stopping, relative to a normal step.")]
    [Range(0f, 1f)] public float settleVolume = 0.55f;
    [Tooltip("Time in the air before touching down counts as a landing.")]
    public float landAfter = 0.25f;

    // short dips off the ground (slopes, stair edges) don't count as leaving it
    const float AirGrace = 0.12f;

    PlayerMovement pm;
    AudioSource[] sources;
    int nextSource, lastVariant = -1;
    Vector3 lastPos;
    float travelled, runDistance, airTime, lastStep;
    bool walking, everGrounded;

    float Height => pm.WorldHeight > 0f ? pm.WorldHeight : 1f;
    float Stride => Mathf.Max(0.01f, strideBodyHeights * Height);

    void Awake()
    {
        pm = GetComponent<PlayerMovement>();
        // one-shots are pitched per source, so a few take turns and a step never bends the one still ringing
        sources = new AudioSource[3];
        for (int i = 0; i < sources.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            sources[i] = s;
        }
    }

    void OnEnable()
    {
        lastPos = transform.position;
        walking = false;
        travelled = 0f;
    }

    void Update()
    {
        Vector3 pos = transform.position;
        Vector3 d = pos - lastPos;
        lastPos = pos;
        d.y = 0f;
        float moved = d.magnitude;
        if (moved > 2f * Height)   // a teleport (spawn, back from a minigame): not a step
        {
            walking = false;
            travelled = 0f;
            return;
        }

        // landing
        if (pm.Grounded)
        {
            if (everGrounded && airTime > landAfter && !PlayerLock.Locked) Play(Sfx.Land(), volume * 1.3f, Random.Range(0.92f, 1.05f));
            airTime = 0f;
            everGrounded = true;
        }
        else airTime += Time.deltaTime;

        bool onGround = pm.Grounded || airTime < AirGrace;
        bool now = onGround && pm.Moving && !PlayerLock.Locked;
        if (now && !walking)
        {
            travelled = 0.75f * Stride;   // the first step comes quickly
            runDistance = 0f;
        }
        else if (!now && walking)
        {
            // settling on the spot, unless a step just played, the player left the ground or never got going (a wall)
            if (onGround && runDistance >= 0.25f * Stride && Time.time - lastStep > 0.12f) Step(settleVolume);
            travelled = 0f;
        }
        walking = now;
        if (!walking) return;

        travelled += moved;
        runDistance += moved;
        if (travelled >= Stride)
        {
            travelled %= Stride;
            Step(1f);
        }
    }

    void Step(float scale)
    {
        // any variant but the last one
        int v = lastVariant < 0 ? Random.Range(0, 4) : (lastVariant + Random.Range(1, 4)) % 4;
        lastVariant = v;
        lastStep = Time.time;
        Play(Sfx.Step(v), volume * scale * Random.Range(0.8f, 1f), Random.Range(0.9f, 1.1f));
    }

    void Play(AudioClip clip, float vol, float pitch)
    {
        var s = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;
        s.pitch = pitch;
        s.PlayOneShot(clip, vol);
    }
}
