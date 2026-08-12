Shader "VexDesigner/GizmoTransparent"
{
    // Companion to GizmoOverlay for the free-rotation ball.
    //
    // A separate shader rather than a variant because the two need opposite
    // depth behaviour: the arrows and rings are opaque and drawn over
    // everything, while the ball has to be faint enough to see the part
    // through, which means alpha blending and no depth write.
    //
    // Back faces are drawn first so the sphere reads as a hollow shell rather
    // than a flat disc - without that, the near and far surfaces blend in an
    // order that changes with the view and the ball appears to flicker.

    Properties
    {
        _BaseColor ("Base Colour", Color) = (1,1,1,0.12)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "GizmoBallBack"
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                return half4(_BaseColor.rgb, _BaseColor.a * 0.5);
            }
            ENDHLSL
        }

        Pass
        {
            Name "GizmoBallFront"
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // Brighter at the silhouette, so the ball reads as a sphere
                // and its extent is obvious without obscuring what is inside.
                float facing = 1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                float alpha = _BaseColor.a * (0.35 + facing * 1.4);
                return half4(_BaseColor.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
