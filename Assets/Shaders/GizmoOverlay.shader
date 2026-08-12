Shader "VexDesigner/GizmoOverlay"
{
    // Draws the transform gizmo on top of everything.
    //
    // A gizmo hidden inside the part it is manipulating is useless, and that
    // is the normal case here: the handles sit at the assembly's centre, which
    // for a C-channel is inside solid aluminium. ZTest Always draws it
    // regardless of depth, which is what every 3D tool does with its gizmo.
    //
    // Unlit on purpose - a handle that changes brightness with the room
    // lighting reads as an object in the scene rather than as a control.

    Properties
    {
        _BaseColor ("Base Colour", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "GizmoOverlay"

            // The three lines that matter: draw over everything, contribute no
            // depth of its own, and stay visible from both sides so a ring seen
            // edge-on does not vanish.
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // A touch of face-angle shading so the arrow still reads as a
                // three-dimensional object rather than a flat silhouette.
                float facing = saturate(dot(normalize(input.normalWS), float3(0.3, 0.8, 0.5)) * 0.5 + 0.6);
                return half4(_BaseColor.rgb * facing, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
