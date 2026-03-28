Shader "Hidden/TronWireOverlay"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _EdgeIntensity ("Edge Intensity", Range(1, 10)) = 3.0
        _EdgeThreshold ("Edge Threshold", Range(0, 1)) = 0.1
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.5
        _BloomTex ("Bloom Texture", 2D) = "black" {}
        _BloomIntensity ("Bloom Intensity", Range(0, 5)) = 1.5
        _BlurSize ("Blur Size", Range(0, 5)) = 2.0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _CameraDepthNormalsTexture;
    float4 _CameraDepthNormalsTexture_TexelSize;
    sampler2D _BloomTex;
    half _EdgeIntensity;
    half _EdgeThreshold;
    half _EdgeAlpha;
    half _BloomIntensity;
    half _BlurSize;

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
    }

    void SampleDepthNormal(float2 uv, out float depth, out float3 normal)
    {
        float4 cdn = tex2D(_CameraDepthNormalsTexture, uv);
        DecodeDepthNormal(cdn, depth, normal);
    }

    // Detect edge strength at a given UV with offset
    float DetectEdge(float2 uv, float2 texel)
    {
        float dC, dL, dR, dU, dD, dTL, dTR, dBL, dBR;
        float3 nC, nL, nR, nU, nD, nTL, nTR, nBL, nBR;

        // 3x3 kernel for smoother edge detection
        SampleDepthNormal(uv, dC, nC);
        SampleDepthNormal(uv + float2(-texel.x, 0), dL, nL);
        SampleDepthNormal(uv + float2( texel.x, 0), dR, nR);
        SampleDepthNormal(uv + float2(0,  texel.y), dU, nU);
        SampleDepthNormal(uv + float2(0, -texel.y), dD, nD);
        SampleDepthNormal(uv + float2(-texel.x,  texel.y), dTL, nTL);
        SampleDepthNormal(uv + float2( texel.x,  texel.y), dTR, nTR);
        SampleDepthNormal(uv + float2(-texel.x, -texel.y), dBL, nBL);
        SampleDepthNormal(uv + float2( texel.x, -texel.y), dBR, nBR);

        // Skip background
        float maxDepthCutoff = 0.99;
        if (dC > maxDepthCutoff)
            return 0.0;

        // Suppress horizontal surfaces
        float horizontalSuppress = smoothstep(0.7, 0.9, abs(nC.y));

        // Sobel-style depth edge (weighted diagonals)
        float depthH = abs(-dTL - 2*dL - dBL + dTR + 2*dR + dBR);
        float depthV = abs(-dTL - 2*dU - dTR + dBL + 2*dD + dBR);
        float depthEdge = sqrt(depthH * depthH + depthV * depthV);
        depthEdge = smoothstep(_EdgeThreshold * 0.003, _EdgeThreshold * 0.015, depthEdge);

        // Sobel-style normal edge
        float normalH = length(-nTL - 2*nL - nBL + nTR + 2*nR + nBR);
        float normalV = length(-nTL - 2*nU - nTR + nBL + 2*nD + nBR);
        float normalEdge = sqrt(normalH * normalH + normalV * normalV);
        normalEdge = smoothstep(_EdgeThreshold * 0.3, _EdgeThreshold * 1.2, normalEdge);

        return saturate(depthEdge + normalEdge) * (1.0 - horizontalSuppress);
    }

    ENDCG

    SubShader
    {
        ZWrite Off
        ZTest Always
        Cull Off

        // Pass 0: Edge detection with AA — amplify scene color at edges
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 sceneColor = tex2D(_MainTex, i.uv);
                float2 texel = _CameraDepthNormalsTexture_TexelSize.xy;

                // Multi-sample AA: detect edges at sub-pixel offsets and average
                float edge = 0;
                edge += DetectEdge(i.uv, texel) * 0.4;
                edge += DetectEdge(i.uv + texel * float2( 0.25,  0.25), texel) * 0.15;
                edge += DetectEdge(i.uv + texel * float2(-0.25,  0.25), texel) * 0.15;
                edge += DetectEdge(i.uv + texel * float2( 0.25, -0.25), texel) * 0.15;
                edge += DetectEdge(i.uv + texel * float2(-0.25, -0.25), texel) * 0.15;

                float multiplier = lerp(1.0, _EdgeIntensity, edge * _EdgeAlpha);
                fixed3 finalColor = sceneColor.rgb * multiplier;

                return fixed4(finalColor, sceneColor.a);
            }
            ENDCG
        }

        // Pass 1: Extract bright edge pixels for bloom
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // Extract pixels brighter than threshold
                float brightness = max(col.r, max(col.g, col.b));
                float knee = 0.7;
                float soft = brightness - knee;
                soft = clamp(soft, 0, knee * 2);
                soft = soft * soft / (4.0 * knee + 0.00001);
                float contribution = max(soft, brightness - 1.0);
                contribution = max(contribution, 0) / max(brightness, 0.00001);
                return col * contribution;
            }
            ENDCG
        }

        // Pass 2: Horizontal gaussian blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag (v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * _BlurSize;
                fixed4 color = fixed4(0, 0, 0, 0);

                color += tex2D(_MainTex, i.uv + float2(-3.0 * texel.x, 0)) * 0.006;
                color += tex2D(_MainTex, i.uv + float2(-2.0 * texel.x, 0)) * 0.061;
                color += tex2D(_MainTex, i.uv + float2(-1.0 * texel.x, 0)) * 0.242;
                color += tex2D(_MainTex, i.uv)                              * 0.382;
                color += tex2D(_MainTex, i.uv + float2( 1.0 * texel.x, 0)) * 0.242;
                color += tex2D(_MainTex, i.uv + float2( 2.0 * texel.x, 0)) * 0.061;
                color += tex2D(_MainTex, i.uv + float2( 3.0 * texel.x, 0)) * 0.006;

                return color;
            }
            ENDCG
        }

        // Pass 3: Vertical gaussian blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag (v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * _BlurSize;
                fixed4 color = fixed4(0, 0, 0, 0);

                color += tex2D(_MainTex, i.uv + float2(0, -3.0 * texel.y)) * 0.006;
                color += tex2D(_MainTex, i.uv + float2(0, -2.0 * texel.y)) * 0.061;
                color += tex2D(_MainTex, i.uv + float2(0, -1.0 * texel.y)) * 0.242;
                color += tex2D(_MainTex, i.uv)                              * 0.382;
                color += tex2D(_MainTex, i.uv + float2(0,  1.0 * texel.y)) * 0.242;
                color += tex2D(_MainTex, i.uv + float2(0,  2.0 * texel.y)) * 0.061;
                color += tex2D(_MainTex, i.uv + float2(0,  3.0 * texel.y)) * 0.006;

                return color;
            }
            ENDCG
        }

        // Pass 4: Composite — add blurred bloom back onto the scene
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                fixed4 bloom = tex2D(_BloomTex, i.uv);
                return scene + bloom * _BloomIntensity;
            }
            ENDCG
        }
    }
    FallBack Off
}