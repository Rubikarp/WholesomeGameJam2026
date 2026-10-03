#if UNITY_EDITOR
using System;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JeffGrawAssets.FlexibleUI
{
public static class UIGraphicToTexture
{
    public static void BakeTexture(List<UIVertex> vertices, List<int> quadVertexCounts, Material material, Texture2D mainTexture, int width, int height, int supersample = 1, bool addPadding = false, string path = null)
    {
        var finalWidth = width;
        var finalHeight = height;
        var renderWidth = width;
        var renderHeight = height;
        var borderOffset = 0;
        
        if (addPadding && width > 2 && height > 2)
        {
            renderWidth -= 2;
            renderHeight -= 2;
            borderOffset = 1;
        }
        else
        {
            addPadding = false;
        }
        
        var planeNormal = CalculatePlaneNormal(vertices);
        CreatePlaneCoordinateSystem(planeNormal, out var planeRight, out var planeUp);

        var projectedVertices = new List<Vector2>();
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var origin = vertices[0].position;

        foreach (var vertex in vertices)
        {
            var fromOrigin = vertex.position - origin;
            var projected = new Vector2(Vector3.Dot(fromOrigin, planeRight), Vector3.Dot(fromOrigin, planeUp));
            projectedVertices.Add(projected);
            min = Vector2.Min(min, projected);
            max = Vector2.Max(max, projected);
        }

        var size = max - min;
        if (size.x < 0.0001f || size.y < 0.0001f)
            return;

        var createdTempTexture = false;
        if (mainTexture == null)
        {
            mainTexture = new Texture2D(1, 1);
            mainTexture.SetPixel(0, 0, Color.white);
            mainTexture.Apply();
            createdTempTexture = true;
        }

        var tempMaterial = new Material(material);
        tempMaterial.SetTexture(Shader.PropertyToID("_MainTex"), mainTexture);
        tempMaterial.SetInt(Shader.PropertyToID("_UIVertexColorAlwaysGammaSpace"), Convert.ToInt32(true));
        tempMaterial.SetInt("_SrcBlend", (int)BlendMode.One);

        var quadTextures = new List<Texture2D>();
        var vertexOffset = 0;
        
        if (quadVertexCounts == null || quadVertexCounts.Count == 0)
            quadVertexCounts = new List<int> { vertices.Count };

        foreach (var vertexCount in quadVertexCounts)
        {
            var quadMesh = CreateQuadMesh(vertices, projectedVertices, vertexOffset, vertexCount, min, size);

            var quadTexture = supersample > 1
                ? RenderMeshToTextureWithSupersampling(quadMesh, tempMaterial, renderWidth, renderHeight, supersample)
                : RenderMeshToTexture(quadMesh, tempMaterial, renderWidth, renderHeight);

            quadTextures.Add(quadTexture);

            Object.DestroyImmediate(quadMesh);
            vertexOffset += vertexCount;
        }

        var result = quadTextures.Count == 1 ? quadTextures[0] : BlendQuadTextures(quadTextures, renderWidth, renderHeight);

        if (addPadding)
        {
            var borderedTexture = new Texture2D(finalWidth, finalHeight, TextureFormat.ARGB32, false);
            var clearPixels = new Color[finalWidth * finalHeight];
            for (int i = 0; i < clearPixels.Length; i++) clearPixels[i] = Color.clear;
            borderedTexture.SetPixels(clearPixels);
            borderedTexture.SetPixels(borderOffset, borderOffset, renderWidth, renderHeight, result.GetPixels());
            borderedTexture.Apply();
            Object.DestroyImmediate(result);
            result = borderedTexture;
        }

        Object.DestroyImmediate(tempMaterial);
        if (createdTempTexture)
            Object.DestroyImmediate(mainTexture);

        if (!string.IsNullOrEmpty(path))
            SaveTextureToDisk(result, path);
    }
    
    private static Mesh CreateQuadMesh(List<UIVertex> totalVertices, List<Vector2> projectedVertices, int offset, int count, Vector2 min, Vector2 size)
    {
        var mesh = new Mesh();
        var meshVertices = new Vector3[count];
        var normals = new Vector3[count];
        var tangents = new Vector4[count];
        var colors = new Color32[count];
        var uv0List = new List<Vector4>(count);
        var uv1List = new List<Vector4>(count);
        var uv2List = new List<Vector4>(count);
        var uv3List = new List<Vector4>(count);

        for (int i = 0; i < count; i++)
        {
            var totalIndex = offset + i;
            var normalized = (projectedVertices[totalIndex] - min) / size;
            meshVertices[i] = new Vector3(normalized.x - 0.5f, normalized.y - 0.5f, 0);
            normals[i] = totalVertices[totalIndex].normal;
            tangents[i] = totalVertices[totalIndex].tangent;
            colors[i] = totalVertices[totalIndex].color;
            uv0List.Add(totalVertices[totalIndex].uv0);
            uv1List.Add(totalVertices[totalIndex].uv1);
            uv2List.Add(totalVertices[totalIndex].uv2);
            uv3List.Add(totalVertices[totalIndex].uv3);
        }

        mesh.vertices = meshVertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.colors32 = colors;

        mesh.SetUVs(0, uv0List);
        mesh.SetUVs(1, uv1List);
        mesh.SetUVs(2, uv2List);
        mesh.SetUVs(3, uv3List);

        var indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = i;

        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        return mesh;
    }
    
    private static Texture2D BlendQuadTextures(List<Texture2D> quadTextures, int width, int height)
    {
        var pixelCount = width * height;
        var basePixelsLin = new Color[pixelCount];

        // Load first texture and convert to linear
        var firstPixelsGamma = quadTextures[0].GetPixels();
        for (int j = 0; j < pixelCount; j++)
            basePixelsLin[j] = firstPixelsGamma[j].linear;
        
        Object.DestroyImmediate(quadTextures[0]);

        // Blend subsequent textures on top
        for (int i = 1; i < quadTextures.Count; i++)
        {
            var overlayPixelsGamma = quadTextures[i].GetPixels();

            for (int j = 0; j < pixelCount; j++)
            {
                var baseLin = basePixelsLin[j];
                var overlayLin = overlayPixelsGamma[j].linear;

                // Standard "Source Over" blending formula, performed with premultiplied alpha
                var outA = overlayLin.a + baseLin.a * (1.0f - overlayLin.a);
                var outR = overlayLin.r * overlayLin.a + baseLin.r * baseLin.a * (1f - overlayLin.a);
                var outG = overlayLin.g * overlayLin.a + baseLin.g * baseLin.a * (1f - overlayLin.a);
                var outB = overlayLin.b * overlayLin.a + baseLin.b * baseLin.a * (1f - overlayLin.a);

                // Un-premultiply
                if (outA > 1e-6f)
                {
                    baseLin.r = outR / outA;
                    baseLin.g = outG / outA;
                    baseLin.b = outB / outA;
                }
                else
                {
                    baseLin.r = 0;
                    baseLin.g = 0;
                    baseLin.b = 0;
                }
                baseLin.a = outA;

                basePixelsLin[j] = baseLin;
            }
            
            Object.DestroyImmediate(quadTextures[i]);
        }

        // Convert final linear pixels back to gamma
        var finalPixelsGamma = new Color[pixelCount];
        for (int j = 0; j < pixelCount; j++)
            finalPixelsGamma[j] = basePixelsLin[j].gamma;

        var finalTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        finalTexture.SetPixels(finalPixelsGamma);
        finalTexture.Apply();

        return finalTexture;
    }

    private static Vector3 CalculatePlaneNormal(List<UIVertex> vertices)
    {
        var v0 = vertices[0].position;
        var v1 = vertices[1].position;
        var v2 = vertices[2].position;
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var cross = Vector3.Cross(edge1, edge2);

        if (cross.magnitude >= 0.0001f || vertices.Count <= 3)
            return cross.normalized;

        for (int i = 3; i < vertices.Count; i++)
        {
            edge2 = vertices[i].position - v0;
            cross = Vector3.Cross(edge1, edge2);
            if (cross.magnitude >= 0.0001f)
                break;
        }

        return cross.normalized;
    }

    private static void CreatePlaneCoordinateSystem(Vector3 normal, out Vector3 right, out Vector3 up)
    {
        var arbitrary = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward;
        right = Vector3.Cross(normal, arbitrary).normalized;
        up = Vector3.Cross(normal, right).normalized;
    }
    
    private static Texture2D RenderMeshToTextureWithSupersampling(Mesh mesh, Material mat, int width, int height, int supersampleFactor)
    {
        var supersampleWidth = width * supersampleFactor;
        var supersampleHeight = height * supersampleFactor;
        var rt = RenderTexture.GetTemporary(supersampleWidth, supersampleHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previousRT = RenderTexture.active;

        var cmd = new CommandBuffer();
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(true, true, Color.clear);
        var proj = Matrix4x4.Ortho(-0.5f, 0.5f, 0.5f, -0.5f, -1, 1);
        var view = Matrix4x4.identity;
        var model = Matrix4x4.identity;
        cmd.SetViewProjectionMatrices(view, proj);
        cmd.DrawMesh(mesh, model, mat, 0, 0);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Dispose();

        RenderTexture.active = rt;
        var supersampledTexture = new Texture2D(supersampleWidth, supersampleHeight, TextureFormat.ARGB32, false);
        supersampledTexture.ReadPixels(new Rect(0, 0, supersampleWidth, supersampleHeight), 0, 0);
        supersampledTexture.Apply();
        var finalTexture = DownscaleTexture(supersampledTexture, width, height, supersampleFactor);
        Object.DestroyImmediate(supersampledTexture);
        RenderTexture.active = previousRT;
        RenderTexture.ReleaseTemporary(rt);
        return finalTexture;
    }

    private static Texture2D DownscaleTexture(Texture2D source, int targetWidth, int targetHeight, int supersampleFactor)
    {
        var result = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var numPixelsToAverage = supersampleFactor * supersampleFactor;
                var invNumPixels = 1.0f / numPixelsToAverage;
                var startSrcX = x * supersampleFactor;
                var startSrcY = y * supersampleFactor;
                var sumLinPre = Color.clear;
                for (int sy = 0; sy < supersampleFactor; sy++)
                {
                    for (int sx = 0; sx < supersampleFactor; sx++)
                    {
                        var pGamma = source.GetPixel(startSrcX + sx, startSrcY + sy);
                        var pLin = pGamma.linear;
                        pLin.r *= pLin.a;
                        pLin.g *= pLin.a;
                        pLin.b *= pLin.a;
                        sumLinPre += pLin;
                    }
                }

                sumLinPre *= invNumPixels;
                var finalAlpha = sumLinPre.a;
                var finalLin = new Color(0, 0, 0, finalAlpha);
                if (finalAlpha > 1e-6f)
                {
                    finalLin.r = sumLinPre.r / finalAlpha;
                    finalLin.g = sumLinPre.g / finalAlpha;
                    finalLin.b = sumLinPre.b / finalAlpha;
                }

                var finalGammaPixel = finalLin.gamma;
                result.SetPixel(x, y, finalGammaPixel);
            }
        }

        result.Apply();
        return result;
    }

    private static Texture2D RenderMeshToTexture(Mesh mesh, Material mat, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previousRT = RenderTexture.active;
        var cmd = new CommandBuffer();
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(true, true, Color.clear);

        var proj = Matrix4x4.Ortho(-0.5f, 0.5f, 0.5f, -0.5f, -1, 1);
        var view = Matrix4x4.identity;
        var model = Matrix4x4.identity;
        cmd.SetViewProjectionMatrices(view, proj);
        cmd.DrawMesh(mesh, model, mat, 0, 0);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Dispose();
        RenderTexture.active = rt;

        var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        
        RenderTexture.active = previousRT;
        RenderTexture.ReleaseTemporary(rt);

        return texture;
    }

    private static void SaveTextureToDisk(Texture2D texture, string path)
    {
        var fullPath = Path.Combine(Application.dataPath, path);
        var preexisting = File.Exists(fullPath);
        var extension = Path.GetExtension(fullPath).ToLower();

        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
            fullPath += extension;
        }

        byte[] bytes;
        switch (extension)
        {
            case ".png":
                bytes = texture.EncodeToPNG();
                break;
            case ".jpg":
            case ".jpeg":
                bytes = texture.EncodeToJPG();
                break;
            case ".exr":
                bytes = texture.EncodeToEXR();
                break;
            case ".tga":
                bytes = texture.EncodeToTGA();
                break;
            default:
                Debug.LogWarning($"Unsupported format '{extension}', defaulting to PNG");
                bytes = texture.EncodeToPNG();
                if (!fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    fullPath += ".png";
                break;
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(fullPath, bytes);
            Debug.Log($"Saved texture to {fullPath}");
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save texture to {fullPath}: {e.Message}");
        }

        if (preexisting)
            return;

        var assetPath = Path.Combine("Assets", path);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"Save texture to {fullPath}, but could not find a valid resulting asset");
            return;
        }

        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;

        importer.SaveAndReimport();
    }
}
}
#endif