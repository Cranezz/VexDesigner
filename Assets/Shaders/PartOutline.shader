Shader "VexDesigner/PartOutline"
{
    // Draws a coloured border round a part by rendering the mesh a second
    // time, inside out and slightly fattened, so only the rim shows past the
    // real surface.
    //
    // Chosen over an emissive glow because an outline says something a glow
    // cannot. A frozen part is *marked*, not lit: the mark has to be legible
    // against pale aluminium and dark shadow alike, has to leave the part's
    // own colour readable, and must not be confused with the part simply
    // catching the light. Emission failed all three - a bright aluminium part
    // washed out and a dark one barely changed.
    //
    // Thickness is scaled by distance so the border stays the same width on
    // screen. A fixed offset in world units disappears across the room and
    // swallows the part when you lean in.
    Properties
    {
        _BaseColor ("Colour", Color) = (0.2, 0.55, 1, 1)
        _Thickness ("Thickness (pixels)", Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+1"
        }

        Pass
        {
            Name "Outline"

            // Front faces culled, so what remains is the back of the fattened
            // hull - visible only where it pokes out past the real part.
            Cull Front
            ZWrite On
            ZTest LEqual

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
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Thickness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(positionWS);

                // Pushed out in clip space rather than along the normal in
                // world space, which is what keeps the border a constant width
                // on screen however far away the part is.
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP,
                                      mul((float3x3)UNITY_MATRIX_M, input.normalOS));

                float2 offset = normalize(normalCS.xy);
                offset *= _Thickness * 2.0 / _ScreenParams.xy;

                output.positionCS.xy += offset * output.positionCS.w;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
