Shader "VexDesigner/SawPreview"
{
    // Shows what a cut will take off, before it takes it.
    //
    // The part is drawn twice with the same mesh: once clipped to the side the
    // blade keeps, in its own colour, and once clipped to the side it removes,
    // in transparent red. Which side is which is decided per pixel against the
    // blade plane, so dragging the part along the fence or swinging the blade
    // updates instantly.
    //
    // Slicing the mesh for real on every frame was the alternative, and on a
    // thirteen-thousand-triangle C-channel it would rebuild a mesh for every
    // thousandth of an inch the part moved. Clipping costs one comparison per
    // pixel and is exact - the red is bounded by the same plane that will do
    // the cutting, so what is shown is what is removed.
    Properties
    {
        _BaseColor ("Colour", Color) = (0.7, 0.72, 0.76, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.85
        _Smoothness ("Smoothness", Range(0, 1)) = 0.55

        // xyz is the plane normal, w its distance from the origin, in world
        // space. Set from the saw every time anything moves.
        _CutPlane ("Cut plane", Vector) = (1, 0, 0, 0)

        // +1 draws the side the normal points at, -1 the other. One material
        // per side, differing only in this.
        _CutSide ("Cut side", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "SawPreview"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Metallic;
                float  _Smoothness;
                float4 _CutPlane;
                float  _CutSide;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Discarded on the wrong side of the blade. The two materials
                // together cover the whole part exactly once.
                float side = dot(_CutPlane.xyz, input.positionWS) + _CutPlane.w;
                clip(side * _CutSide);

                // Two-sided: the offcut is see-through, so its inside shows.
                float3 normal = normalize(input.normalWS);

                Light light = GetMainLight();
                float lambert = saturate(dot(normal, light.direction));

                half3 lit = _BaseColor.rgb *
                    ((light.color * lambert * 0.75) + unity_AmbientSky.rgb + 0.25);

                return half4(lit, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
