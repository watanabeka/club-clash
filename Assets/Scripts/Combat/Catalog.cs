using System;
using System.Collections.Generic;

namespace ClubClash
{
    public enum StageId { Ground, Classroom, Gym }
    public static class World
    {
        public const float Width = 1280, Height = 720, FloorY = 570;
        public const float FighterWidth = 65, BodyHeight = 180, Gravity = 2100;
    }

    [Serializable]
    public sealed class ClubStats
    {
        public float Speed, Jump, GuardScale, GuardCost, GuardMove;
        public ClubStats Copy() { return (ClubStats)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class ProjectileDefinition
    {
        public string Kind;
        public float Speed, Radius, Gravity, Vy, Life, Range, Height = 100;
        public ProjectileDefinition Copy() { return (ProjectileDefinition)MemberwiseClone(); }
    }

    // Stable move and club IDs are the extension points for future equipment or progression.
    // No levels, purchases, or permanent bonuses are enabled in this version.
    [Serializable]
    public sealed class MoveDefinition
    {
        public string Id, Button, Kind, Name, Color;
        public float Damage, Reach, Startup, Active, Total, Cooldown;
        public float Low = 35, High = 150, Knockback = 110, Stun = .16f, GuardDamage;
        public float Lunge;
        public float ChargeDistance;
        public int PoisonTicks;
        public float PoisonTickDamage = 2;
        public ProjectileDefinition Projectile;
        public MoveDefinition Copy()
        {
            var copy = (MoveDefinition)MemberwiseClone();
            if (Projectile != null) copy.Projectile = Projectile.Copy();
            return copy;
        }
    }

    [Serializable]
    public sealed class ClubDefinition
    {
        public string Id, Name, Color;
        public bool Item;
        public StageId HomeStage;
        public int PowerRating, SpeedRating, GuardRating;
        public ClubStats Stats;
        public MoveDefinition[] LightMoves;
        public MoveDefinition StrongMove;

        public ClubDefinition Copy()
        {
            var copy = (ClubDefinition)MemberwiseClone();
            copy.Stats = Stats == null ? null : Stats.Copy();
            if (LightMoves != null)
            {
                copy.LightMoves = new MoveDefinition[LightMoves.Length];
                for (int i = 0; i < LightMoves.Length; i++)
                    copy.LightMoves[i] = LightMoves[i] == null ? null : LightMoves[i].Copy();
            }
            copy.StrongMove = StrongMove == null ? null : StrongMove.Copy();
            return copy;
        }
    }

    public static class Catalog
    {
        static readonly List<ClubDefinition> all = new List<ClubDefinition>();
        static readonly Dictionary<string, ClubDefinition> byId = new Dictionary<string, ClubDefinition>();
        public static IReadOnlyList<ClubDefinition> Clubs { get { return all; } }
        // A practice target is not a selectable club or a combatant with stage bonuses.
        public static readonly ClubDefinition Teacher = new ClubDefinition {
            Id = "teacher", Name = "体育教師", Color = "#a8d5d1", Item = false,
            HomeStage = (StageId)(-1), PowerRating = 1, SpeedRating = 1, GuardRating = 1,
            Stats = new ClubStats { Speed = 0, Jump = 0, GuardScale = .25f, GuardCost = 1, GuardMove = 0 },
            LightMoves = new[] { new MoveDefinition(), new MoveDefinition(), new MoveDefinition() },
            StrongMove = new MoveDefinition()
        };

        static Catalog()
        {
            Add("home", "帰宅部", "#8ca4cf", false, 3, 4, 3, 68, "punch",
                new MoveDefinition { Button = "b", Kind = "bike", Name = "自転車", Reach = 88,
                    Startup = .30f, Active = .80f, Total = 1.20f, Cooldown = 2.20f,
                    Lunge = 800, ChargeDistance = 640, Low = 25, High = 150, Knockback = 150, Stun = .24f },
                (StageId)(-1));
            Add("kendo", "剣道", "#45b8ff", true, 8, 5, 9, 125, "slash",
                Melee("thrust", 170, .30f, .14f, .95f, .95f, 240, 180), StageId.Gym);
            Add("soccer", "サッカー", "#5add97", false, 6, 7, 4, 85, "kick",
                Shot("soccer", 620, 15, .40f, .95f, 1.90f, 500, 240, 35, 760, 1.30f));
            Add("baseball", "野球", "#ffae58", true, 9, 5, 8, 118, "swing",
                Shot("baseball", 760, 11, .42f, 1.00f, 2.00f, 600, 50, 160, 790, 1.15f));
            Add("volleyball", "バレー", "#ffe56c", false, 6, 8, 4, 80, "palm",
                Shot("volleyball", 650, 18, .38f, .93f, 1.85f, 220, -65, 190, 780, 1.35f), StageId.Gym);
            Add("tennis", "テニス", "#cdff78", true, 6, 6, 7, 108, "swing",
                Shot("tennis", 850, 9, .34f, .88f, 1.80f, 260, -70, 190, 870, 1.20f));
            Add("golf", "ゴルフ", "#7de2c3", true, 10, 3, 10, 132, "swing",
                Shot("golf", 720, 10, .52f, 1.12f, 2.20f, 0, 0, 32, 780, 1.12f));
            Add("boxing", "ボクシング", "#ff7286", false, 8, 10, 3, 65, "punch",
                Melee("punch", 90, .17f, .11f, .60f, .80f, 210, 130), StageId.Gym);
            Add("archery", "弓道", "#ab94ff", true, 7, 4, 8, 123, "swing",
                Shot("arrow", 820, 8, .48f, 1.02f, 2.10f, 20, 0, 125, 840, 1.05f));
            Add("music", "軽音", "#f391e4", true, 7, 5, 8, 112, "swing",
                Shot("note", 470, 21, .38f, .95f, 1.85f, 0, 0, 95, 710, 1.55f), StageId.Classroom);
            Add("art", "美術", "#ffaaab", true, 5, 5, 8, 119, "brush",
                Shot("paint", 430, 23, .40f, 1.00f, 1.90f, 0, 0, 95, 650, 1.55f), StageId.Classroom);
            Add("science", "科学", "#72eaf5", true, 8, 1, 6, 75, "flask",
                Shot("flask", 280, 16, .48f, 1.05f, 2.10f, 340, 240, 135, 580, 2.40f), StageId.Classroom);
            Add("shogi", "将棋", "#dac29a", true, 6, 6, 7, 96, "fan",
                Shot("tile", 540, 17, .42f, .98f, 1.95f, 0, 0, 95, 760, 1.45f), StageId.Classroom);
            Add("handball", "ハンドボール", "#ffad69", false, 7, 8, 4, 82, "ball",
                Shot("handball", 680, 15, .39f, .94f, 1.90f, 600, 90, 160, 760, 1.20f));
            Add("swimming", "水泳", "#64d6ff", false, 6, 9, 3, 72, "swim",
                Shot("water", 560, 17, .37f, .90f, 1.80f, 0, 0, 110, 610, 1.12f));
            Add("basketball", "バスケ", "#ff9a47", false, 7, 8, 4, 82, "ball",
                Shot("basketball", 620, 19, .40f, .98f, 1.90f, 600, 180, 160, 750, 1.30f), StageId.Gym);
            Add("badminton", "バドミントン", "#a7f6d5", true, 6, 7, 6, 105, "swing",
                Shot("shuttle", 760, 12, .35f, .90f, 1.85f, 300, -70, 190, 770, 1.12f), StageId.Gym);
            Add("judo", "柔道", "#f7e2c1", false, 9, 9, 5, 60, "throw",
                Melee("throw", 82, .19f, .14f, .65f, .85f, 230, 120), StageId.Gym);
            Add("calligraphy", "書道", "#b998ef", true, 6, 5, 8, 121, "brush",
                Shot("ink", 450, 21, .43f, 1.00f, 1.95f, 0, 0, 105, 650, 1.50f), StageId.Classroom);

            var science = Get("science").StrongMove;
            science.Damage = 8; science.GuardDamage = 0; science.PoisonTicks = 7; science.PoisonTickDamage = 2;
            Get("volleyball").StrongMove.Damage = 25;
            Get("tennis").StrongMove.Damage = 24;
            foreach (var move in Get("volleyball").LightMoves) { move.Kind = "ball"; move.Name = "ボール振り"; }
            Get("swimming").LightMoves[0].Name = "クロール";
            Get("swimming").LightMoves[1].Name = "バタフライ";
            Get("swimming").LightMoves[2].Name = "クロール";
            Get("home").LightMoves[0].Name = "パンチ";
            Get("home").LightMoves[1].Kind = "kick"; Get("home").LightMoves[1].Name = "キック";
            Get("home").LightMoves[2].Name = "パンチ";
        }

        public static ClubDefinition Get(string id)
        {
            if (id == "teacher") return Teacher;
            ClubDefinition value;
            return id != null && byId.TryGetValue(id, out value) ? value : all[0];
        }

        static MoveDefinition Melee(string kind, float reach, float startup, float active,
            float total, float cooldown, float knockback, float lunge)
        {
            return new MoveDefinition { Button = "b", Kind = kind, Name = "強", Reach = reach,
                Startup = startup, Active = active, Total = total, Cooldown = cooldown,
                Knockback = knockback, Lunge = lunge, Stun = .25f, Low = 30, High = 170 };
        }

        static MoveDefinition Shot(string kind, float speed, float radius, float startup,
            float total, float cooldown, float gravity = 0, float vy = 0, float height = 100,
            float range = 760, float life = 1.5f)
        {
            return new MoveDefinition { Button = "b", Kind = kind, Name = "強", Startup = startup,
                Active = .075f, Total = total, Cooldown = cooldown, Knockback = 90, Stun = .17f,
                Projectile = new ProjectileDefinition { Kind = kind, Speed = speed, Radius = radius,
                    Gravity = gravity, Vy = vy, Height = height, Range = range, Life = life } };
        }

        static void Add(string id, string name, string color, bool item, int power, int speed,
            int guard, float reach, string kind, MoveDefinition strong, StageId homeStage = StageId.Ground)
        {
            // A narrower strike always starts and recovers faster. Movement and guard use
            // the displayed 10-point ratings directly, so the selection screen is truthful.
            float reachFactor = Math.Max(0, (reach - 60) / 72);
            float startup = .040f + .095f * reachFactor;
            float total = .18f + .20f * reachFactor;
            float[] totals = { total, total + .02f + .02f * reachFactor, total + .10f + .08f * reachFactor };
            var lights = new MoveDefinition[3];
            for (int i = 0; i < 3; i++)
            {
                lights[i] = new MoveDefinition { Id = id + ".a" + (i + 1), Button = "a", Name = "攻撃",
                    Kind = kind, Color = color, Reach = reach + i * 8, Startup = startup + (i == 2 ? .045f : i * .01f),
                    Active = .060f + i * .01f, Total = totals[i], Damage = (float)Math.Round((9 + i * 3) * (.65f + power * .045f)),
                    Knockback = 45 + i * 35, Stun = .12f + i * .025f,
                    GuardDamage = 12 + power + i * 4 };
            }
            strong.Id = id + ".b";
            strong.Color = color;
            strong.Damage = (float)Math.Round(strong.Projectile == null ? 19 + power : 13 + power * .8f);
            strong.GuardDamage = strong.Projectile == null ? 39 : 31;
            var club = new ClubDefinition { Id = id, Name = name, Color = color, Item = item,
                PowerRating = power, SpeedRating = speed, GuardRating = guard, HomeStage = homeStage,
                Stats = new ClubStats { Speed = 100 + speed * 32, Jump = item ? 740 : 790,
                    GuardScale = .28f - guard * .018f, GuardCost = 1.22f - guard * .06f,
                    GuardMove = item ? .32f : .48f }, LightMoves = lights, StrongMove = strong };
            all.Add(club);
            byId.Add(id, club);
        }
    }
}
