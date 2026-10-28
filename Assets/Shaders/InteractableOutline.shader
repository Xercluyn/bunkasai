// Outline drawn around interactable objects (InteractableOutline.cs): a copy of the mesh pushed out along its
// smoothed normals by a fixed number of screen pixels, in one flat colour. The copy is also moved a little
// farther from the camera, so the object itself hides it everywhere except the thin rim sticking out past its
// silhouette: a line around the object, not a coat of paint over it. Depth-tested, so walls in front hide it.
// No stencil, so it works with the deferred renderer, which uses the stencil buffer itself.
Shader "Elevator Game/Interactable Outline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.55, 0.1, 1)
        _Width ("Width (pixels)", Float) = 4
        _DepthPush ("Depth Push (fraction of distance)", Float) = 0.002
    }
    SubShader
    {
        // After the skybox, which would otherwise paint over the parts of the ring against the sky
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _DepthPush;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Slide the copy back along the view ray: same spot on screen, slightly behind the real surface
                float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                positionVS *= 1.0 + _DepthPush;
                float4 positionCS = TransformWViewToHClip(positionVS);

                float3 normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS), true);
                float2 dirCS = mul((float2x2)UNITY_MATRIX_P, normalVS.xy);
                // Faces turned toward the camera stay put instead of sliding sideways out of the shape
                float edge = saturate(length(normalVS.xy) * 3.0);
                // Same width in pixels at any distance: offset in clip space, scaled back up by w
                float2 offset = normalize(dirCS + 1e-6) * edge * _Width * 2.0 / _ScreenParams.xy;
                positionCS.xy += offset * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
