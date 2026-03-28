using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds an anti-aliased wireframe overlay with built-in bloom to a camera
/// using screen-space depth/normal edge detection. Works on Metal.
/// Created and managed by TronMode — do not add manually.
/// </summary>
[RequireComponent(typeof(Camera))]
public class WireframeOverlay : MonoBehaviour
{
    [HideInInspector] public float wireIntensity = 3.0f;
    [HideInInspector] public float wireThreshold = 0.1f;
    [HideInInspector] public float wireAlpha = 0.5f;
    [HideInInspector] public float wireBloomIntensity = 1.5f;
    [HideInInspector] public float wireBloomSize = 2.0f;
    [HideInInspector] public int wireBloomPasses = 2;

    private Material edgeMaterial;
    private Camera cam;

    void OnEnable()
    {
        Shader edgeShader = Shader.Find("Hidden/TronWireOverlay");
        if (edgeShader == null)
        {
            Debug.LogError("[WireOverlay] Shader not found");
            enabled = false;
            return;
        }

        edgeMaterial = new Material(edgeShader);
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.DepthNormals;

        Debug.Log($"[WireOverlay] Enabled on {cam.name}");
    }

    void OnDisable()
    {
        if (edgeMaterial != null)
        {
            Destroy(edgeMaterial);
            edgeMaterial = null;
        }
        Debug.Log("[WireOverlay] Disabled");
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (edgeMaterial == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        edgeMaterial.SetFloat("_EdgeIntensity", wireIntensity);
        edgeMaterial.SetFloat("_EdgeThreshold", wireThreshold);
        edgeMaterial.SetFloat("_EdgeAlpha", wireAlpha);
        edgeMaterial.SetFloat("_BloomIntensity", wireBloomIntensity);
        edgeMaterial.SetFloat("_BlurSize", wireBloomSize);

        int width = source.width;
        int height = source.height;

        // Pass 0: Edge detection with AA — amplify scene colors at edges
        RenderTexture edged = RenderTexture.GetTemporary(width, height, 0, source.format);
        Graphics.Blit(source, edged, edgeMaterial, 0);

        // Pass 1: Extract bright pixels at half resolution
        int bloomW = width / 2;
        int bloomH = height / 2;
        RenderTexture bright = RenderTexture.GetTemporary(bloomW, bloomH, 0, source.format);
        Graphics.Blit(edged, bright, edgeMaterial, 1);

        // Iterative blur passes for wider bloom
        for (int i = 0; i < wireBloomPasses; i++)
        {
            // Pass 2: Horizontal blur
            RenderTexture blurH = RenderTexture.GetTemporary(bloomW, bloomH, 0, source.format);
            Graphics.Blit(bright, blurH, edgeMaterial, 2);
            RenderTexture.ReleaseTemporary(bright);

            // Pass 3: Vertical blur
            bright = RenderTexture.GetTemporary(bloomW, bloomH, 0, source.format);
            Graphics.Blit(blurH, bright, edgeMaterial, 3);
            RenderTexture.ReleaseTemporary(blurH);
        }

        // Pass 4: Composite — add bloom back onto edged scene
        edgeMaterial.SetTexture("_BloomTex", bright);
        Graphics.Blit(edged, destination, edgeMaterial, 4);

        RenderTexture.ReleaseTemporary(edged);
        RenderTexture.ReleaseTemporary(bright);
    }
}