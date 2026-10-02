using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// VillageScene의 합친 벽 Tilemap(WallVisuals)에 붙어서, 플레이어 위치에서 다른 벽에 가려진 벽 "부분"을 픽셀 단위로
/// 어둡게 덮는다. 벽 그림자 상자는 이어진 벽 전체를 한 그룹으로 묶어 Self Shadows를 끄기 때문에(그래야 벽이 타일
/// 경계로 갈라져 보이지 않는다), URP는 벽 위에 벽 그림자를 전혀 그리지 않는다 - 그대로 두면 꺾인 벽의 "뒤쪽" 줄도
/// 앞쪽과 똑같이 밝게 보인다. 그래서 벽끼리의 가림은 여기서 따로 계산한다.
///
/// 벽 칸 하나를 TexelsPerCell x TexelsPerCell 점으로 나눠, 점마다 플레이어에서 그 점까지의 선이
///   - 벽을 한 번 들어간 뒤 바닥으로 나왔다가 다시 벽으로 들어가면(= 앞의 다른 벽에 가림) 안 보이고,
///   - 마지막으로 벽에 들어간 지점에서 MaxPenetration 칸보다 깊으면(= 벽 끝면만 보고 있는데 벽 줄 전체가
///     밝아지지 않게) 안 보인다.
/// 이어진 벽 안을 지나는 것은 막지 않으므로 칸 경계에서 벽이 갈라지지 않고, 가려진 부분만 점 단위로 어두워진다.
/// 결과는 검은 반투명 텍스처 한 장(플레이어 주변만)에 담아 벽 바로 위에 덮는다.
/// </summary>
public class WallVisibilityMask : MonoBehaviour
{
    // 벽 칸 하나를 몇 x 몇 점으로 나눠 판정할지. 텍스처는 Bilinear라 경계는 부드럽게 이어진다.
    private const int TexelsPerCell = 8;
    // 빛이 벽에 들어간 지점에서 이 거리(칸)까지만 벽 윗면이 보인다고 본다. 두께 1칸 벽을 비스듬히 봐도 윗면
    // 전체가 보일 만큼은 넉넉하게, 벽 줄을 끝면에서 길게 들여다볼 때는 앞쪽 일부만 보이게 잡은 값이다.
    private const float MaxPenetration = 2f;
    // 시야 반경 밖 벽은 라이트가 닿지 않아 어차피 어두우므로, 반경에 여유만 조금 두고 그 안쪽만 판정한다.
    private const float RangePadding = 1.5f;

    private static readonly Color32 Hidden = new Color32(0, 0, 0, 255);

    private Transform viewer;
    private GameBalanceConfig config;
    private HashSet<Vector2Int> wallCells;
    private SpriteRenderer overlay;
    private Texture2D texture;
    private Color32[] pixels;
    private int sizeCells;

    public void Init(Transform viewerTransform, GameBalanceConfig balanceConfig, HashSet<Vector2Int> cells)
    {
        viewer = viewerTransform;
        config = balanceConfig;
        wallCells = cells;

        // 벽(TilemapRenderer)보다 위, 캐릭터/장애물(같은 sortingOrder+1)보다는 아래에 그려야 한다. 같은 order끼리는
        // 2D 렌더러의 Y축 정렬로 순서가 정해지므로, 기준점을 텍스처 맨 위(가장 높은 Y)에 둬서 그 order 안에서
        // 가장 먼저 그려지게 한다 - 벽 근처에 선 캐릭터는 덮개 위에 그려진다.
        GameObject overlayGO = new GameObject("WallOcclusionOverlay");
        overlayGO.transform.SetParent(transform, false);
        overlay = overlayGO.AddComponent<SpriteRenderer>();
        overlay.spriteSortPoint = SpriteSortPoint.Pivot;
        TilemapRenderer wallRenderer = GetComponent<TilemapRenderer>();
        if (wallRenderer != null)
        {
            overlay.sortingLayerID = wallRenderer.sortingLayerID;
            overlay.sortingOrder = wallRenderer.sortingOrder + 1;
        }
    }

    private void OnDestroy()
    {
        if (overlay != null && overlay.sprite != null) Destroy(overlay.sprite);
        if (texture != null) Destroy(texture);
    }

    private void LateUpdate()
    {
        if (overlay == null || viewer == null || wallCells == null) return;

        PlayerClass currentClass = GameManager.Instance != null ? GameManager.Instance.CurrentClass : PlayerClass.Scout;
        float focusedRadius = config != null ? config.GetClassFocusedVisionRadius(currentClass) : 13f;
        // 기본 원형 시야가 그림자를 무시하는 설정이면(바닥도 벽 너머까지 보인다) 그 안의 벽도 가리지 않는다.
        float alwaysVisibleRadius = config != null && config.ambientVisionIgnoresShadows
            ? config.GetClassAmbientVisionRadius(currentClass)
            : 0f;
        float range = Mathf.Max(focusedRadius, alwaysVisibleRadius) + RangePadding;

        EnsureTexture(Mathf.CeilToInt(range) * 2 + 2);

        Vector2 origin = viewer.position;
        Vector2Int baseCell = Vector2Int.FloorToInt(origin) - new Vector2Int(sizeCells / 2, sizeCells / 2);
        overlay.transform.position = new Vector3(baseCell.x, baseCell.y + sizeCells, 0f);

        System.Array.Clear(pixels, 0, pixels.Length);
        int width = sizeCells * TexelsPerCell;
        float alwaysVisibleSqr = alwaysVisibleRadius * alwaysVisibleRadius;
        float rangeSqr = range * range;

        for (int cy = 0; cy < sizeCells; cy++)
        {
            for (int cx = 0; cx < sizeCells; cx++)
            {
                Vector2Int cell = new Vector2Int(baseCell.x + cx, baseCell.y + cy);
                if (!wallCells.Contains(cell)) continue;
                if ((new Vector2(cell.x + 0.5f, cell.y + 0.5f) - origin).sqrMagnitude > rangeSqr) continue;

                for (int sy = 0; sy < TexelsPerCell; sy++)
                {
                    for (int sx = 0; sx < TexelsPerCell; sx++)
                    {
                        Vector2 point = new Vector2(cell.x + (sx + 0.5f) / TexelsPerCell, cell.y + (sy + 0.5f) / TexelsPerCell);
                        if ((point - origin).sqrMagnitude <= alwaysVisibleSqr) continue;
                        if (IsPointVisible(origin, cell, point)) continue;
                        pixels[(cy * TexelsPerCell + sy) * width + cx * TexelsPerCell + sx] = Hidden;
                    }
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    private void EnsureTexture(int cells)
    {
        if (texture != null && sizeCells == cells) return;

        if (overlay.sprite != null) Destroy(overlay.sprite);
        if (texture != null) Destroy(texture);

        sizeCells = cells;
        int size = sizeCells * TexelsPerCell;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "WallOcclusionMask"
        };
        pixels = new Color32[size * size];
        overlay.sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0f, 1f), TexelsPerCell);
    }

    // origin에서 point(targetCell 안의 점)까지 격자를 따라 걸으며(Amanatides-Woo DDA) 벽에 들어가고 나오는 것을 센다.
    private bool IsPointVisible(Vector2 origin, Vector2Int targetCell, Vector2 point)
    {
        Vector2 dir = point - origin;
        Vector2Int cell = Vector2Int.FloorToInt(origin);
        if (cell == targetCell) return true;

        int stepX = dir.x > 0 ? 1 : (dir.x < 0 ? -1 : 0);
        int stepY = dir.y > 0 ? 1 : (dir.y < 0 ? -1 : 0);
        float tDeltaX = stepX != 0 ? Mathf.Abs(1f / dir.x) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? Mathf.Abs(1f / dir.y) : float.PositiveInfinity;
        float tMaxX = stepX > 0 ? (cell.x + 1 - origin.x) * tDeltaX
            : stepX < 0 ? (origin.x - cell.x) * tDeltaX
            : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (cell.y + 1 - origin.y) * tDeltaY
            : stepY < 0 ? (origin.y - cell.y) * tDeltaY
            : float.PositiveInfinity;

        bool inWall = wallCells.Contains(cell);
        bool leftWall = false;
        float entryT = 0f;

        int maxSteps = Mathf.Abs(targetCell.x - cell.x) + Mathf.Abs(targetCell.y - cell.y) + 2;
        for (int i = 0; i < maxSteps; i++)
        {
            float t;
            if (tMaxX < tMaxY)
            {
                t = tMaxX;
                cell.x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                t = tMaxY;
                cell.y += stepY;
                tMaxY += tDeltaY;
            }

            bool isWall = wallCells.Contains(cell);
            if (isWall && !inWall)
            {
                if (leftWall) return false; // 앞의 다른 벽을 지나온 뒤 다시 벽으로 들어왔다
                entryT = t;
            }
            else if (!isWall && inWall)
            {
                leftWall = true;
            }
            inWall = isWall;

            if (cell == targetCell) break;
        }

        return (1f - entryT) * dir.magnitude <= MaxPenetration;
    }
}
