using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClubClash
{
    /// <summary>
    /// Native 2D presentation. The simulation stores Y as height above the floor;
    /// Unity sprites are feet anchored at (X, 150 + Y). The UI can use 1280 x 720.
    /// </summary>
    public sealed class BattleRenderer : MonoBehaviour
    {
        const float Floor = 150;
        const int FrameCount = 24;
        static readonly string[] ClubIds = { "home", "kendo", "soccer", "baseball", "volleyball", "tennis", "golf", "boxing", "archery", "music", "art", "science", "shogi", "handball", "swimming", "basketball", "badminton", "judo", "calligraphy" };

        readonly Dictionary<string, Sprite[]> atlases = new Dictionary<string, Sprite[]>();
        readonly Dictionary<string, float> bodyScales = new Dictionary<string, float>();
        readonly Dictionary<string, float> atlasCellHeights = new Dictionary<string, float>();
        readonly Dictionary<string, AtlasFrameInfo[]> frameGeometry = new Dictionary<string, AtlasFrameInfo[]>();
        readonly Dictionary<string, BallArtProfile> ballArt = new Dictionary<string, BallArtProfile>();
        readonly Dictionary<string, Sprite> projectileSprites = new Dictionary<string, Sprite>();
        readonly Dictionary<StageId, Sprite> stageSprites = new Dictionary<StageId, Sprite>();
        readonly Dictionary<int, Sprite> numberSprites = new Dictionary<int, Sprite>();
        readonly List<Texture2D> generatedTextures = new List<Texture2D>();
        readonly List<Sprite> generatedSprites = new List<Sprite>();
        readonly List<ProjectileVisual> projectiles = new List<ProjectileVisual>();
        readonly List<Spark> sparks = new List<Spark>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly List<LineRenderer> debugLines = new List<LineRenderer>();
        readonly FighterVisual[] fighters = new FighterVisual[2];
        readonly Color[] playerColors = { new Color(.27f, .86f, 1), new Color(1, .55f, .27f) };
        readonly Vector3[] lineBuffer = new Vector3[5];
        GameObject worldRoot;
        Transform fxRoot;
        SpriteRenderer stage;
        SpriteRenderer flashOverlay;
        StageId renderedStage = StageId.Ground;
        Camera sceneCamera;
        Battle battle;
        BattleSound sound;
        Material lineMaterial;
        bool ownsMaterial;
        Sprite shadowSprite, ringSprite, slashSprite, whitePixel, sparkSprite, starSprite;
        Sprite impactSprite, smallRingSprite, dustSprite, dropletSprite, bubbleSprite, shardSprite, streakSprite;
        bool initialized, debug, visible = true;
        float clock, shake, cameraX = 640, zoom = 1, flash;
        long lastEventId;

        public Camera SceneCamera { get { Ensure(); return sceneCamera; } }

        sealed class FighterVisual
        {
            public Transform Root;
            public SpriteRenderer Body, Shadow, Guard, Trail, HomeRing, HeldBall;
            public SpriteRenderer[] Stars, PoisonBubbles;
            public string State;
            public float StateAge, TrailClock, DustClock, PoisonClock;
            public bool WasGrounded;
        }

        sealed class ProjectileVisual
        {
            public SpriteRenderer Body, Trail;
            public SpriteRenderer[] Tail;
            public Transform Root;
            public long Id = -1;
            public Vector2[] History;
            public int Samples;
        }

        sealed class Spark
        {
            public SpriteRenderer Renderer;
            public float Age, Duration, Vx, Vy, Rotation, Gravity, Grow, Spin;
            public Vector2 Position;
            public Color Color;
            public Vector2 Size;
            public bool Active;
        }

        sealed class Ghost
        {
            public SpriteRenderer Renderer;
            public float Age, Duration;
            public Color Color;
            public bool Active;
        }

        public void Bind(Battle value)
        {
            Ensure();
            battle = value;
            sound.Bind(value);
            lastEventId = 0;
            shake = 0;
            flash = 0;
            zoom = 1;
            cameraX = 640;
            foreach (Spark spark in sparks) { spark.Active = false; spark.Renderer.enabled = false; }
            foreach (Ghost ghost in ghosts) { ghost.Active = false; ghost.Renderer.enabled = false; }
            foreach (ProjectileVisual p in projectiles) p.Root.gameObject.SetActive(false);
            for (int i = 0; i < fighters.Length; i++)
            {
                fighters[i].State = null;
                fighters[i].StateAge = 0;
                fighters[i].TrailClock = fighters[i].DustClock = fighters[i].PoisonClock = 0;
                fighters[i].WasGrounded = true;
                fighters[i].HeldBall.enabled = false;
                fighters[i].Root.gameObject.SetActive(value != null);
                fighters[i].Shadow.enabled = value != null;
            }
            Render(0);
        }

        public void SetMuted(bool value) { Ensure(); sound.SetMuted(value); }
        public void SetMusicEnabled(bool value) { Ensure(); sound.SetMusicEnabled(value); }
        public void SetEffectsEnabled(bool value) { Ensure(); sound.SetEffectsEnabled(value); }
        public void PlaySelectionTick(int tick, bool final) { Ensure(); sound.PlaySelectionTick(tick, final); }
        public bool MusicEnabled { get { Ensure(); return sound.MusicEnabled; } }
        public bool EffectsEnabled { get { Ensure(); return sound.EffectsEnabled; } }
        public BattleSound AudioDiagnostics { get { Ensure(); return sound; } }
        public void SetDebug(bool value) { debug = value; }
        public void SetVisible(bool value)
        {
            Ensure();
            visible = value;
            worldRoot.SetActive(value);
            sound.UpdateMusicState(battle, value);
        }

        /// <summary>Idle sprite for selection panels; returned sprites are cached.</summary>
        public Sprite Portrait(string id)
        {
            Ensure();
            Sprite[] frames = Atlas(id);
            return frames != null && frames.Length > 0 ? frames[0] : null;
        }

        /// <summary>Subtle two-pose idle for menu previews; the training teacher stays still.</summary>
        public Sprite IdlePortrait(string id, float elapsed)
        {
            Ensure();
            Sprite[] frames = Atlas(id);
            if (frames == null || frames.Length == 0) return null;
            if (id == "teacher" || frames.Length == 1) return frames[0];
            int frame = Mathf.FloorToInt(Mathf.Max(0, elapsed) / .55f) % 2;
            return frames[frame];
        }

        /// <summary>Idle body scale relative to baseball, excluding weapon extent.</summary>
        public float PortraitScale(string id)
        {
            Ensure(); Atlas(id); Atlas("baseball");
            float own = bodyScales.TryGetValue(id, out float value) ? value : 1;
            float reference = bodyScales.TryGetValue("baseball", out float baseValue) ? baseValue : 1;
            return own / Mathf.Max(.001f, reference);
        }

        /// <summary>GUI draw rectangle with equal body size and feet on slot.yMax.
        /// Crop bounds may include wide weapons; they never affect body normalization.</summary>
        public Rect PortraitPlacement(string id, Sprite sprite, Rect slot)
        {
            if (sprite == null) return slot;
            Atlas("baseball");
            float referenceHeight = atlasCellHeights.TryGetValue("baseball", out float referenceCell) ? referenceCell : 256;
            float scale = slot.height / referenceHeight * PortraitScale(id);
            Rect source = sprite.rect;
            return new Rect(slot.center.x - sprite.pivot.x * scale,
                slot.yMax - (source.height - sprite.pivot.y) * scale,
                source.width * scale, source.height * scale);
        }

        public AtlasFrameInfo FrameGeometry(string id, int frame)
        {
            Ensure(); Atlas(id);
            return frameGeometry.TryGetValue(id, out AtlasFrameInfo[] frames) && frames.Length > 0
                ? frames[Mathf.Clamp(frame, 0, frames.Length - 1)] : null;
        }

        public float BallDiameter(string id)
        { Ensure(); Atlas(id); return ballArt.TryGetValue(id, out BallArtProfile ball) ? ball.WorldDiameter : 0; }

        public Sprite BallSprite(string id)
        { Ensure(); Atlas(id); return ballArt.TryGetValue(id, out BallArtProfile ball) ? ball.Sprite : null; }

        void Ensure()
        {
            if (initialized) return;
            initialized = true;
            worldRoot = new GameObject("2D Battle World");
            worldRoot.transform.SetParent(transform, false);
            fxRoot = new GameObject("Effects").transform;
            fxRoot.SetParent(worldRoot.transform, false);
            // A Resources material gives the native player a build-time shader
            // reference, even though all SpriteRenderers are created at runtime.
            lineMaterial = Resources.Load<Material>("SpriteMaterial");
            if (lineMaterial == null)
            {
                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader != null) { lineMaterial = new Material(spriteShader); ownsMaterial = true; }
                else Debug.LogError("Missing SpriteMaterial resource and Sprites/Default shader.");
            }
            sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                var cameraObject = new GameObject("Battle Camera");
                cameraObject.tag = "MainCamera";
                sceneCamera = cameraObject.AddComponent<Camera>();
            }
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = 360;
            sceneCamera.transform.position = new Vector3(640, 360, -10);
            sceneCamera.transform.rotation = Quaternion.identity;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(.035f, .065f, .12f);
            sceneCamera.nearClipPlane = .1f;
            sceneCamera.farClipPlane = 100;
            if (FindAnyObjectByType<AudioListener>() == null) sceneCamera.gameObject.AddComponent<AudioListener>();
            sound = gameObject.AddComponent<BattleSound>();
            CreateUtilitySprites();
            for (int damage = 1; damage <= 60; damage++) NumberSprite(damage);
            CreateStage();
            for (int i = 0; i < fighters.Length; i++) fighters[i] = CreateFighter(i);
            foreach (string id in ClubIds) Atlas(id);
            Atlas("teacher");
            // The pool is fixed after warmup; ordinary battles do not allocate FX.
            flashOverlay = CreateSprite("Brief Impact Flash", fxRoot, whitePixel, 35);
            flashOverlay.enabled = false;
            for (int i = 0; i < 180; i++)
            {
                SpriteRenderer r = CreateSprite("Spark " + i, fxRoot, sparkSprite, 25);
                r.enabled = false;
                sparks.Add(new Spark { Renderer = r });
            }
            for (int i = 0; i < 24; i++)
            {
                SpriteRenderer r = CreateSprite("Movement Afterimage", fxRoot, null, 5);
                r.enabled = false;
                ghosts.Add(new Ghost { Renderer = r });
            }
            foreach (string kind in new[] { "soccer", "baseball", "volleyball", "tennis", "golf", "arrow", "note", "paint", "flask", "tile", "handball", "water", "basketball", "shuttle", "ink" }) ProjectileSprite(kind);
        }

        void CreateStage()
        {
            stage = CreateSprite("School Ground", worldRoot.transform, null, -100);
            stage.transform.position = new Vector3(640, 360, 0);
            SetStage(StageId.Ground);
        }

        public void SetStage(StageId id)
        {
            renderedStage = id;
            if (stage == null) return;
            if (!stageSprites.TryGetValue(id, out Sprite sprite))
            {
                string path = id == StageId.Classroom ? "Art/stage_classroom" : id == StageId.Gym ? "Art/stage_gym" : "Art/stage";
                Texture2D texture = Resources.Load<Texture2D>(path);
                if (texture == null)
                {
                    Debug.LogError("Missing stage PNG in Resources: " + path);
                    return;
                }
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
                generatedSprites.Add(sprite);
                stageSprites[id] = sprite;
            }
            stage.sprite = sprite;
            stage.transform.localScale = new Vector3(1280f / sprite.rect.width, 720f / sprite.rect.height, 1);
        }

        FighterVisual CreateFighter(int index)
        {
            var root = new GameObject("Fighter " + (index + 1)).transform;
            root.SetParent(worldRoot.transform, false);
            SpriteRenderer body = CreateSprite("Animated Sprite", root, null, 10 + index);
            SpriteRenderer shadow = CreateSprite("Ground Shadow", worldRoot.transform, shadowSprite, 1);
            shadow.color = new Color(.04f, .065f, .09f, .48f);
            SpriteRenderer guard = CreateSprite("Guard", root, ringSprite, 16);
            guard.transform.localPosition = new Vector3(0, 83, 0);
            guard.transform.localScale = new Vector3(.64f, .79f, 1);
            guard.enabled = false;
            SpriteRenderer trail = CreateSprite("Attack Arc", root, slashSprite, 17);
            trail.enabled = false;
            SpriteRenderer homeRing = CreateSprite("Home Ground Ring", root, smallRingSprite, 2);
            homeRing.transform.localPosition = new Vector3(0, 1, 0);
            homeRing.transform.localScale = new Vector3(1.4f, .25f, 1);
            homeRing.color = new Color(1, .85f, .38f, .2f);
            homeRing.enabled = false;
            var poisonBubbles = new SpriteRenderer[5];
            for (int i = 0; i < poisonBubbles.Length; i++)
            {
                poisonBubbles[i] = CreateSprite("Poison Aura", root, bubbleSprite, i % 2 == 0 ? 7 : 18);
                poisonBubbles[i].enabled = false;
            }
            var stars = new SpriteRenderer[3];
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i] = CreateSprite("Guard Break Star", root, starSprite, 21);
                stars[i].color = new Color(1, .89f, .28f);
                stars[i].enabled = false;
            }
            SpriteRenderer heldBall = CreateSprite("Same Ball Before Release", root, null, body.sortingOrder - 1);
            heldBall.enabled = false;
            return new FighterVisual { Root = root, Body = body, Shadow = shadow, Guard = guard, Trail = trail, Stars = stars, HeldBall = heldBall,
                PoisonBubbles = poisonBubbles, HomeRing = homeRing };
        }

        Sprite[] Atlas(string id)
        {
            if (atlases.TryGetValue(id, out Sprite[] result)) return result;
            Texture2D texture = Resources.Load<Texture2D>("Art/" + id);
            if (texture == null) { Debug.LogError("Missing fighter atlas Art/" + id); atlases[id] = null; return null; }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (id == "teacher") return TeacherSprite(texture);
            int cellWidth = texture.width / 6, cellHeight = texture.height / 4;
            atlasCellHeights[id] = cellHeight;
            Color32[] pixels = texture.isReadable ? texture.GetPixels32() : null;
            AtlasFrameSlicer slicer = pixels != null
                ? new AtlasFrameSlicer(pixels, texture.width, texture.height) : null;
            bodyScales[id] = BodyArtProfile.Scale(id, slicer == null ? cellHeight : slicer.IdleBodyHeight());
            if (slicer != null) frameGeometry[id] = slicer.Frames;
            BallArtProfile ball = pixels == null ? null : BallArtProfile.For(id);
            if (ball != null && slicer != null)
            {
                int w = (int)ball.Canonical.width, h = (int)ball.Canonical.height;
                var ballTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                ballTexture.name = id + " original ball"; ballTexture.filterMode = FilterMode.Point; ballTexture.wrapMode = TextureWrapMode.Clamp;
                ballTexture.SetPixels32(ball.CanonicalPixels(pixels, texture.width, texture.height)); ballTexture.Apply(false, true);
                generatedTextures.Add(ballTexture);
                ball.Sprite = Sprite.Create(ballTexture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
                ball.Sprite.name = id + " same ball held and released"; generatedSprites.Add(ball.Sprite);
                ball.WorldDiameter = ball.Diameter * bodyScales[id];
                AtlasFrameInfo windup = slicer.Frames[16];
                ball.WindupPosition = new Vector2(ball.WindupCenter.x - windup.SourceFeet.x,
                    texture.height - ball.WindupCenter.y - windup.SourceFeet.y) * bodyScales[id];
                ballArt[id] = ball; projectileSprites[ball.Kind] = ball.Sprite;
            }
            result = new Sprite[FrameCount];
            for (int i = 0; i < result.Length; i++)
            {
                if (slicer == null)
                {
                    Rect fallback = new Rect(i % 6 * cellWidth, texture.height - (i / 6 + 1) * cellHeight, cellWidth, cellHeight);
                    result[i] = Sprite.Create(texture, fallback, new Vector2(.5f, 1f / 16), 1, 0, SpriteMeshType.FullRect);
                }
                else
                {
                    AtlasFrameInfo geometry = slicer.Frames[i];
                    int frame = i;
                    Texture2D extracted = slicer.Texture(i, ball == null ? (Action<Color32[], Rect>)null :
                        (copy, sourceBounds) => ball.RemovePaintedBall(copy, sourceBounds, texture.height, frame));
                    generatedTextures.Add(extracted);
                    Vector2 localFeet = geometry.SourceFeet - geometry.SourceBounds.position;
                    Vector2 pivot = new Vector2(localFeet.x / extracted.width, localFeet.y / extracted.height);
                    result[i] = Sprite.Create(extracted, new Rect(0, 0, extracted.width, extracted.height), pivot, 1, 0, SpriteMeshType.FullRect);
                }
                result[i].name = id + " frame " + i;
                generatedSprites.Add(result[i]);
            }
            atlases[id] = result;
            return result;
        }

        Sprite[] TeacherSprite(Texture2D texture)
        {
            // This resource is one full-body standing sprite, not a 6x4 atlas.
            float footY = texture.height / 16f;
            float headY = texture.height * .90f;
            if (texture.isReadable)
            {
                Color32[] pixels = texture.GetPixels32();
                footY = CellBottom(pixels, texture.width, 0, 0, texture.width, texture.height);
                for (int y = texture.height - 1; y >= 0; y--)
                {
                    if (!RowHasAlpha(pixels, texture.width, 0, y, texture.width, 128)) continue;
                    headY = y;
                    break;
                }
            }
            bodyScales["teacher"] = 216f / Mathf.Max(1, headY - footY);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, footY / texture.height), 1, 0, SpriteMeshType.FullRect);
            sprite.name = "teacher standing";
            generatedSprites.Add(sprite);
            var frames = new[] { sprite };
            atlases["teacher"] = frames;
            return frames;
        }

        static bool RowHasAlpha(Color32[] pixels, int textureWidth, int cellX, int y, int width, byte threshold)
        {
            int start = y * textureWidth + cellX;
            for (int x = 0; x < width; x++) if (pixels[start + x].a > threshold) return true;
            return false;
        }

        static int CellBottom(Color32[] pixels, int textureWidth, int cellX, int cellY, int width, int height)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[(cellY + y) * textureWidth + cellX + x].a > 128) return y;
            return height / 16;
        }

        public void Render(float dt)
        {
            Ensure();
            sound.UpdateMusicState(battle, visible);
            if (!visible || battle == null) return;
            dt = Mathf.Clamp(dt, 0, .05f);
            // The root can render with dt=0 while paused, retaining all poses.
            clock += dt;
            if (battle.Stage != renderedStage) SetStage(battle.Stage);
            ReceiveEvents();
            RenderCamera(dt);
            for (int i = 0; i < fighters.Length; i++) RenderFighter(battle.Fighters[i], fighters[i], dt);
            RenderProjectiles(dt);
            RenderSparks(dt);
            RenderGhosts(dt);
            flash = Mathf.Max(0, flash - dt * .9f);
            flashOverlay.enabled = flash > 0;
            flashOverlay.color = new Color(1, .97f, .83f, flash);
            flashOverlay.transform.position = new Vector3(sceneCamera.transform.position.x, sceneCamera.transform.position.y, 0);
            flashOverlay.transform.localScale = new Vector3(sceneCamera.orthographicSize * sceneCamera.aspect * 2, sceneCamera.orthographicSize * 2, 1);
            RenderDebug();
        }

        void RenderCamera(float dt)
        {
            float separation = Mathf.Abs(battle.Fighters[0].X - battle.Fighters[1].X);
            float targetZoom = Mathf.Clamp(1280f / (separation + 740), 1, 1.12f);
            float targetX = (battle.Fighters[0].X + battle.Fighters[1].X) * .5f;
            float aspect = Mathf.Max(.1f, sceneCamera.aspect);
            float baseSize = Mathf.Max(360, 640 / aspect);
            float halfView = baseSize * aspect / targetZoom;
            targetX = Mathf.Clamp(targetX, Mathf.Min(640, halfView), Mathf.Max(640, 1280 - halfView));
            zoom = Mathf.Lerp(zoom, targetZoom, 1 - Mathf.Exp(-dt * 3));
            cameraX = Mathf.Lerp(cameraX, targetX, 1 - Mathf.Exp(-dt * 4));
            shake = Mathf.Max(0, shake - dt * 35);
            float shakeX = Mathf.Sin(clock * 183) * shake, shakeY = Mathf.Cos(clock * 227) * shake * .5f;
            sceneCamera.orthographicSize = baseSize / zoom;
            // Zoom about the floor so grounded feet stay at screen Y=570,
            // leaving the fixed touch-control area clear.
            float centerY = Floor + (baseSize - Floor) / zoom;
            sceneCamera.transform.position = new Vector3(cameraX + shakeX, centerY + shakeY, -10);
            // Fill wide phones and tablets, including safe-area margins and camera shake.
            float halfHeight=sceneCamera.orthographicSize;float halfWidth=halfHeight*aspect;
            float coverWidth=2*(halfWidth+Mathf.Abs(sceneCamera.transform.position.x-640)+16);
            float coverHeight=2*(halfHeight+Mathf.Abs(sceneCamera.transform.position.y-360)+16);
            stage.transform.localScale=new Vector3(Mathf.Max(1280,coverWidth)/stage.sprite.rect.width,Mathf.Max(720,coverHeight)/stage.sprite.rect.height,1);
        }

        void RenderFighter(Fighter f, FighterVisual v, float dt)
        {
            bool teacher = f.Club.Id == "teacher";
            string state = f.Animation;
            if (teacher) state = "idle";
            else if (f.Hp <= 0 || state == "down") state = "ko";
            else if ((battle.Phase == BattlePhase.RoundOver || battle.Phase == BattlePhase.MatchOver) && battle.RoundWinner == f.Index) state = "win";
            else if (state == "jump" && f.Vy < 0) state = "fall";
            if (v.State != state) { v.State = state; v.StateAge = 0; }
            v.StateAge += dt;
            v.Root.position = new Vector3(Mathf.Round(f.X), Mathf.Round(Floor + f.Y), 0);
            v.Body.flipX = f.Facing < 0;
            Sprite[] atlas = Atlas(f.Club.Id);
            if (atlas != null) v.Body.sprite = atlas[Mathf.Clamp(AnimationFrame(f, state, v.StateAge), 0, atlas.Length - 1)];
            v.Body.transform.localScale = Vector3.one * (bodyScales.TryGetValue(f.Club.Id, out float bodyScale) ? bodyScale : 1);
            v.Body.color = f.IsPoisoned && f.Flash > 0 ? new Color(.72f, 1, .59f) :
                teacher && f.Flash > 0 ? new Color(1, .67f, .54f) :
                f.Hitstun > 0 && Mathf.FloorToInt(v.StateAge * 26) % 2 == 0 ? new Color(1, .57f, .48f) : Color.white;
            v.Shadow.transform.position = new Vector3(f.X, Floor - 3, 0);
            float shadowScale = Mathf.Clamp(1 - f.Y / 540, .45f, 1);
            v.Shadow.transform.localScale = new Vector3(.82f * shadowScale, .72f * shadowScale, 1);
            v.Shadow.color = new Color(.035f, .07f, .08f, .37f * shadowScale);
            v.Guard.enabled = f.Guard;
            v.Guard.color = new Color(playerColors[f.Index].r, playerColors[f.Index].g, playerColors[f.Index].b, f.Club.Item ? .52f : .3f);
            v.Guard.transform.localScale = new Vector3(.64f + Mathf.Sin(clock * 9) * .012f, .79f, 1);
            v.HomeRing.enabled = battle.Mode != BattleMode.Practice && f.HomeAdvantage && f.Grounded && f.Hp > 0;
            for (int i = 0; i < v.Stars.Length; i++)
            {
                v.Stars[i].enabled = f.GuardBroken > 0;
                if (f.GuardBroken > 0)
                {
                    float a = clock * 6 + i * Mathf.PI * 2 / 3;
                    v.Stars[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 30, 199 + Mathf.Sin(a) * 7, 0);
                    v.Stars[i].transform.localScale = Vector3.one * .7f;
                    v.Stars[i].sortingOrder = Mathf.Sin(a) > 0 ? 9 : 21;
                }
            }
            RenderAttack(f, v);
            RenderHeldBall(f, v);
            RenderFighterEffects(f, v, dt);
        }

        void RenderFighterEffects(Fighter f, FighterVisual v, float dt)
        {
            v.TrailClock += dt; v.DustClock += dt; v.PoisonClock += dt;
            if (battle.Mode != BattleMode.Practice && v.TrailClock > .055f && (Mathf.Abs(f.Vx) > 140 || (f.Attack != null && f.Attack.Button == "b" && f.Attack.Move.Lunge > 0)))
            {
                v.TrailClock = 0;
                CaptureGhost(v.Body, Hex(f.Club.Color));
            }
            if (battle.Mode != BattleMode.Practice && f.Grounded && (!v.WasGrounded || (Mathf.Abs(f.Vx) > 140 && v.DustClock > .12f)))
            {
                v.DustClock = 0;
                GroundDust(f.X, !v.WasGrounded ? 5 : 2, f.Facing);
            }
            v.WasGrounded = f.Grounded;
            for (int i = 0; i < v.PoisonBubbles.Length; i++)
            {
                SpriteRenderer r = v.PoisonBubbles[i];
                r.enabled = f.IsPoisoned && f.Hp > 0;
                if (!r.enabled) continue;
                float q = (clock * .8f + i * .19f) % 1;
                r.transform.localPosition = new Vector3(Mathf.Sin(clock * 2 + i * 2.1f) * 35, 12 + q * 168, 0);
                r.transform.localScale = Vector3.one * (.35f + Mathf.Sin(i + clock * 2) * .09f);
                r.color = i % 2 == 0 ? new Color(.52f, 1, .35f, Mathf.Sin(q * Mathf.PI) * .65f) : new Color(.79f, .36f, .95f, Mathf.Sin(q * Mathf.PI) * .48f);
            }
            if (f.IsPoisoned && f.Hp > 0 && v.PoisonClock > .18f)
            {
                v.PoisonClock = 0;
                EmitFx(bubbleSprite, f.X + Mathf.Sin(clock * 7) * 20, Floor + f.Y + 40, new Color(.58f, 1, .34f, .48f),
                    .42f, new Vector2(.28f, .28f), new Vector2(0, 65), -15, .35f, 0, 0, 7);
            }
            // Ball games use one source-art ball layer until the simulation releases it.
            // Their extracted contact poses contain no second departing ball.
        }

        static int AnimationFrame(Fighter f, string state, float age)
        {
            if (f.Attack != null && (state.StartsWith("attack", StringComparison.Ordinal) || state == "airA" || state == "airB"))
            {
                AttackState a = f.Attack;
                if (f.Club.Id == "home" && a.Air && a.Button == "b")
                    return a.Elapsed < a.Move.Startup ? 12 : a.Elapsed < a.Move.Startup + a.Move.Active ? 13 : 7;
                if (a.Move.Kind == "bike") return f.IsBicycling ? 17 : a.Elapsed < a.Move.Startup ? 16 : 18;
                int start = a.Button == "b" ? 16 : 10 + Mathf.Clamp(a.Combo, 0, 2) * 2;
                float elapsed = a.Elapsed;
                if (a.Button == "b" && BallArtProfile.UsesBall(f.Club.Id))
                    return elapsed < a.Move.Startup ? 16 : elapsed < a.Move.Startup + a.Move.Active ? 17 : 18;
                if (a.Button == "b" && a.Move.Projectile != null)
                    return elapsed < a.Move.Startup * .75f ? 16 : elapsed < a.Move.Startup ? 17 : 18;
                if (elapsed < a.Move.Startup) return start;
                if (elapsed < a.Move.Startup + a.Move.Active) return start + 1;
                float recovery = Mathf.Max(.001f, a.Total - a.Move.Startup - a.Move.Active);
                float q = (elapsed - a.Move.Startup - a.Move.Active) / recovery;
                return a.Button == "b" ? start + 2 : q < .55f ? start + 1 : 0;
            }
            switch (state)
            {
                case "run": return 8 + Mathf.FloorToInt(age * 5) % 2;
                case "jump": return 6;
                case "fall": return 7;
                case "guard": return 8 + Mathf.FloorToInt(age * 4) % 2;
                case "hurt": return 19 + Mathf.Min(1, Mathf.FloorToInt(age * 9));
                case "win": return 21 + Mathf.FloorToInt(age * 4) % 2;
                case "ko": return 23;
                default: return Mathf.FloorToInt(age * 4) % 2;
            }
        }

        void RenderAttack(Fighter f, FighterVisual v)
        {
            AttackState a = f.Attack;
            bool active = a != null && a.Move.Projectile == null && a.Move.Kind != "bike" && a.Elapsed >= a.Move.Startup && a.Elapsed < a.Move.Startup + a.Move.Active;
            v.Trail.enabled = active;
            if (!active) return;
            float q = (a.Elapsed - a.Move.Startup) / Mathf.Max(.001f, a.Move.Active);
            Color c = Hex(a.Move.Color);
            c.a = (1 - q) * .55f;
            v.Trail.color = c;
            v.Trail.flipX = f.Facing < 0;
            float reach = a.Move.Reach;
            float middleY = (a.Move.Low + a.Move.High) * .5f;
            bool fist = a.Move.Kind == "punch" || a.Move.Kind == "throw";
            v.Trail.sprite = fist ? streakSprite : slashSprite;
            v.Trail.transform.localPosition = new Vector3(f.Facing * (15 + reach * .4f), middleY, 0);
            v.Trail.transform.localScale = new Vector3(Mathf.Max(.35f, reach / (fist ? 64 : 110)), fist ? .55f : (a.Move.High - a.Move.Low) / 128, 1);
            v.Trail.transform.localRotation = Quaternion.Euler(0, 0, f.Facing * Mathf.Lerp(-32, 25, q));
        }

        void RenderHeldBall(Fighter f, FighterVisual v)
        {
            bool show = f.Attack != null && f.Attack.Button == "b" && f.Attack.Move.Projectile != null
                && f.Attack.Elapsed < f.Attack.Move.Startup && ballArt.TryGetValue(f.Club.Id, out BallArtProfile profile);
            v.HeldBall.enabled = show;
            if (!show) return;
            BallArtProfile ball = ballArt[f.Club.Id];
            v.HeldBall.sprite = ball.Sprite;
            v.HeldBall.transform.localPosition = new Vector3(f.Facing * ball.WindupPosition.x, ball.WindupPosition.y, 0);
            v.HeldBall.transform.localScale = Vector3.one * (ball.WorldDiameter / Mathf.Max(ball.Sprite.rect.width, ball.Sprite.rect.height));
            v.HeldBall.flipX = f.Facing < 0; v.HeldBall.color = v.Body.color;
        }

        void RenderProjectiles(float dt)
        {
            while (projectiles.Count < battle.Projectiles.Count)
            {
                Transform root = new GameObject("Projectile").transform;
                root.SetParent(fxRoot, false);
                SpriteRenderer trail = CreateSprite("Velocity Trail", root, streakSprite, 8);
                SpriteRenderer body = CreateSprite("Ball or Club Projectile", root, null, 19);
                var tail = new SpriteRenderer[6];
                for (int j = 0; j < tail.Length; j++) tail[j] = CreateSprite("Short Arc Trail", root, smallRingSprite, 6);
                projectiles.Add(new ProjectileVisual { Root = root, Body = body, Trail = trail, Tail = tail, History = new Vector2[6] });
            }
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileVisual v = projectiles[i];
                bool used = i < battle.Projectiles.Count;
                v.Root.gameObject.SetActive(used);
                if (!used) continue;
                Projectile p = battle.Projectiles[i];
                string kind = ResolveProjectileKind(p.Kind, p.Owner);
                v.Root.position = new Vector3(p.X, Floor + p.Y, 0);
                v.Body.sprite = ProjectileSprite(kind);
                float scale = Mathf.Max(.3f, p.Radius * 2 / 64);
                if (kind == "arrow") scale = .75f;
                if (kind == "shuttle") scale = .58f;
                if (p.Owner >= 0 && p.Owner < battle.Fighters.Length && ballArt.TryGetValue(battle.Fighters[p.Owner].Club.Id, out BallArtProfile ball))
                    scale = ball.WorldDiameter / Mathf.Max(v.Body.sprite.rect.width, v.Body.sprite.rect.height);
                v.Body.transform.localScale = Vector3.one * scale;
                float angle = Mathf.Atan2(p.Vy, p.Vx) * Mathf.Rad2Deg;
                v.Body.transform.localRotation = Quaternion.Euler(0, 0, kind == "arrow" || kind == "shuttle" || kind == "water" ? angle : clock * (p.Vx > 0 ? -370 : 370));
                v.Body.color = kind == "note" || kind == "paint" || kind == "ink" ? Hex(p.Color) : Color.white;
                Color color = Hex(p.Color); color.a = .3f;
                v.Trail.color = color;
                float length = Mathf.Clamp(new Vector2(p.Vx, p.Vy).magnitude * .085f, 20, 76);
                v.Trail.transform.localScale = new Vector3(length / 64, Mathf.Max(3, p.Radius * .6f) / 16, 1);
                Vector2 direction = new Vector2(p.Vx, p.Vy).normalized;
                v.Trail.transform.localPosition = new Vector3(-direction.x * length * .45f, -direction.y * length * .45f, 0);
                v.Trail.transform.localRotation = Quaternion.Euler(0, 0, angle);
                if (v.Id != p.Id) { v.Id = p.Id; v.Samples = 0; }
                if (dt > 0)
                {
                    for (int j = v.History.Length - 1; j > 0; j--) v.History[j] = v.History[j - 1];
                    v.History[0] = new Vector2(p.X, Floor + p.Y);
                    v.Samples = Mathf.Min(v.History.Length, v.Samples + 1);
                }
                for (int j = 0; j < v.Tail.Length; j++)
                {
                    SpriteRenderer r = v.Tail[j];
                    r.enabled = j < v.Samples - 1;
                    r.transform.position = v.History[j + (j < v.History.Length - 1 ? 1 : 0)];
                    r.transform.localScale = Vector3.one * Mathf.Max(.06f, p.Radius / 50 * (1 - j / 7f));
                    r.color = new Color(color.r, color.g, color.b, .35f * (1 - j / 6f));
                }
            }
        }

        string ResolveProjectileKind(string kind, int owner)
        {
            string id = battle != null && owner >= 0 && owner < battle.Fighters.Length ? battle.Fighters[owner].Club.Id : "";
            if (kind == "ball") return id == "handball" ? "handball" : id == "basketball" ? "basketball" : "soccer";
            if (kind == "brush" || kind == "ink") return "ink";
            if (kind == "badminton") return "shuttle";
            return kind;
        }

        void ReceiveEvents()
        {
            foreach (BattleEvent e in battle.Events)
            {
                if (e.Id <= lastEventId) continue;
                lastEventId = e.Id;
                sound.Play(e);
                if (e.Type == "hit")
                {
                    Impact(e);
                }
                else if (e.Type == "block")
                {
                    Color color = e.Target >= 0 && battle.Fighters[e.Target].Club.Item ? new Color(.57f, .89f, 1) : new Color(1, .84f, .55f);
                    Burst(e.X, Floor + e.Y, color, 8, .65f);
                    EmitFx(smallRingSprite, e.X, Floor + e.Y, color, .16f, new Vector2(.48f, .7f), Vector2.zero, 0, .4f, 0, 0, 22);
                    shake = Mathf.Max(shake, 2);
                }
                else if (e.Type == "guardbreak")
                {
                    Color color = new Color(1, .73f, .26f);
                    Burst(e.X, Floor + e.Y, color, 18, 1.2f);
                    EmitFx(smallRingSprite, e.X, Floor + e.Y, color, .25f, Vector2.one * .65f, Vector2.zero, 0, 1.5f, 0, 0, 22);
                    EmitFx(impactSprite, e.X, Floor + e.Y, Color.white, .13f, Vector2.one * 1.0f, Vector2.zero, 0, .6f);
                    shake = Mathf.Max(shake, 8);
                }
                else if (e.Type == "jump" && e.Owner >= 0) GroundDust(battle.Fighters[e.Owner].X, 5, battle.Fighters[e.Owner].Facing);
                else if (e.Type == "projectile")
                {
                    string kind = ResolveProjectileKind(e.Kind, e.Owner);
                    Color color = Hex(e.Owner >= 0 ? battle.Fighters[e.Owner].Club.Color : "#ffffff");
                    Burst(e.X, Floor + e.Y, color, 5, .55f);
                    EmitFx(smallRingSprite, e.X, Floor + e.Y, new Color(color.r, color.g, color.b, .7f), .17f, Vector2.one * .35f, Vector2.zero, 0, .9f);
                    if (kind == "water" || kind == "ink" || kind == "paint") Liquid(e.X, Floor + e.Y, color, 7);
                }
                else if (e.Type == "poison") PoisonBurst(e.X, Floor + e.Y);
                else if (e.Type == "poisontick")
                {
                    PoisonBurst(e.X, Floor + e.Y, 3);
                    DamageNumber(e.Target, e.Damage, new Color(.66f, 1, .45f));
                }
                else if (e.Type == "roundend")
                {
                    int winner = battle.RoundWinner;
                    if (winner >= 0)
                    {
                        Fighter loser = battle.Fighters[1 - winner];
                        if (loser.Hp <= 0)
                        {
                            Burst(loser.X, Floor + loser.Y + 75, new Color(1, .82f, .42f), 22, 1.5f);
                            EmitFx(smallRingSprite, loser.X, Floor + loser.Y + 75, Color.white, .29f, Vector2.one * .75f, Vector2.zero, 0, 2);
                            GroundDust(loser.X, 7, loser.Facing);
                            shake = Mathf.Max(shake, 10); flash = Mathf.Max(flash, .12f);
                        }
                        Fighter victor = battle.Fighters[winner];
                        for (int n = 0; n < 7; n++)
                            EmitFx(starSprite, victor.X + (n - 3) * 18, Floor + victor.Y + 170, playerColors[winner], .6f,
                                Vector2.one * .5f, new Vector2((n - 3) * 30, 90 + n % 3 * 40), 350, -.3f, n * 35, 110);
                    }
                }
            }
        }

        void Impact(BattleEvent e)
        {
            string kind = ResolveProjectileKind(e.Kind, e.Owner);
            Color color = e.Damage >= 18 ? new Color(1, .78f, .29f) : new Color(1, .96f, .72f);
            float strength = e.Damage >= 18 ? 1.2f : .8f;
            float y = Floor + e.Y;
            bool liquid = kind == "water" || kind == "swim" || kind == "paint" || kind == "ink" || kind == "brush";
            if (liquid)
            {
                color = kind == "water" || kind == "swim" ? new Color(.4f, .86f, 1) : Hex(e.Owner >= 0 ? battle.Fighters[e.Owner].Club.Color : "#ffffff");
                Liquid(e.X, y, color, 13);
                EmitFx(smallRingSprite, e.X, y, color, .24f, Vector2.one * .6f, Vector2.zero, 0, 1.2f);
            }
            else if (kind == "flask")
            {
                PoisonBurst(e.X, y, 9);
                Liquid(e.X, y, new Color(.55f, 1, .33f), 9);
                for (int i = 0; i < 6; i++) EmitFx(shardSprite, e.X, y, new Color(.7f, .96f, 1), .32f,
                    Vector2.one * .8f, new Vector2(Mathf.Sin(i * 2.4f) * 180, 80 + i * 15), 450, -.3f, i * 55, 130);
            }
            else
            {
                bool weapon = kind == "slash" || kind == "swing" || kind == "thrust" || kind == "fan";
                if (weapon) color = new Color(.61f, .9f, 1);
                Burst(e.X, y, color, e.Damage >= 18 ? 16 : 11, strength);
                EmitFx(impactSprite, e.X, y, Color.white, .12f, Vector2.one * (weapon ? .8f : 1), Vector2.zero, 0, .5f, weapon ? 30 : 0);
                EmitFx(smallRingSprite, e.X, y, color, .19f, new Vector2(.45f, .45f), Vector2.zero, 0, 1.1f);
                if (weapon) EmitFx(streakSprite, e.X, y, Color.white, .13f, new Vector2(1.1f, .25f), Vector2.zero, 0, -.1f, 35);
                if (kind == "note") for (int i = 0; i < 3; i++) EmitFx(ProjectileSprite("note"), e.X, y, new Color(1, .55f, .88f), .35f,
                    Vector2.one * .28f, new Vector2((i - 1) * 100, 90 + i * 20), 200, -.2f, (i - 1) * 25, 60);
            }
            DamageNumber(e.Target, e.Damage, liquid || kind == "flask" ? color : new Color(1, .95f, .65f));
            shake = Mathf.Max(shake, e.Damage >= 18 ? 6 : 3.3f);
            if (e.Damage >= 24) flash = Mathf.Max(flash, .055f);
        }

        void PoisonBurst(float x, float y, int count = 7)
        {
            for (int i = 0; i < count; i++)
            {
                Color color = i % 2 == 0 ? new Color(.58f, 1, .29f, .8f) : new Color(.74f, .35f, .95f, .7f);
                EmitFx(bubbleSprite, x, y, color, .38f + i % 3 * .06f, Vector2.one * (.3f + i % 3 * .1f),
                    new Vector2(Mathf.Sin(i * 2.4f) * 95, 45 + i % 4 * 30), -30, .3f);
            }
        }

        void Liquid(float x, float y, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2;
                EmitFx(dropletSprite, x, y, color, .23f + i % 3 * .06f, Vector2.one * (.55f + i % 4 * .14f),
                    new Vector2(Mathf.Cos(a) * 160, Mathf.Sin(a) * 170 + 60), 560, -.25f, a * Mathf.Rad2Deg - 90, 60);
            }
            EmitFx(dustSprite, x, y, new Color(color.r, color.g, color.b, .3f), .21f, new Vector2(1.5f, .8f), Vector2.zero, 0, .6f);
        }

        void GroundDust(float x, int count, float facing)
        {
            Color color = renderedStage == StageId.Ground ? new Color(.94f, .82f, .56f, .55f) : new Color(.88f, .93f, .98f, .36f);
            for (int i = 0; i < count; i++) EmitFx(dustSprite, x + (i - count * .5f) * 10, Floor + 5, color,
                .24f + i % 2 * .04f, new Vector2(.5f + i % 3 * .18f, .35f),
                new Vector2(-facing * (25 + i % 3 * 30), 15 + i % 3 * 17), 95, .75f, 0, 0, 3);
        }

        void DamageNumber(int target, float damage, Color color)
        {
            if (target < 0 || target >= battle.Fighters.Length || damage <= 0) return;
            Fighter f = battle.Fighters[target];
            EmitFx(NumberSprite(Mathf.RoundToInt(damage)), f.X + Mathf.Sin(clock * 13) * 12, Floor + f.Y + 197, color,
                .55f, Vector2.one * 2.3f, new Vector2(0, 48), 75, -.15f, 0, 0, 28);
        }

        void Burst(float x, float y, Color color, int count, float strength)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (i + .5f) / count * Mathf.PI * 2 + clock;
                float velocity = (110 + i % 3 * 70) * strength;
                float size = (i % 3 == 0 ? 1.0f : .55f) * strength;
                EmitFx(i % 4 == 0 ? starSprite : sparkSprite, x, y, color, .17f + i % 4 * .025f, Vector2.one * size,
                    new Vector2(Mathf.Cos(angle) * velocity, Mathf.Sin(angle) * velocity), 380, -.35f, angle * Mathf.Rad2Deg, i % 4 == 0 ? 150 : 0);
            }
        }

        void EmitFx(Sprite sprite, float x, float y, Color color, float duration, Vector2 size, Vector2 velocity,
            float gravity = 0, float grow = 0, float rotation = 0, float spin = 0, int order = 24)
        {
            foreach (Spark spark in sparks)
            {
                if (spark.Active) continue;
                spark.Active = true; spark.Renderer.enabled = true;
                spark.Renderer.sprite = sprite; spark.Renderer.sortingOrder = order;
                spark.Age = 0; spark.Duration = duration;
                spark.Position = new Vector2(x, y); spark.Vx = velocity.x; spark.Vy = velocity.y;
                spark.Rotation = rotation; spark.Spin = spin; spark.Gravity = gravity; spark.Grow = grow;
                spark.Size = size; spark.Color = color;
                break;
            }
        }

        void RenderSparks(float dt)
        {
            foreach (Spark spark in sparks)
            {
                if (!spark.Active) continue;
                spark.Age += dt;
                if (spark.Age >= spark.Duration) { spark.Active = false; spark.Renderer.enabled = false; continue; }
                spark.Position.x += spark.Vx * dt;
                spark.Position.y += spark.Vy * dt;
                spark.Vy -= spark.Gravity * dt;
                spark.Rotation += spark.Spin * dt;
                spark.Renderer.transform.position = new Vector3(Mathf.Round(spark.Position.x), Mathf.Round(spark.Position.y), 0);
                spark.Renderer.transform.rotation = Quaternion.Euler(0, 0, spark.Rotation);
                float growth = Mathf.Max(.05f, 1 + spark.Age / spark.Duration * spark.Grow);
                spark.Renderer.transform.localScale = new Vector3(spark.Size.x * growth, spark.Size.y * growth, 1);
                Color color = spark.Color; color.a *= 1 - spark.Age / spark.Duration;
                spark.Renderer.color = color;
            }
        }

        void CaptureGhost(SpriteRenderer body, Color color)
        {
            foreach (Ghost ghost in ghosts)
            {
                if (ghost.Active) continue;
                ghost.Active = true; ghost.Age = 0; ghost.Duration = .18f;
                ghost.Renderer.enabled = true; ghost.Renderer.sprite = body.sprite;
                ghost.Renderer.flipX = body.flipX;
                ghost.Renderer.transform.position = body.transform.position;
                ghost.Renderer.transform.localScale = body.transform.lossyScale;
                ghost.Color = new Color(color.r, color.g, color.b, .19f);
                break;
            }
        }

        void RenderGhosts(float dt)
        {
            foreach (Ghost ghost in ghosts)
            {
                if (!ghost.Active) continue;
                ghost.Age += dt;
                if (ghost.Age >= ghost.Duration) { ghost.Active = false; ghost.Renderer.enabled = false; continue; }
                Color color = ghost.Color; color.a *= 1 - ghost.Age / ghost.Duration;
                ghost.Renderer.color = color;
            }
        }

        void RenderDebug()
        {
            int index = 0;
            if (debug)
            {
                foreach (HitBox box in battle.GetHitboxes())
                {
                    Color color = box.Type == "hurt" ? Color.cyan : box.Type == "projectile" ? Color.yellow : Color.red;
                    DebugRect(ref index, box.X, Floor + box.Y, box.Width, box.Height, color);
                }
            }
            for (int i = index; i < debugLines.Count; i++) debugLines[i].enabled = false;
        }

        void DebugRect(ref int index, float x, float y, float w, float h, Color color)
        {
            if (index >= debugLines.Count)
            {
                LineRenderer line = new GameObject("Debug Hitbox").AddComponent<LineRenderer>();
                line.transform.SetParent(fxRoot, false);
                line.sharedMaterial = lineMaterial;
                line.widthMultiplier = 2;
                line.useWorldSpace = true;
                line.positionCount = 5;
                line.sortingOrder = 50;
                debugLines.Add(line);
            }
            LineRenderer r = debugLines[index++];
            r.enabled = true;
            r.startColor = r.endColor = color;
            lineBuffer[0] = new Vector3(x, y, 0); lineBuffer[1] = new Vector3(x + w, y, 0);
            lineBuffer[2] = new Vector3(x + w, y + h, 0); lineBuffer[3] = new Vector3(x, y + h, 0); lineBuffer[4] = lineBuffer[0];
            r.SetPositions(lineBuffer);
        }

        SpriteRenderer CreateSprite(string name, Transform parent, Sprite sprite, int sortingOrder)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            if (lineMaterial != null) renderer.sharedMaterial = lineMaterial;
            return renderer;
        }

        static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value ?? "#ffffff", out color) ? color : Color.white;
        }

        Sprite PixelSprite(int width, int height, Func<int, int, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var colors = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) colors[y * width + x] = pixel(x, y);
            texture.SetPixels(colors); texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
            generatedTextures.Add(texture); generatedSprites.Add(sprite);
            return sprite;
        }

        void CreateUtilitySprites()
        {
            Color clear = Color.clear;
            whitePixel = PixelSprite(1, 1, (x, y) => Color.white);
            shadowSprite = PixelSprite(128, 32, (x, y) => Mathf.Pow((x - 63.5f) / 62, 2) + Mathf.Pow((y - 15.5f) / 14, 2) <= 1 ? Color.white : clear);
            ringSprite = PixelSprite(256, 256, (x, y) =>
            {
                float d = Mathf.Sqrt((x - 127.5f) * (x - 127.5f) + (y - 127.5f) * (y - 127.5f));
                return d > 119 && d < 125 ? Color.white : d <= 119 ? new Color(1, 1, 1, .08f) : clear;
            });
            slashSprite = PixelSprite(128, 128, (x, y) =>
            {
                float dx = x - 48, dy = y - 64, d = Mathf.Sqrt(dx * dx + dy * dy);
                float inner = Mathf.Sqrt((x - 35) * (x - 35) + dy * dy);
                return d < 61 && inner > 51 && x > 32 ? Color.white : clear;
            });
            sparkSprite = PixelSprite(24, 12, (x, y) => Mathf.Abs(y - 5.5f) < (1 - Mathf.Abs(x - 11.5f) / 12) * 4 ? Color.white : clear);
            starSprite = PixelSprite(24, 24, (x, y) =>
            {
                float dx = Mathf.Abs(x - 11.5f), dy = Mathf.Abs(y - 11.5f);
                return dx + dy < 10 && (dx < 3 || dy < 3 || dx + dy < 6) ? Color.white : clear;
            });
            smallRingSprite = PixelSprite(64, 64, (x, y) =>
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude;
                return d > 25 && d < 29 ? Color.white : clear;
            });
            impactSprite = PixelSprite(64, 64, (x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float angle = Mathf.Atan2(dy, dx);
                float boundary = 12 + 17 * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4)), 5);
                return dx * dx + dy * dy < boundary * boundary ? Color.white : clear;
            });
            streakSprite = PixelSprite(64, 16, (x, y) =>
            {
                float height = (x + 3) / 67f * 7;
                return Mathf.Abs(y - 7.5f) < height ? new Color(1, 1, 1, .2f + x / 80f) : clear;
            });
            dustSprite = PixelSprite(32, 20, (x, y) =>
            {
                bool shape = Mathf.Pow((x - 15) / 13f, 2) + Mathf.Pow((y - 7) / 6f, 2) < 1
                    || Mathf.Pow((x - 10) / 7f, 2) + Mathf.Pow((y - 10) / 7f, 2) < 1
                    || Mathf.Pow((x - 23) / 7f, 2) + Mathf.Pow((y - 7) / 7f, 2) < 1;
                return shape ? Color.white : clear;
            });
            dropletSprite = PixelSprite(16, 24, (x, y) =>
            {
                float dx = Mathf.Abs(x - 7.5f);
                bool shape = (x - 7.5f) * (x - 7.5f) + (y - 7) * (y - 7) < 43 || (y >= 7 && y < 23 && dx < (23 - y) * .4f);
                return shape ? Color.white : clear;
            });
            bubbleSprite = PixelSprite(24, 24, (x, y) =>
            {
                float d = new Vector2(x - 11.5f, y - 11.5f).magnitude;
                if (d > 10) return clear;
                if (d > 8 || (x > 13 && x < 18 && y > 14 && y < 18)) return Color.white;
                return new Color(1, 1, 1, .15f);
            });
            shardSprite = PixelSprite(16, 16, (x, y) => x > 2 && x < 14 && y > 3 && y < x + 1 && y > x * .3f ? Color.white : clear);
        }

        static readonly string[] DigitMasks = {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111", "000000111000000"
        };

        Sprite NumberSprite(int damage)
        {
            damage = Mathf.Clamp(damage, 0, 999);
            if (numberSprites.TryGetValue(damage, out Sprite result)) return result;
            string text = "-" + damage;
            result = PixelSprite(text.Length * 4 + 3, 9, (x, y) =>
            {
                if (DigitPixel(text, x, y)) return Color.white;
                if (DigitPixel(text, x - 1, y) || DigitPixel(text, x + 1, y) || DigitPixel(text, x, y - 1) || DigitPixel(text, x, y + 1)) return new Color(.05f, .075f, .11f);
                return Color.clear;
            });
            numberSprites[damage] = result;
            return result;
        }

        static bool DigitPixel(string text, int x, int y)
        {
            x -= 2; int row = 6 - y;
            if (x < 0 || row < 0 || row >= 5 || x / 4 >= text.Length || x % 4 == 3) return false;
            char c = text[x / 4]; int digit = c == '-' ? 10 : c - '0';
            return DigitMasks[digit][row * 3 + x % 4] == '1';
        }

        Sprite ProjectileSprite(string kind)
        {
            if (projectileSprites.TryGetValue(kind, out Sprite sprite)) return sprite;
            sprite = PixelSprite(64, 64, (x, y) => ProjectilePixel(kind, x, y));
            projectileSprites[kind] = sprite;
            return sprite;
        }

        static Color ProjectilePixel(string kind, int x, int y)
        {
            Color clear = Color.clear, ink = new Color(.07f, .09f, .13f), ivory = new Color(.99f, .98f, .89f), shade = new Color(.65f, .7f, .7f);
            float dx = x - 31.5f, dy = y - 31.5f, distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (kind == "water")
            {
                float middle = 31.5f + Mathf.Sin(x * .14f) * 8;
                float radius = 4 + x / 64f * 17;
                if (x < 5 || x > 59 || Mathf.Abs(y - middle) > radius) return clear;
                if (Mathf.Abs(y - middle) > radius - 3) return new Color(.15f, .51f, .86f);
                if (y > middle + 3) return new Color(.82f, .97f, 1);
                return new Color(.28f, .8f, 1);
            }
            if (kind == "ink")
            {
                float bound = 22 + Mathf.Sin(Mathf.Atan2(dy, dx) * 5) * 5;
                if (distance > bound) return clear;
                return distance > bound - 3 ? new Color(.25f, .26f, .31f) : new Color(.72f, .73f, .76f);
            }
            if (kind == "shuttle")
            {
                if (x > 40 && x < 57 && Mathf.Abs(dy) < 7) return x > 52 ? new Color(.85f, .67f, .38f) : ivory;
                bool feathers = x >= 9 && x <= 44 && Mathf.Abs(dy) < (46 - x) * .56f;
                if (!feathers) return clear;
                if (x % 7 < 2 || Mathf.Abs(dy) > (46 - x) * .56f - 2) return shade;
                return ivory;
            }
            if (kind == "arrow")
            {
                if (x > 49 && Mathf.Abs(dy) < (62 - x) * .55f) return ivory;
                if (x > 4 && x < 56 && Mathf.Abs(dy) < 2) return new Color(.65f, .41f, .18f);
                if (x < 16 && x > 3 && Mathf.Abs(dy) < 9 && Mathf.Abs(dy) > 2 && x + Mathf.Abs(dy) < 20) return ivory;
                return clear;
            }
            if (kind == "note")
            {
                if (((x - 19) * (x - 19) / 110f + (y - 16) * (y - 16) / 64f < 1) || ((x - 47) * (x - 47) / 110f + (y - 22) * (y - 22) / 64f < 1) || (x >= 24 && x <= 29 && y >= 17 && y <= 51) || (x >= 51 && x <= 56 && y >= 23 && y <= 55) || (x >= 25 && x <= 56 && y >= 46 && y <= 53)) return Color.white;
                return clear;
            }
            if (kind == "tile")
            {
                bool body = y > 8 && y < 53 && Mathf.Abs(dx) < (y > 42 ? (60 - y) * 1.5f : 21);
                if (!body) return clear;
                if (x < 14 || x > 49 || y < 12) return new Color(.44f, .24f, .12f);
                if ((Mathf.Abs(dx) < 2 && y > 19 && y < 40) || (y > 28 && y < 32 && x > 20 && x < 44) || (y > 40 && y < 43 && x > 22 && x < 42)) return ink;
                return new Color(.96f, .78f, .45f);
            }
            if (kind == "flask")
            {
                bool neck = x > 26 && x < 38 && y > 33 && y < 57;
                bool bulb = dy < 7 && distance < 25 && y > 9;
                if (!(neck || bulb)) return clear;
                if (x < 13 || x > 51 || y < 12 || y > 53 || ((x == 27 || x == 37) && y > 32)) return new Color(.52f, .89f, 1);
                if (y < 27) return new Color(.27f, .84f, .64f);
                return new Color(.72f, .97f, 1, .82f);
            }
            if (kind == "paint")
            {
                float boundary = 22 + Mathf.Sin(Mathf.Atan2(dy, dx) * 7) * 5;
                if (distance > boundary) return clear;
                return distance < boundary - 3 ? Color.white : new Color(.63f, .63f, .63f);
            }
            if (distance > 29) return clear;
            if (distance > 26.5f) return ink;
            if (kind == "soccer")
            {
                bool centre = Mathf.Abs(dx) + Mathf.Abs(dy) * .75f < 11;
                bool panels = (x < 14 && y > 20 && y < 43) || (x > 49 && y > 23 && y < 44) || (y > 49 && x > 20 && x < 42) || (y < 13 && x > 22 && x < 46);
                if (centre || panels) return ink;
                if (x > 41 || y < 20) return shade;
                return ivory;
            }
            if (kind == "baseball")
            {
                if ((Mathf.Abs(dx + 12 - dy * dy / 55) < 1.5f || Mathf.Abs(dx - 12 + dy * dy / 55) < 1.5f) && Mathf.Abs(dy) < 23) return new Color(.86f, .25f, .3f);
                return x > 42 || y < 19 ? shade : ivory;
            }
            if (kind == "volleyball")
            {
                if (Mathf.Abs(dx + dy * .32f) < 3 || Mathf.Abs(dy - dx * .43f) < 3) return new Color(.17f, .35f, .75f);
                return x > 40 || y < 15 ? new Color(.78f, .64f, .18f) : new Color(1, .88f, .37f);
            }
            if (kind == "tennis")
            {
                if (Mathf.Abs(dx + 12 - dy * dy / 70) < 2 || Mathf.Abs(dx - 12 + dy * dy / 70) < 2) return ivory;
                return x > 39 || y < 16 ? new Color(.51f, .66f, .13f) : new Color(.83f, .98f, .33f);
            }
            if (kind == "golf")
            {
                if (x % 8 < 2 && y % 8 < 2) return shade;
                return x > 41 || y < 16 ? shade : ivory;
            }
            if (kind == "basketball")
            {
                if (Mathf.Abs(dx) < 2 || Mathf.Abs(dy) < 2 || Mathf.Abs(dx - dy * dy / 45 + 11) < 2 || Mathf.Abs(dx + dy * dy / 45 - 11) < 2) return ink;
                return x > 41 || y < 15 ? new Color(.71f, .25f, .08f) : new Color(1, .55f, .17f);
            }
            if (kind == "handball")
            {
                if (Mathf.Abs(dx + dy * .5f) < 3 || Mathf.Abs(dy - dx * .55f) < 3) return new Color(.19f, .37f, .69f);
                return x > 41 || y < 15 ? new Color(.72f, .59f, .23f) : new Color(1, .88f, .45f);
            }
            return ivory;
        }

        void OnDestroy()
        {
            foreach (Sprite sprite in generatedSprites) if (sprite != null) Destroy(sprite);
            foreach (Texture2D texture in generatedTextures) if (texture != null) Destroy(texture);
            if (ownsMaterial && lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
