using UnityEngine;
using UnityEngine.UI;

public static class Extension_Texture2D
{
    public static void SetAlpha(this Image sprite, float alpha) => sprite.color = sprite.color.WithAlpha(alpha);
    
    public static Sprite Crop(this Sprite sprite, Vector4 paddingRatio)
    {
        if (sprite == null || sprite.texture == null)
        {
            Debug.LogError("Invalid sprite or texture.", sprite);
            return null;
        }

        Texture2D texture = sprite.texture.Crop(paddingRatio);
        Rect newRect = new Rect(Vector2.zero, new Vector2(texture.width, texture.height));
        Sprite cropped = Sprite.Create(texture, newRect, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
        return cropped;
    }
    public static Texture2D Crop(this Texture2D texture, Vector4 paddingRatio)
    {
        if (texture == null)
        {
            Debug.LogError("Invalid texture.");
            return null;
        }
        if (!texture.isReadable)
        {
            Debug.LogError("Texture is not readable. Please enable 'Read/Write Enabled' in import settings.", texture);
            return null;
        }

        int width = texture.width;
        int height = texture.height;

        // Convert ratios to pixels
        float left = paddingRatio.x * width;
        float top = paddingRatio.y * height;
        float right = paddingRatio.z * width;
        float bottom = paddingRatio.w * height;

        // Calculate new cropped rect
        int newX = Mathf.FloorToInt(left);
        int newY = Mathf.FloorToInt(bottom);
        int newWidth = Mathf.FloorToInt(width - (left + right));
        int newHeight = Mathf.FloorToInt(height - (top + bottom));

        // Clamp to valid texture bounds
        newX = Mathf.Clamp(newX, 0, width - 1);
        newY = Mathf.Clamp(newY, 0, height - 1);
        newWidth = Mathf.Clamp(newWidth, 1, width - newX);
        newHeight = Mathf.Clamp(newHeight, 1, height - newY);

        //Create the Texture
        Texture2D resultTex = new Texture2D(newWidth, newHeight);
        Color[] pixels = texture.GetPixels(newX, newY, newWidth, newHeight);
        resultTex.SetPixels(pixels);

        resultTex.wrapMode = TextureWrapMode.Clamp;
        resultTex.filterMode = texture.filterMode;
        resultTex.anisoLevel = texture.anisoLevel;
        resultTex.Apply();
        return resultTex;
    }

    public static Sprite[,] Split(this Sprite sprite, Vector2Int cellCount) => Split(sprite, cellCount, Vector2.zero);
    public static Sprite[,] Split(this Sprite sprite, Vector2Int cellCount, float spacingRatio) => Split(sprite, cellCount, Vector2.one * spacingRatio);
    public static Sprite[,] Split(this Sprite sprite, Vector2Int cellCount, Vector2 spacingRatio)
    {
        Sprite[,] result = new Sprite[cellCount.x, cellCount.y];
        if (sprite == null || sprite.texture == null)
        {
            Debug.LogError("Invalid sprite or texture.");
            return null;
        }
        if (!sprite.texture.isReadable)
        {
            Debug.LogError("Base Image is not readable. Please set the texture to be readable in the import settings.", sprite.texture);
            return null;
        }

        Texture2D[,] textures = sprite.texture.Split(cellCount, spacingRatio);
        for (int x = 0; x < cellCount.x; x++)
            for (int y = 0; y < cellCount.y; y++)
            {
                Rect rect = new Rect(0f, 0f, textures[x,y].width, textures[x, y].height);
                result[x, y] = Sprite.Create(textures[x, y], rect, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
            }

        return result;
    }
    public static Texture2D[,] Split(this Texture2D texture, Vector2Int cellCount, Vector2 spacingRatio)
    {
        Texture2D[,] result = new Texture2D[cellCount.x, cellCount.y];
        if (texture == null)
        {
            Debug.LogError("Invalid texture.");
            return null;
        }
        if (!texture.isReadable)
        {
            Debug.LogError("Base Image is not readable. Please set the texture to be readable in the import settings.", texture);
            return null;
        }

        cellCount = new Vector2Int(
            Mathf.Max(cellCount.x, 1),
            Mathf.Max(cellCount.y, 1)
            );
        Vector2Int spacingCount = cellCount - Vector2Int.one;

        Vector2 gapTotalRatio = spacingRatio;
        Vector2 gapRatio = new Vector2(
            spacingCount.x == 0 ? 0f : gapTotalRatio.x / spacingCount.x,
            spacingCount.y == 0 ? 0f : gapTotalRatio.y / spacingCount.y
            );

        Vector2 cellTotalRatio = Vector2.one - gapTotalRatio;
        Vector2 cellRatio = cellTotalRatio / cellCount;

        // Calculate the segment size
        int texWidth = texture.width;
        int texHeight = texture.height;
        Vector2Int cellSize = new Vector2Int(
            Mathf.FloorToInt(texWidth * cellRatio.x),
            Mathf.FloorToInt(texHeight * cellRatio.y)
            );
        Vector2Int gapSize = new Vector2Int(
            Mathf.FloorToInt(texWidth * gapRatio.x),
            Mathf.FloorToInt(texHeight * gapRatio.y)
            );

        //Create the Textures
        Texture2D resultTex;
        Color[] pixels;
        int posX;
        int posY;
        for (int x = 0; x < cellCount.x; x++)
            for (int y = 0; y < cellCount.y; y++)
            {
                posX = x * (cellSize.x + gapSize.x);
                posY = y * (cellSize.y + gapSize.y);

                resultTex = new Texture2D(cellSize.x, cellSize.y);
                pixels = texture.GetPixels(posX, posY, cellSize.x, cellSize.y);

                resultTex.SetPixels(pixels);
                resultTex.wrapMode = TextureWrapMode.Clamp;
                resultTex.filterMode = texture.filterMode;
                resultTex.anisoLevel = texture.anisoLevel;
                resultTex.Apply();

                result[x, y] = resultTex;
            }

        return result;
    }
}