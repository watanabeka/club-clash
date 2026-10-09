using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClubClash
{
    /// <summary>Measurements refer to the unmodified source atlas, in Unity pixel coordinates.</summary>
    public sealed class AtlasFrameInfo
    {
        public Rect SourceBounds;
        public Vector2 SourceFeet;
        public int CorePixels, ReclaimedPixels, ComponentCount;
        public int Frame;
        internal int MainBottom, MainTop;
    }

    /// <summary>Extracts complete connected poses before looking at grid boundaries.
    /// Opaque cores decide ownership; antialias fringes attach to the nearest core.
    /// Separate frame textures prevent a neighbour inside a borrowed rectangle leaking in.</summary>
    internal sealed class AtlasFrameSlicer
    {
        const byte CoreAlpha = 128;
        const int FringeDistance = 4;
        public readonly AtlasFrameInfo[] Frames = new AtlasFrameInfo[24];
        readonly int width, height, cellWidth, cellHeight;
        readonly Color32[] source;
        readonly int[] owners;
        readonly byte[] distance;

        sealed class Component
        {
            public int Owner, Count, MinX, MinY, MaxX, MaxY;
            public int[] Pixels;
        }

        public AtlasFrameSlicer(Color32[] pixels, int textureWidth, int textureHeight)
        {
            source = pixels; width = textureWidth; height = textureHeight;
            cellWidth = width / 6; cellHeight = height / 4;
            owners = new int[source.Length]; distance = new byte[source.Length];
            for (int i = 0; i < owners.Length; i++) owners[i] = -1;
            for (int i = 0; i < Frames.Length; i++) Frames[i] = new AtlasFrameInfo { Frame = i };
            var seen = new bool[source.Length];
            var components = new List<Component>();
            var queue = new int[source.Length];
            int[] largest = new int[24];
            for (int start = 0; start < source.Length; start++)
            {
                if (seen[start] || source[start].a <= CoreAlpha) continue;
                int head = 0, tail = 1; queue[0] = start; seen[start] = true;
                int[] votes = new int[24];
                int minX = width, minY = height, maxX = 0, maxY = 0;
                while (head < tail)
                {
                    int p = queue[head++], x = p % width, y = p / width;
                    votes[Nominal(x, y)]++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int next = ny * width + nx;
                        if (seen[next] || source[next].a <= CoreAlpha) continue;
                        seen[next] = true; queue[tail++] = next;
                    }
                }
                if (tail < 4) continue; // Subpixel debris is never part of a pose.
                int owner = 0;
                for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[owner]) owner = i;
                int[] members = new int[tail]; Array.Copy(queue, members, tail);
                var component = new Component { Owner = owner, Count = tail, Pixels = members,
                    MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
                components.Add(component);
                AtlasFrameInfo frame = Frames[owner];
                frame.CorePixels += tail; frame.ReclaimedPixels += tail - votes[owner]; frame.ComponentCount++;
                if (tail > largest[owner])
                { largest[owner] = tail; frame.MainBottom = minY; frame.MainTop = maxY; }
                foreach (int p in members) owners[p] = owner;
            }
            // Breadth-first attachment resolves halo contact between two independent
            // bodies without allowing a low-alpha bridge to merge their entire poses.
            int count = 0;
            for (int i = 0; i < owners.Length; i++)
                if (owners[i] >= 0) { queue[count++] = i; distance[i] = 0; }
            int cursor = 0;
            while (cursor < count)
            {
                int p = queue[cursor++];
                if (distance[p] >= FringeDistance) continue;
                int x = p % width, y = p / width;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (owners[next] >= 0 || source[next].a <= 8) continue;
                    owners[next] = owners[p]; distance[next] = (byte)(distance[p] + 1); queue[count++] = next;
                }
            }
            int[] left = new int[24], bottom = new int[24], right = new int[24], top = new int[24];
            for (int i = 0; i < 24; i++) { left[i] = width; bottom[i] = height; }
            for (int p = 0; p < owners.Length; p++)
            {
                int id = owners[p]; if (id < 0) continue;
                int x = p % width, y = p / width;
                left[id] = Math.Min(left[id], x); bottom[id] = Math.Min(bottom[id], y);
                right[id] = Math.Max(right[id], x); top[id] = Math.Max(top[id], y);
            }
            for (int i = 0; i < 24; i++)
            {
                AtlasFrameInfo frame = Frames[i];
                if (frame.CorePixels == 0)
                {
                    frame.SourceBounds = new Rect(i % 6 * cellWidth, height - (i / 6 + 1) * cellHeight, cellWidth, cellHeight);
                    frame.SourceFeet = new Vector2((i % 6 + .5f) * cellWidth, frame.SourceBounds.y + cellHeight / 16f);
                    continue;
                }
                // Two transparent pixels protect the full antialias outline in a tight rect.
                int x0 = Math.Max(0, left[i] - 2), y0 = Math.Max(0, bottom[i] - 2);
                int x1 = Math.Min(width, right[i] + 3), y1 = Math.Min(height, top[i] + 3);
                frame.SourceBounds = new Rect(x0, y0, x1 - x0, y1 - y0);
                frame.SourceFeet = new Vector2((i % 6 + .5f) * cellWidth, frame.MainBottom);
            }
        }

        int Nominal(int x, int y)
        { return Math.Min(3, (height - 1 - y) / cellHeight) * 6 + Math.Min(5, x / cellWidth); }

        public Texture2D Texture(int frame, Action<Color32[], Rect> refine = null)
        {
            Rect r = Frames[frame].SourceBounds;
            int x0 = (int)r.x, y0 = (int)r.y, w = (int)r.width, h = (int)r.height;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int index = (y0 + y) * width + x0 + x;
                if (owners[index] == frame) pixels[y * w + x] = source[index];
            }
            if (refine != null) refine(pixels, r);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.name = "Extracted pose " + frame;
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        public float IdleBodyHeight()
        {
            // The central head band excludes the high bat/bow/brush. Feet come from
            // the dominant connected body, never a detached ball or neighbouring shoe.
            int firstRowBottom = height - cellHeight;
            for (int y = height - 1; y >= firstRowBottom; y--)
                for (int x = cellWidth * 40 / 100; x < cellWidth * 60 / 100; x++)
                    if (owners[y * width + x] == 0 && source[y * width + x].a > CoreAlpha)
                        return Math.Max(40, y - Frames[0].MainBottom);
            return Math.Max(40, Frames[0].MainTop - Frames[0].MainBottom);
        }
    }
}
